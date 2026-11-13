using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordTunnelPrototype
{
    internal static class SmokeTest
    {
        private const string LocalEngine = "{\"log\":{\"level\":\"info\"},\"outbounds\":[{\"type\":\"direct\",\"tag\":\"direct\"}]}";

        private static int Main(string[] args)
        {
            try
            {
                if (args.Contains("--orphan-test"))
                {
                    using (var engine = new Engine(LocalEngine, false, null))
                    {
                        Expect(Bounded(engine.Ready.Task), "Child startup without TUN");
                        Console.WriteLine("READY");
                        Console.Out.Flush();
                        Thread.Sleep(TimeSpan.FromSeconds(20));
                    }
                    return 0;
                }

                TestPaths();
                TestConfig();
                TestDnsPacket();
                WireGuardLoopback.Run();
                using (var engine = new Engine(LocalEngine, false, null))
                {
                    Expect(Bounded(engine.Ready.Task), "Child startup without TUN");
                    Expect(!engine.Exit.IsCompleted, "Child stays running");
                    Bounded(engine.Stop().ContinueWith(task =>
                    {
                        task.GetAwaiter().GetResult();
                        return true;
                    }));
                    Expect(Bounded(engine.Exit) == 0, "Child exits normally after Ctrl+C");
                    Console.WriteLine("OK · real sing-box start and graceful exit without TUN");
                }
                if (args.Contains("--dns"))
                    Console.WriteLine(DnsDiagnostics.Run().GetAwaiter().GetResult());
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void TestPaths()
        {
            var root = @"C:\Users\örnek+ test\AppData\Local\Discord";
            var pattern = new Regex(TunnelConfig.ProcessPattern(root), RegexOptions.CultureInvariant);
            foreach (string path in new[] { @"\Update.exe", @"\Discord.exe", @"\app-1.0.9267\Discord.exe",
                @"\App-1.0.9268\DISCORD.EXE", @"\app-1.0.10000\updater_launcher.exe" })
                Expect(pattern.IsMatch(root + path), path);
            foreach (string path in new[] { @"\Other\Discord.exe", @"\app-abc\Discord.exe",
                @"\app-1.0.9268\Update.exe", @"\app-1.0.9268\Discord.exe.bak", @"-Other\Update.exe" })
                Expect(!pattern.IsMatch(root + path), path);
            Console.WriteLine("OK · paired and future versions, updater and negative path examples");

            if (Directory.Exists(TunnelConfig.DiscordRoot))
            {
                var installed = TunnelConfig.FindDiscord(TunnelConfig.DiscordRoot);
                Console.WriteLine("OK · " + installed.Length + " local Discord executable paths found");
            }
        }

        private static void TestConfig()
        {
            var privateKey = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray());
            var publicKey = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray());
            var sample = "[Interface]\nPrivateKey=" + privateKey +
                "\nAddress=192.0.2.2/32\nAddress=2001:db8::2/128\nDNS=192.0.2.53\nMTU=1280\n[Peer]\nPublicKey=" +
                publicKey + "\nEndpoint=192.0.2.1:51820\nAllowedIPs=0.0.0.0/0, ::/0\nReserved=1,2,3\nPersistentKeepalive=25\n";
            string config = TunnelConfig.Build(sample, TunnelConfig.DiscordRoot);
            Expect(config.IndexOf("192.0.2.53", StringComparison.Ordinal) < 0, "Profile DNS isolation");
            Expect(config.IndexOf("\"dns_mode\":\"disabled\"", StringComparison.Ordinal) >= 0, "TUN DNS isolation");
            using (var engine = new Engine(config, true, null))
                Expect(Bounded(engine.Exit) == 0, "Pinned sing-box accepts generated TUN config: " + engine.Failure);
            foreach (string change in new[] { sample.Replace(", ::/0", ""), sample + "\n[Peer]\n",
                sample.Replace(privateKey, "broken"), sample + "PostUp=example\n" })
            {
                try { TunnelConfig.Build(change, TunnelConfig.DiscordRoot); }
                catch (InvalidDataException) { continue; }
                throw new InvalidOperationException("Malformed WireGuard profile was accepted");
            }
            Console.WriteLine("OK · pinned sing-box check with synthetic dual-stack profile and invalid profile cases");
        }

        private static void TestDnsPacket()
        {
            byte[] query = { 1, 2, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 120, 0, 0, 1, 0, 1 };
            byte[] answer = { 192, 12, 0, 1, 0, 1, 0, 0, 0, 1, 0, 4, 192, 0, 2, 1 };
            var reply = query.Concat(answer).ToArray();
            reply[2] = 0x81;
            reply[7] = 1;
            Expect(DnsDiagnostics.HasDnsAResponse(query, reply), "DNS A answer");
            Expect(!DnsDiagnostics.HasDnsAResponse(query, reply.Take(query.Length).ToArray()), "Truncated DNS answer");
            reply[query.Length + 3] = 5;
            Expect(!DnsDiagnostics.HasDnsAResponse(query, reply), "CNAME-only answer");
            Console.WriteLine("OK · DNS packet parsing with valid, truncated and non-A responses");
        }

        private static T Bounded<T>(Task<T> task)
        {
            if (Task.WhenAny(task, Task.Delay(10000)).GetAwaiter().GetResult() != task)
                throw new TimeoutException();
            return task.GetAwaiter().GetResult();
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
