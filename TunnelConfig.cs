using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DiscordTunnelPrototype
{
    internal static class TunnelConfig
    {
        internal const string AdapterName = "DiscordTunnel";
        private const string Direct = "direct";
        private const string Tunnel = "discord-wireguard";

        internal static string DiscordRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord"); }
        }

        internal static string ProcessPattern(string root)
        {
            return @"(?i)^" + Regex.Escape(root) +
                @"\\(?:Discord\.exe|Update\.exe|app-[0-9]+\.[0-9]+(?:\.[0-9]+){0,2}\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]+\.exe)$";
        }

        internal static string[] FindDiscord(string root)
        {
            if (!Directory.Exists(root) || IsLink(root))
                throw new InvalidDataException("Bu Windows hesabında standart Discord masaüstü kurulumu bulunamadı.");

            var candidates = new List<string> { Path.Combine(root, "Discord.exe"), Path.Combine(root, "Update.exe") };
            foreach (var directory in Directory.EnumerateDirectories(root, "app-*").Where(path => !IsLink(path)))
            {
                candidates.Add(Path.Combine(directory, "Discord.exe"));
                candidates.Add(Path.Combine(directory, "updater_launcher.exe"));
            }

            var pattern = new Regex(ProcessPattern(root), RegexOptions.CultureInvariant);
            var files = candidates.Where(path => pattern.IsMatch(path) && File.Exists(path) && !IsLink(path)).ToArray();
            if (!files.Any(path => Path.GetDirectoryName(path) != root &&
                Path.GetFileName(path).Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Standart Discord klasöründe sürüme ait Discord.exe bulunamadı.");
            return files;
        }

        private static bool IsLink(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        internal static string Build(string profile, string root)
        {
            var fields = Parse(profile);
            var addresses = Prefixes(Required(fields, "Interface.Address"));
            var allowed = Prefixes(Required(fields, "Peer.AllowedIPs"));
            if (!addresses.Any(address => IsIPv6(address)) || !addresses.Any(address => !IsIPv6(address))
                || !allowed.Contains("0.0.0.0/0") || !allowed.Contains("::/0"))
                throw new InvalidDataException("Profil IPv4 ve IPv6 tünel adresleri ile 0.0.0.0/0 ve ::/0 içermeli.");

            string host;
            int port;
            SplitEndpoint(Required(fields, "Peer.Endpoint"), out host, out port);
            var peer = Map(
                "address", host, "port", port, "public_key", Key(Required(fields, "Peer.PublicKey")),
                "allowed_ips", allowed);
            string value;
            if (fields.TryGetValue("Peer.PresharedKey", out value))
                peer.Add("pre_shared_key", Key(value));
            if (fields.TryGetValue("Peer.PersistentKeepalive", out value))
                peer.Add("persistent_keepalive_interval", Number(value, 0, 65535, "PersistentKeepalive"));
            if (fields.TryGetValue("Peer.Reserved", out value))
            {
                var bytes = value.Split(',').Select(item => Number(item.Trim(), 0, 255, "Reserved")).ToArray();
                if (bytes.Length != 3)
                    throw new InvalidDataException("Reserved alanı virgülle ayrılmış üç bayt olmalı.");
                peer.Add("reserved", bytes);
            }

            var endpoint = Map("type", "wireguard", "tag", Tunnel, "system", false,
                "address", addresses, "private_key", Key(Required(fields, "Interface.PrivateKey")),
                "peers", new[] { peer });
            if (fields.TryGetValue("Interface.MTU", out value))
                endpoint.Add("mtu", Number(value, 1280, 9000, "MTU"));

            var configuration = Map(
                "log", Map("level", "debug"),
                "dns", Map("servers", new[] { Map("type", "local", "tag", "local-dns", "prefer_go", false) }),
                "inbounds", new[] { Map("type", "tun", "tag", "tun-in", "interface_name", AdapterName,
                    "address", new[] { "172.30.249.1/30", "fd5d:1d9c:8283::1/126" }, "mtu", 1500,
                    "auto_route", true, "strict_route", false, "dns_mode", "disabled", "stack", "mixed") },
                "endpoints", new[] { endpoint },
                "outbounds", new[] { Map("type", Direct, "tag", Direct) },
                "route", Map("auto_detect_interface", true, "default_domain_resolver", "local-dns", "final", Direct,
                    "rules", new[] {
                        Map("inbound", Tunnel, "action", "reject"),
                        Map("inbound", "tun-in", "network", new[] { "tcp", "udp" },
                            "process_path_regex", new[] { ProcessPattern(root) }, "action", "route", "outbound", Tunnel) }));
            return new JavaScriptSerializer().Serialize(configuration);
        }

        private static Dictionary<string, string> Parse(string profile)
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Interface.PrivateKey", "Interface.Address", "Interface.MTU", "Interface.DNS",
                "Peer.PublicKey", "Peer.PresharedKey", "Peer.AllowedIPs", "Peer.Endpoint",
                "Peer.PersistentKeepalive", "Peer.Reserved", "Peer.AllowedApps", "Peer.DisallowedApps"
            };
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var section = "";
            using (var reader = new StringReader(profile))
            {
                string raw;
                while ((raw = reader.ReadLine()) != null)
                {
                    var line = raw.Split(new[] { '#', ';' }, 2)[0].Trim();
                    if (line.Length == 0)
                        continue;
                    if (section.Length == 0 && line.Equals("[Interface]", StringComparison.OrdinalIgnoreCase))
                    {
                        section = "Interface";
                        continue;
                    }
                    if (section == "Interface" && line.Equals("[Peer]", StringComparison.OrdinalIgnoreCase))
                    {
                        section = "Peer";
                        continue;
                    }

                    var parts = line.Split(new[] { '=' }, 2);
                    var name = section + "." + parts[0].Trim();
                    if (parts.Length != 2 || !allowed.Contains(name) || parts[1].Trim().Length == 0)
                        throw new InvalidDataException("Profil tek [Interface] ve tek [Peer] içermeli. Desteklenmeyen veya boş alan var.");
                    var value = parts[1].Trim();
                    string previous;
                    if (fields.TryGetValue(name, out previous))
                    {
                        if (!name.Equals("Interface.Address", StringComparison.OrdinalIgnoreCase) &&
                            !name.Equals("Peer.AllowedIPs", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("WireGuard profilinde yinelenen ayar var.");
                        value = previous + "," + value;
                    }
                    fields[name] = value;
                }
            }
            return fields;
        }

        private static string Required(Dictionary<string, string> fields, string name)
        {
            string value;
            if (!fields.TryGetValue(name, out value))
                throw new InvalidDataException(name + " alanı eksik.");
            return value;
        }

        private static string[] Prefixes(string text)
        {
            return text.Split(',').Select(value =>
            {
                var parts = value.Trim().Split('/');
                IPAddress address;
                if (parts.Length != 2 || !ParseIP(parts[0], out address))
                    throw new InvalidDataException("WireGuard adresi CIDR biçiminde olmalı.");
                var prefix = Number(parts[1], 0, address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128, "CIDR");
                return address + "/" + prefix.ToString(CultureInfo.InvariantCulture);
            }).ToArray();
        }

        private static bool IsIPv6(string prefix)
        {
            return prefix.Contains(":");
        }

        private static bool ParseIP(string value, out IPAddress address)
        {
            if (!IPAddress.TryParse(value, out address))
                return false;
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                return address.ScopeId == 0 && !address.IsIPv4MappedToIPv6;
            return address.ToString() == value;
        }

        private static string Key(string text)
        {
            try
            {
                var key = Convert.FromBase64String(text);
                if (key.Length == 32 && Convert.ToBase64String(key) == text && key.Any(value => value != 0))
                    return text;
            }
            catch (FormatException) { }
            throw new InvalidDataException("WireGuard anahtarı 32 baytlık Base64 biçiminde olmalı.");
        }

        private static void SplitEndpoint(string text, out string host, out int port)
        {
            var match = Regex.Match(text, @"\A(?:\[([0-9A-Fa-f:.]+)\]|([A-Za-z0-9.-]+)):([0-9]+)\z");
            if (!match.Success)
                throw new InvalidDataException("Endpoint alanı sunucu:port veya [IPv6]:port biçiminde olmalı.");
            host = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            IPAddress address;
            if (match.Groups[1].Success)
            {
                if (!ParseIP(host, out address) || address.AddressFamily != AddressFamily.InterNetworkV6)
                    throw new InvalidDataException("Endpoint IPv6 adresi geçersiz.");
            }
            else if (!ParseIP(host, out address) &&
                (Uri.CheckHostName(host) != UriHostNameType.Dns || !host.Any(char.IsLetter)))
                throw new InvalidDataException("Endpoint sunucu adı veya IPv4 adresi geçersiz.");
            port = Number(match.Groups[3].Value, 1, 65535, "Endpoint portu");
        }

        private static int Number(string value, int minimum, int maximum, string field)
        {
            int number;
            if (!Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) ||
                number < minimum || number > maximum)
                throw new InvalidDataException(field + " alanı geçersiz.");
            return number;
        }

        private static Dictionary<string, object> Map(params object[] pairs)
        {
            var map = new Dictionary<string, object>();
            for (var index = 0; index < pairs.Length; index += 2)
                map.Add((string)pairs[index], pairs[index + 1]);
            return map;
        }
    }
}
