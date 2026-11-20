using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DiscordTunnelPrototype
{
    internal static class WireGuardLoopback
    {
        internal static void Run()
        {
            DirectFallback();
            var peer = GenerateKeys();
            var client = GenerateKeys();
            var ports = HoldPorts();
            using (var udp4 = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (var udp6 = new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, 0)))
            {
                var tcp4 = new TcpListener(IPAddress.Loopback, 0);
                var tcp6 = new TcpListener(IPAddress.IPv6Loopback, 0);
                try
                {
                    tcp4.Start();
                    tcp6.Start();
                    var targets = new[] { ((IPEndPoint)tcp4.LocalEndpoint).Port, ((IPEndPoint)tcp6.LocalEndpoint).Port,
                        ((IPEndPoint)udp4.Client.LocalEndPoint).Port, ((IPEndPoint)udp6.Client.LocalEndPoint).Port };
                    var serializer = new JavaScriptSerializer();
                    var clientNetwork = new[] { "10.77.0.2/32", "fd77::2/128" };
                    var peerNetwork = new[] { "10.77.0.1/32", "fd77::1/128" };
                    var profile = "[Interface]\nPrivateKey=" + client[0] + "\nAddress=" + string.Join(",", clientNetwork)
                        + "\n[Peer]\nPublicKey=" + peer[1] + "\nEndpoint=127.0.0.1:" + ports[0]
                        + "\nAllowedIPs=0.0.0.0/0,::/0\n";
                    var config = serializer.Deserialize<Dictionary<string, object>>(TunnelConfig.Build(profile, TunnelConfig.DiscordRoot));
                    var localInbounds = targets.Select((target, i) => new
                    {
                        type = "direct", tag = "probe-" + i, listen = "127.0.0.1", listen_port = ports[i + 1],
                        network = i < 2 ? "tcp" : "udp", override_address = i % 2 == 0 ? "10.77.0.1" : "fd77::1",
                        override_port = target
                    }).ToArray();
                    config["inbounds"] = localInbounds;
                    var routing = (Dictionary<string, object>)config["route"];
                    routing["auto_detect_interface"] = false;
                    routing["rules"] = new object[] { ((System.Collections.IList)routing["rules"])[0],
                        ((System.Collections.IList)routing["rules"])[1], new { action = "reject" } };
                    var rules = (System.Collections.IList)routing["rules"];
                    var targetRule = (Dictionary<string, object>)rules[1];
                    targetRule["inbound"] = localInbounds.Select(inbound => inbound.tag).ToArray();
                    using (var currentProcess = Process.GetCurrentProcess())
                        targetRule["process_path_regex"] = new[] { "(?i)^" +
                            System.Text.RegularExpressions.Regex.Escape(currentProcess.MainModule.FileName) + "$" };

                    var peerConfig = serializer.Serialize(new
                    {
                        endpoints = new[] { new { type = "wireguard", tag = "test-wg-peer", system = false,
                            listen_port = ports[0], address = peerNetwork, private_key = peer[0],
                            peers = new[] { new { public_key = client[1], allowed_ips = clientNetwork } } } },
                        outbounds = new[] { new { type = "direct", tag = "direct" } },
                        route = new { final = "direct", rules = new[] { new { ip_cidr = new[] { "127.0.0.1/32", "::1/128" },
                            invert = true, action = "reject" } } }
                    });
                    using (var serverEngine = new Engine(peerConfig, false, null))
                    {
                        RequireReady(serverEngine);
                        using (var clientEngine = new Engine(serializer.Serialize(config), false, null))
                        {
                            RequireReady(clientEngine);
                            var tests = new[] { TcpEcho(tcp4, ports[1]), TcpEcho(tcp6, ports[2]),
                                UdpEcho(udp4, ports[3]), UdpEcho(udp6, ports[4]) };
                            if (!Task.WhenAll(tests).Wait(TimeSpan.FromSeconds(10)))
                                throw new TimeoutException("WireGuard TCP/UDP echo timed out.");
                            clientEngine.Stop().GetAwaiter().GetResult();
                        }
                        serverEngine.Stop().GetAwaiter().GetResult();
                    }
                    Console.WriteLine("OK - Windows process path and actual WireGuard IPv4/IPv6 TCP/UDP payloads over loopback, without Windows TUN");
                }
                finally
                {
                    tcp4.Stop();
                    tcp6.Stop();
                }
            }
        }

        private static void DirectFallback()
        {
            var ports = HoldPorts();
            using (var udp4 = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (var udp6 = new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, 0)))
            {
                var tcp4 = new TcpListener(IPAddress.Loopback, 0);
                var tcp6 = new TcpListener(IPAddress.IPv6Loopback, 0);
                try
                {
                    tcp4.Start();
                    tcp6.Start();
                    var targets = new[] { ((IPEndPoint)tcp4.LocalEndpoint).Port, ((IPEndPoint)tcp6.LocalEndpoint).Port,
                        ((IPEndPoint)udp4.Client.LocalEndPoint).Port, ((IPEndPoint)udp6.Client.LocalEndPoint).Port };
                    var keys = GenerateKeys();
                    var profile = "[Interface]\nPrivateKey=" + keys[0] +
                        "\nAddress=10.77.0.2/32,fd77::2/128\n[Peer]\nPublicKey=" + keys[1] +
                        "\nEndpoint=127.0.0.1:" + ports[0] + "\nAllowedIPs=0.0.0.0/0,::/0\n";
                    var json = new JavaScriptSerializer();
                    var config = json.Deserialize<Dictionary<string, object>>(TunnelConfig.Build(profile, TunnelConfig.DiscordRoot));
                    config["inbounds"] = targets.Select((port, i) => new
                    {
                        type = "direct", tag = "direct-probe-" + i, listen = "127.0.0.1", listen_port = ports[i + 1],
                        network = i < 2 ? "tcp" : "udp", override_address = i % 2 == 0 ? "127.0.0.1" : "::1",
                        override_port = port
                    }).ToArray();
                    var route = (Dictionary<string, object>)config["route"];
                    route["auto_detect_interface"] = false;
                    var rules = (System.Collections.IList)route["rules"];
                    ((Dictionary<string, object>)rules[1])["inbound"] =
                        Enumerable.Range(0, 4).Select(i => "direct-probe-" + i).ToArray();
                    // The production Discord process rule and final direct route are retained.
                    // No WireGuard server is running, so successful echoes require the direct route.
                    using (var engine = new Engine(json.Serialize(config), false, null))
                    {
                        RequireReady(engine);
                        var echoes = Task.WhenAll(TcpEcho(tcp4, ports[1]), TcpEcho(tcp6, ports[2]),
                            UdpEcho(udp4, ports[3]), UdpEcho(udp6, ports[4]));
                        if (!echoes.Wait(TimeSpan.FromSeconds(10)))
                            throw new TimeoutException("Non-Discord direct fallback timed out.");
                        engine.Stop().GetAwaiter().GetResult();
                    }
                    Console.WriteLine("OK - non-Discord TCP/UDP IPv4/IPv6 uses final direct route with no WireGuard server");
                }
                finally
                {
                    tcp4.Stop();
                    tcp6.Stop();
                }
            }
        }

        private static string[] GenerateKeys()
        {
            var binary = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", "sing-box.exe");
            var info = new ProcessStartInfo(binary, "generate wg-keypair")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
            };
            using (var process = Process.Start(info))
            {
                if (!process.WaitForExit(5000))
                {
                    process.Kill();
                    throw new TimeoutException("Temporary WireGuard key generation timed out.");
                }
                var keys = process.StandardOutput.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Split(':').Last().Trim()).ToArray();
                if (process.ExitCode != 0 || keys.Length != 2 || keys.Any(key => Convert.FromBase64String(key).Length != 32))
                    throw new InvalidOperationException("Temporary WireGuard key generation failed.");
                return keys;
            }
        }

        private static int[] HoldPorts()
        {
            var reservations = new List<IDisposable>();
            var ports = new List<int>();
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    bool udp = i == 0 || i > 2;
                    var socket = new Socket(AddressFamily.InterNetwork, udp ? SocketType.Dgram : SocketType.Stream,
                        udp ? ProtocolType.Udp : ProtocolType.Tcp);
                    reservations.Add(socket);
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    ports.Add(((IPEndPoint)socket.LocalEndPoint).Port);
                    if (i == 0)
                        continue;
                    // Keep the port unavailable for both protocols until all addresses are assigned.
                    var other = new Socket(AddressFamily.InterNetwork, udp ? SocketType.Stream : SocketType.Dgram,
                        udp ? ProtocolType.Tcp : ProtocolType.Udp);
                    reservations.Add(other);
                    other.Bind(new IPEndPoint(IPAddress.Loopback, ports[i]));
                }
                return ports.ToArray();
            }
            finally { foreach (var item in reservations) item.Dispose(); }
        }

        private static void RequireReady(Engine engine)
        {
            if (!engine.Ready.Task.Wait(5000) || !engine.Ready.Task.Result)
                throw new InvalidOperationException("Could not start temporary WireGuard peer.");
        }

        private static async Task TcpEcho(TcpListener target, int port)
        {
            var payload = Encoding.ASCII.GetBytes("discord-tunnel-test");
            using (var sender = new TcpClient())
            {
                await sender.ConnectAsync(IPAddress.Loopback, port);
                await sender.GetStream().WriteAsync(payload, 0, payload.Length);
                using (var echo = await target.AcceptTcpClientAsync())
                {
                    await ExpectPayload(echo.GetStream(), payload);
                    await echo.GetStream().WriteAsync(payload, 0, payload.Length);
                    await ExpectPayload(sender.GetStream(), payload);
                }
            }
        }

        private static async Task ExpectPayload(Stream stream, byte[] payload)
        {
            var received = new byte[payload.Length];
            for (int offset = 0; offset < received.Length;)
            {
                int count = await stream.ReadAsync(received, offset, received.Length - offset);
                if (count == 0)
                    throw new EndOfStreamException("TCP echo ended early.");
                offset += count;
            }
            if (!payload.SequenceEqual(received))
                throw new InvalidDataException("TCP echo mismatch.");
        }

        private static async Task UdpEcho(UdpClient echo, int port)
        {
            var payload = Encoding.ASCII.GetBytes("discord-tunnel-test");
            using (var sender = new UdpClient("127.0.0.1", port))
            {
                await sender.SendAsync(payload, payload.Length);
                var input = await echo.ReceiveAsync();
                if (!input.Buffer.SequenceEqual(payload))
                    throw new InvalidDataException("UDP request mismatch.");
                await echo.SendAsync(input.Buffer, input.Buffer.Length, input.RemoteEndPoint);
                if (!(await sender.ReceiveAsync()).Buffer.SequenceEqual(payload))
                    throw new InvalidDataException("UDP response mismatch.");
            }
        }
    }
}
