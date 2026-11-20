using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DiscordTunnelPrototype
{
    // An adversarial regression probe. Exit 1 means the protection FAILED.
    // All sockets, keys and executable fixtures are local and synthetic. No TUN is opened.
    internal static class AttributionRepro
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length >= 2 && args[0] == "--sender")
                {
                    using (var socket = new UdpClient())
                    {
                        socket.Connect(IPAddress.Loopback, Int32.Parse(args[1]));
                        var bytes = Encoding.ASCII.GetBytes("synthetic-attribution-probe");
                        socket.Send(bytes, bytes.Length);
                        if (args.Length == 3) Thread.Sleep(8000);
                    }
                    return 0;
                }
                if (args.Length != 1) return 2;
                string root = Path.GetFullPath(args[0]);
                string app = Path.Combine(root, "app-1.0.9269");
                Directory.CreateDirectory(app);
                string sender = Path.Combine(app, "Discord.exe");
                File.Copy(Assembly.GetExecutingAssembly().Location, sender, false);
                if (RunCase(root, sender, false))
                    throw new InvalidOperationException("Live process baseline escaped instead of selecting WireGuard");
                Console.WriteLine("BASELINE - matching live fixture selected WireGuard; local peer received a handshake request");
                bool escaped = RunCase(root, sender, true);
                Console.WriteLine(escaped
                    ? "FAIL - queued UDP reached final direct after its matching sender exited before process attribution"
                    : "NO DIRECT DELIVERY OBSERVED - investigate the route; this alone is not complete fail-closed acceptance");
                return escaped ? 1 : 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }

        private static bool RunCase(string root, string senderPath, bool loseProcess)
        {
            using (var destination = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                int inputPort;
                using (var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                    inputPort = ((IPEndPoint)reservation.Client.LocalEndPoint).Port;
                var privateKey = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray());
                var publicKey = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray());
                var profile = "[Interface]\nPrivateKey=" + privateKey + "\nAddress=10.77.0.2/32,fd77::2/128\n" +
                    "[Peer]\nPublicKey=" + publicKey + "\nEndpoint=127.0.0.1:" + ((IPEndPoint)peer.Client.LocalEndPoint).Port +
                    "\nAllowedIPs=0.0.0.0/0,::/0\n";
                var json = new JavaScriptSerializer();
                var config = json.Deserialize<Dictionary<string, object>>(TunnelConfig.Build(profile, root));
                config["inbounds"] = new[] { new { type = "direct", tag = "attribution-probe", listen = "127.0.0.1",
                    listen_port = inputPort, network = "udp", override_address = "127.0.0.1",
                    override_port = ((IPEndPoint)destination.Client.LocalEndPoint).Port } };
                var route = (Dictionary<string, object>)config["route"];
                route["auto_detect_interface"] = false;
                ((Dictionary<string, object>)((IList)route["rules"])[1])["inbound"] = "attribution-probe";
                using (var engine = new Engine(json.Serialize(config), false, null))
                {
                    try
                    {
                        if (!engine.Ready.Task.Wait(8000) || !engine.Ready.Task.Result)
                            throw new InvalidOperationException("Isolated engine did not become ready");
                        var engineProcess = (Process)typeof(Engine).GetField("process", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(engine);
                        bool attached = false;
                        Process sender = null;
                        try
                        {
                            if (loseProcess)
                            {
                                if (!DebugActiveProcess(engineProcess.Id)) throw new Win32Exception(Marshal.GetLastWin32Error());
                                attached = true;
                                if (!DebugSetProcessKillOnExit(false)) throw new Win32Exception(Marshal.GetLastWin32Error());
                            }
                            sender = Process.Start(new ProcessStartInfo(senderPath, "--sender " + inputPort + (loseProcess ? "" : " hold"))
                                { UseShellExecute = false, CreateNoWindow = true });
                            if (loseProcess)
                            {
                                if (!sender.WaitForExit(3000) || sender.ExitCode != 0)
                                    throw new InvalidOperationException("Synthetic sender did not exit after queuing its datagram");
                                if (!DebugActiveProcessStop(engineProcess.Id)) throw new Win32Exception(Marshal.GetLastWin32Error());
                                attached = false;
                            }
                            else
                            {
                                peer.Client.ReceiveTimeout = 2000;
                                IPEndPoint source = null;
                                byte[] handshake = peer.Receive(ref source);
                                if (handshake.Length != 148 || handshake[0] != 1)
                                    throw new InvalidOperationException("Expected a WireGuard initiation from the matching live fixture");
                            }
                            destination.Client.ReceiveTimeout = 2000;
                            try
                            {
                                IPEndPoint source = null;
                                return Encoding.ASCII.GetString(destination.Receive(ref source)) == "synthetic-attribution-probe";
                            }
                            catch (SocketException error)
                            {
                                if (error.SocketErrorCode != SocketError.TimedOut) throw;
                                return false;
                            }
                        }
                        finally
                        {
                            if (attached && !DebugActiveProcessStop(engineProcess.Id))
                                throw new Win32Exception(Marshal.GetLastWin32Error());
                            if (sender != null)
                            {
                                if (!sender.HasExited) { sender.Kill(); sender.WaitForExit(); }
                                sender.Dispose();
                            }
                        }
                    }
                    finally { engine.Stop().GetAwaiter().GetResult(); }
                }
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DebugActiveProcess(int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DebugActiveProcessStop(int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DebugSetProcessKillOnExit(bool killOnExit);
    }
}
