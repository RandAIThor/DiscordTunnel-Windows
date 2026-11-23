using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DiscordTunnelPrototype
{
    internal sealed class Tray : ApplicationContext
    {
        private readonly Control dispatcher = new Control();
        private readonly NotifyIcon icon;
        private readonly ToolStripMenuItem start, stop, diagnose, status;
        private readonly bool admin;
        private Engine engine;
        private bool busy, quitting, diagnosing;
        private volatile bool monitoring;
        private int networkGeneration;
        private int handshakes;
        private bool networkCheckPending;
        private const string HandshakeReceived = "WireGuard eşinden el sıkışma yanıtı alındı. Discord oturumu ve ses ayrı doğrulanmalı.";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool admin;
            using (var identity = WindowsIdentity.GetCurrent())
                admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

            bool created;
            using (var instance = new Mutex(true, admin ? @"Local\DiscordTunnelPrototype.Admin" : @"Local\DiscordTunnelPrototype", out created))
            {
                if (created)
                    using (var tray = new Tray(admin, args.Contains("--start")))
                        Application.Run(tray);
            }
        }

        private Tray(bool admin, bool startRequested)
        {
            this.admin = admin;
            // Capture a WinForms synchronization context before handling asynchronous menu actions.
            IntPtr handle = dispatcher.Handle;
            var menu = new ContextMenuStrip();
            status = new ToolStripMenuItem("Tünel kapalı");
            status.Click += (sender, args) => ShowStatus();
            start = new ToolStripMenuItem("Profil seç ve tüneli başlat");
            start.Click += async (sender, args) => await Start();
            stop = new ToolStripMenuItem("Tüneli durdur");
            stop.Click += async (sender, args) => await Stop(false);
            diagnose = new ToolStripMenuItem("Windows DNS, port 53 ve DoH testi");
            diagnose.Click += async (sender, args) => await Diagnose();
            var exit = new ToolStripMenuItem("Çıkış");
            exit.Click += async (sender, args) => await Stop(true);
            menu.Items.AddRange(new ToolStripItem[] { status, new ToolStripSeparator(), start, stop, diagnose, exit });
            icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "Discord Tunnel", ContextMenuStrip = menu, Visible = true };
            icon.DoubleClick += (sender, args) => ShowStatus();
            NetworkChange.NetworkAddressChanged += NetworkChanged;
            UpdateMenu();
            dispatcher.BeginInvoke(new Action(async () => await StartFromLaunch(startRequested)));
        }

        private async Task StartFromLaunch(bool startRequested)
        {
            if (busy || quitting)
                return;
            if (!startRequested)
            {
                DialogResult choice;
                busy = true;
                UpdateMenu();
                try
                {
                    var next = admin ? "profil seçimi" : "Windows yönetici onayı ve profil seçimi";
                    choice = MessageBox.Show(
                        "Bağlanmak için kendi WireGuard (.conf) profilin gerekir. Devam ettiğinde " + next + " açılır.",
                        "Discord Tunnel · Bağlantı kurulumu", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                }
                finally
                {
                    busy = false;
                    UpdateMenu();
                }
                if (choice != DialogResult.OK)
                    return;
            }
            await Start();
        }

        private async Task Start()
        {
            if (busy || engine != null)
                return;
            if (!admin)
            {
                try
                {
                    using (Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--start") { UseShellExecute = true, Verb = "runas" })) { }
                    ExitThread();
                }
                catch (Win32Exception exception)
                {
                    ShowStartupFailure(exception.NativeErrorCode == 1223
                        ? "Windows yönetici onayı iptal edildi."
                        : "Windows uygulamayı yönetici olarak açamadı.");
                }
                return;
            }

            busy = true;
            UpdateMenu();
            try
            {
                TunnelConfig.FindDiscord(TunnelConfig.DiscordRoot);
                using (var file = new OpenFileDialog
                {
                    Filter = "WireGuard profili (*.conf)|*.conf|Tüm dosyalar (*.*)|*.*",
                    Title = "Bu oturumda kullanılacak kendi WireGuard profilini seç",
                    CheckFileExists = true
                })
                {
                    if (file.ShowDialog() != DialogResult.OK)
                        return;
                    var deadline = Stopwatch.StartNew();
                    SetStatus("WireGuard profili ve motor doğrulanıyor…", false);
                    string path = file.FileName;
                    string config = await Within(Task.Run(() => TunnelConfig.Build(ReadProfile(path), TunnelConfig.DiscordRoot)),
                        deadline, "WireGuard profili okunamadı");
                    using (var validation = await StartEngine(config, true, deadline))
                    {
                        if (await Within(validation.Exit, deadline, "sing-box yapılandırma kontrolü zaman aşımına uğradı") != 0)
                            throw new InvalidDataException(validation.Failure + ".");
                    }
                    if (NetworkInterface.GetAllNetworkInterfaces().Any(item => item.Name.Equals(TunnelConfig.AdapterName, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException("Önceki TUN bağdaştırıcısı hâlâ mevcut. Ağ durumunu kontrol et.");

                    SetStatus("Windows TUN bağdaştırıcısı ve rotaları açılıyor…", false);
                    handshakes = 0;
                    engine = await StartEngine(config, false, deadline);
                    if (!await Within(engine.Ready.Task, deadline, "Windows TUN veya WireGuard başlangıcı zaman aşımına uğradı"))
                        throw new InvalidDataException(engine.Failure + ".");
                    monitoring = true;
                    WatchEngine(engine);
                    if (Volatile.Read(ref handshakes) == 0)
                    {
                        SetStatus("TUN hazır. Discord’u kullanarak WireGuard el sıkışmasını tetikle.", true);
                        WatchHandshake(engine, ++networkGeneration, 0, false,
                            Math.Max(0, 28500 - (int)deadline.ElapsedMilliseconds));
                    }
                    else
                        SetStatus(HandshakeReceived, true);
                }
            }
            catch (Exception exception)
            {
                DropEngine();
                string message = exception is InvalidDataException || exception is TimeoutException ? exception.Message :
                    "Profil okunamadı veya sing-box çalıştırılamadı.";
                ShowStartupFailure(message);
            }
            finally
            {
                busy = false;
                UpdateMenu();
            }
        }

        private async Task<Engine> StartEngine(string config, bool check, Stopwatch deadline)
        {
            Action report = check ? null : new Action(() =>
            {
                Interlocked.Increment(ref handshakes);
                Post(() =>
                {
                    if (!monitoring)
                        return;
                    networkCheckPending = false;
                    SetStatus(HandshakeReceived, true);
                });
            });
            var pending = Task.Run(() => new Engine(config, check, report));
            try { return await Within(pending, deadline, "sing-box işlemi başlatılamadı"); }
            catch (TimeoutException)
            {
                DisposeWhenStarted(pending);
                throw;
            }
        }

        private static void DisposeWhenStarted(Task<Engine> pending)
        {
            pending.ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                    task.Result.Dispose();
                else if (task.IsFaulted)
                    GC.KeepAlive(task.Exception);
            }, TaskScheduler.Default);
        }

        private static string ReadProfile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new StreamReader(stream))
            {
                if (stream.Length > 32768)
                    throw new InvalidDataException("WireGuard profili 32 KB sınırını aşıyor.");
                return reader.ReadToEnd();
            }
        }

        private async void WatchEngine(Engine active)
        {
            try { await active.Exit; }
            catch (Exception) { }
            if (active != engine || !monitoring)
                return;
            DropEngine();
            SetStatus("sing-box beklenmedik biçimde kapandı. Normal internet bağlantısını kontrol et.", true);
            UpdateMenu();
        }

        private void NetworkChanged(object sender, EventArgs args)
        {
            if (!monitoring)
                return;
            Post(() =>
            {
                if (engine == null || !monitoring || networkCheckPending)
                    return;
                networkCheckPending = true;
                int generation = ++networkGeneration;
                int baseline = Volatile.Read(ref handshakes);
                SetStatus("Ağ değişti. Yeni bağlantı üzerinden WireGuard yanıtı bekleniyor.", true);
                WatchHandshake(engine, generation, baseline, true, 27000);
            });
        }

        private async void WatchHandshake(Engine active, int generation, int baseline, bool switched, int wait)
        {
            await Task.Delay(wait);
            if (active != engine || generation != networkGeneration || !monitoring)
                return;
            if (switched)
                networkCheckPending = false;
            if (Volatile.Read(ref handshakes) != baseline)
                return;
            SetStatus(switched
                ? "Ağ değişiminden sonra yeni WireGuard el sıkışması görülmedi. Discord bağlantısını veya DNS testini kontrol et."
                : "WireGuard yanıtı görülmedi. Discord trafiği üret ve eşin erişimini veya DNS’i kontrol et.", true);
        }

        private async Task Stop(bool exit)
        {
            if (busy || quitting)
                return;
            quitting = exit;
            busy = true;
            monitoring = false;
            UpdateMenu();
            try
            {
                if (engine != null)
                {
                    SetStatus("Tünel durduruluyor…", false);
                    await engine.Stop();
                    SetStatus("Tünel kapalı. Normal ağ durumunu kontrol edebilirsin.", !exit);
                }
            }
            catch (Exception)
            {
                SetStatus("Motorun kapanışı doğrulanamadı. Normal ağ durumunu kontrol et.", true);
            }
            finally
            {
                DropEngine();
                busy = false;
                if (exit)
                    ExitThread();
                else
                    UpdateMenu();
            }
        }

        private void DropEngine()
        {
            monitoring = false;
            networkCheckPending = false;
            networkGeneration++;
            if (engine != null)
                engine.Dispose();
            engine = null;
        }

        private async Task Diagnose()
        {
            if (diagnosing)
                return;
            diagnosing = true;
            UpdateMenu();
            try
            {
                string result = await DnsDiagnostics.Run();
                if (!quitting)
                    MessageBox.Show(result, "DNS karşılaştırması", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                diagnosing = false;
                if (!quitting)
                    UpdateMenu();
            }
        }

        private static async Task<T> Within<T>(Task<T> operation, Stopwatch stopwatch, string failure)
        {
            var remaining = Math.Max(0, 28500 - (int)stopwatch.ElapsedMilliseconds);
            if (await Task.WhenAny(operation, Task.Delay(remaining)) != operation)
                throw new TimeoutException(failure + ".");
            return await operation;
        }

        private void Post(Action action)
        {
            try { dispatcher.BeginInvoke(action); }
            catch (InvalidOperationException) { } // The tray is already shutting down.
        }

        private void UpdateMenu()
        {
            start.Enabled = !busy && engine == null;
            stop.Enabled = !busy && engine != null;
            diagnose.Enabled = !busy && !diagnosing;
        }

        private void SetStatus(string text, bool notify)
        {
            status.Text = text;
            if (notify)
                icon.ShowBalloonTip(6000, "Discord Tunnel", text, ToolTipIcon.Info);
        }

        private void ShowStatus()
        {
            MessageBox.Show(status.Text, "Discord Tunnel", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowStartupFailure(string message)
        {
            SetStatus(message, false);
            MessageBox.Show(message, "Discord Tunnel · Bağlantı kurulamadı",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                NetworkChange.NetworkAddressChanged -= NetworkChanged;
                DropEngine();
                icon.Visible = false;
                icon.ContextMenuStrip.Dispose();
                icon.Dispose();
                dispatcher.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class DnsDiagnostics
    {
        internal static async Task<string> Run()
        {
            var system = Result("Windows DNS istemcisi", SystemLookup);
            var plaintext = Result("Cloudflare UDP/53", PlaintextLookup);
            var https = Result("Cloudflare DoH", HttpsLookup);
            return string.Join(Environment.NewLine, await Task.WhenAll(system, plaintext, https)) +
                Environment.NewLine + Environment.NewLine + "Bu test Discord’un DNS ayarını değiştirmez.";
        }

        private static async Task<string> Result(string label, Func<Task<bool>> test)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var operation = Task.Run(test);
                if (await Task.WhenAny(operation, Task.Delay(8000)) != operation)
                    return label + " · 8 sn içinde yanıt alınamadı";
                return label + (await operation ? " · A yanıtı alındı" : " · geçerli A yanıtı yok") +
                    " (" + watch.ElapsedMilliseconds + " ms)";
            }
            catch (Exception)
            {
                return label + " · sorgu başarısız (" + watch.ElapsedMilliseconds + " ms)";
            }
        }

        private static async Task<bool> SystemLookup()
        {
            return (await Dns.GetHostAddressesAsync("discord.com")).Any(address =>
                address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        }

        private static Task<bool> PlaintextLookup()
        {
            using (var socket = new System.Net.Sockets.UdpClient("1.1.1.1", 53))
            {
                byte[] query = { 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 7, 100, 105, 115, 99, 111, 114, 100,
                    3, 99, 111, 109, 0, 0, 1, 0, 1 };
                using (var random = System.Security.Cryptography.RandomNumberGenerator.Create())
                {
                    byte[] id = new byte[2];
                    random.GetBytes(id);
                    Array.Copy(id, query, id.Length);
                }
                socket.Client.ReceiveTimeout = 6500;
                socket.Send(query, query.Length);
                IPEndPoint source = null;
                var reply = socket.Receive(ref source);
                return Task.FromResult(HasDnsAResponse(query, reply));
            }
        }

        internal static bool HasDnsAResponse(byte[] query, byte[] reply)
        {
            if (reply.Length < query.Length || reply[0] != query[0] || reply[1] != query[1] ||
                (reply[2] & 0xFA) != 0x80 || (reply[3] & 0x0F) != 0 || ReadWord(reply, 4) != 1 ||
                !reply.Skip(12).Take(query.Length - 12).SequenceEqual(query.Skip(12)))
                return false;

            int count = ReadWord(reply, 6);
            int offset = query.Length;
            for (int answer = 0; answer < count; answer++)
            {
                if (!SkipDnsName(reply, ref offset) || offset + 10 > reply.Length)
                    return false;
                int type = ReadWord(reply, offset);
                int recordClass = ReadWord(reply, offset + 2);
                int size = ReadWord(reply, offset + 8);
                offset += 10;
                if (offset + size > reply.Length)
                    return false;
                if (type == 1 && recordClass == 1 && size == 4)
                    return true;
                offset += size;
            }
            return false;
        }

        private static bool SkipDnsName(byte[] message, ref int offset)
        {
            while (offset < message.Length)
            {
                int length = message[offset++];
                if (length == 0)
                    return true;
                if ((length & 0xC0) == 0xC0)
                    return offset < message.Length && (((length & 0x3F) << 8) | message[offset++]) < message.Length;
                if (length > 63 || offset + length > message.Length)
                    return false;
                offset += length;
            }
            return false;
        }

        private static int ReadWord(byte[] message, int offset)
        {
            return (message[offset] << 8) | message[offset + 1];
        }

        private static async Task<bool> HttpsLookup()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(7) })
            {
                client.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");
                var text = await client.GetStringAsync("https://1.1.1.1/dns-query?name=discord.com&type=A");
                var json = new JavaScriptSerializer().DeserializeObject(text) as System.Collections.Generic.Dictionary<string, object>;
                object status, answer;
                return json != null && json.TryGetValue("Status", out status) && Equals(status, 0) &&
                    json.TryGetValue("Answer", out answer) && answer is object[] &&
                    ((object[])answer).OfType<System.Collections.Generic.Dictionary<string, object>>().Any(record =>
                        record.ContainsKey("type") && Equals(record["type"], 1) &&
                        record.ContainsKey("data") && Uri.CheckHostName(record["data"] as string) == UriHostNameType.IPv4);
            }
        }
    }
}
