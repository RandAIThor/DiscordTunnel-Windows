using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace DiscordTunnelPrototype
{
    // Acceptance harness only. Adds a loopback-only WireGuard diagnostic proxy.
    // Success of that proxy is NOT proof of Discord's own DNS or voice behavior.
    internal static class LiveAcceptance
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, bool> Processes = new Dictionary<string, bool>();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static string report;
        private static int tcp, udp;
        private static void Record(string value)
        {
            lock (Gate) File.AppendAllText(report, Clock.ElapsedMilliseconds + " " + value + Environment.NewLine);
        }

        // Called by the isolated instrumented controller built by Build-LiveAcceptance.ps1.
        // Parse in memory; do not persist raw engine lines, paths, addresses, or profile data.
        internal static void RecordEngine(string line)
        {
            var flow = Regex.Match(line, @"\[(\d+) \d+ms\]");
            if (!flow.Success) return;
            lock (Gate)
            {
                const string marker = "router: found process path: ";
                int index = line.IndexOf(marker, StringComparison.Ordinal);
                if (index >= 0)
                {
                    if (Processes.Count > 8192) Processes.Clear();
                    Processes[flow.Groups[1].Value] = Regex.IsMatch(line.Substring(index + marker.Length),
                        TunnelConfig.ProcessPattern(TunnelConfig.DiscordRoot), RegexOptions.CultureInvariant);
                }
                bool discord;
                if (!Processes.TryGetValue(flow.Groups[1].Value, out discord) || !discord ||
                    line.IndexOf("via outbound/wireguard[discord-wireguard]", StringComparison.Ordinal) < 0) return;
                if (line.IndexOf("forward tcp connection", StringComparison.Ordinal) >= 0) tcp++;
                if (line.IndexOf("forward udp connection", StringComparison.Ordinal) >= 0) udp++;
            }
        }

        private static int Main(string[] args)
        {
            if (args.Length != 2 || !Directory.Exists(args[0])) return 2;
            report = Path.Combine(args[0], "status.txt");
            try
            {
                TunnelConfig.FindDiscord(TunnelConfig.DiscordRoot);
                if (new FileInfo(args[1]).Length > 32768) throw new InvalidDataException();
                var json = new JavaScriptSerializer();
                var config = json.Deserialize<Dictionary<string, object>>(TunnelConfig.Build(File.ReadAllText(args[1]), TunnelConfig.DiscordRoot));
                var reservation = new TcpListener(IPAddress.Loopback, 0);
                reservation.Start();
                int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
                reservation.Stop();
                var inbounds = new List<object>();
                foreach (var item in (IList)config["inbounds"]) inbounds.Add(item);
                inbounds.Add(new { type = "socks", tag = "local-acceptance-probe", listen = "127.0.0.1", listen_port = port });
                config["inbounds"] = inbounds;
                var route = (Dictionary<string, object>)config["route"];
                var rules = new List<object>();
                foreach (var item in (IList)route["rules"]) rules.Add(item);
                rules.Insert(1, new { inbound = "local-acceptance-probe", action = "route", outbound = "discord-wireguard" });
                route["rules"] = rules;
                string text = json.Serialize(config);
                using (var check = new Engine(text, true, null))
                    if (!check.Exit.Wait(10000) || check.Exit.Result != 0) { Record("config-rejected"); return 3; }
                Record("ready-for-go");
                var wait = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(args[0], "go")))
                {
                    if (wait.Elapsed > TimeSpan.FromSeconds(55)) { Record("go-timeout"); return 4; }
                    Thread.Sleep(100);
                }
                using (var engine = new Engine(text, false, () => Record("wireguard-handshake-response")))
                {
                    try
                    {
                        if (!engine.Ready.Task.Wait(26000) || !engine.Ready.Task.Result) { Record("tun-start-failed"); return 5; }
                        Record("tun-ready probe-port=" + port);
                        wait.Restart();
                        while (!engine.Exit.IsCompleted && !File.Exists(Path.Combine(args[0], "stop")) && wait.Elapsed < TimeSpan.FromMinutes(5))
                            Thread.Sleep(100);
                    }
                    finally
                    {
                        engine.Stop().GetAwaiter().GetResult();
                        Record("engine-stopped code=" + engine.Exit.GetAwaiter().GetResult());
                        lock (Gate) Record("discord-route-counts tcp=" + tcp + " udp=" + udp);
                    }
                }
                return 0;
            }
            catch (Exception error) { Record("exception=" + error.GetType().Name); return 6; }
        }
    }
}
