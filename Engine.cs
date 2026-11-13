using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace DiscordTunnelPrototype
{
    internal sealed class Engine : IDisposable
    {
        internal readonly TaskCompletionSource<bool> Ready = new TaskCompletionSource<bool>();
        private readonly bool check;
        private readonly Action signal;
        private readonly SafeFileHandle job;
        private readonly Process process;
        private readonly Task output;
        internal readonly Task<int> Exit;
        private volatile string failure = "sing-box çalışma hatası";

        internal string Failure { get { return failure; } }

        internal Engine(string configuration, bool check, Action signal)
        {
            this.check = check;
            this.signal = signal;
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine");
            string exe = Path.Combine(directory, "sing-box.exe");
            Verify(exe, "8F30682EE198855440D2A7FE070D5D3653793B2D01BB8C85EEA88F93BBE7C8B9");
            Verify(Path.Combine(directory, "libcronet.dll"), "3217C6260FBCA5F16072E0B79735742F40109A63BB0FF88FD6B96DD6B54A2928");

            job = CreateJobObject(IntPtr.Zero, null);
            try
            {
                var limits = new JobLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
                Require(!job.IsInvalid && SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf(typeof(JobLimits))));

                var attributes = new SecurityAttributes { Length = Marshal.SizeOf(typeof(SecurityAttributes)), Inherit = true };
                SafeFileHandle stdinRead, stdinWrite, stdoutRead, stdoutWrite;
                Require(CreatePipe(out stdinRead, out stdinWrite, ref attributes, 0));
                using (stdinRead)
                using (stdinWrite)
                {
                    Require(CreatePipe(out stdoutRead, out stdoutWrite, ref attributes, 0));
                    bool outputOwned = false;
                    try
                    {
                        Require(SetHandleInformation(stdinWrite, 1, 0) && SetHandleInformation(stdoutRead, 1, 0));
                        var startup = new Startup
                        {
                            Size = Marshal.SizeOf(typeof(Startup)), Flags = 0x101,
                            Stdin = stdinRead.DangerousGetHandle(), Stdout = stdoutWrite.DangerousGetHandle(),
                            Stderr = stdoutWrite.DangerousGetHandle()
                        };
                        ProcessInfo child;
                        const uint suspendedWithConsole = 0x04 | 0x10;
                        var command = new StringBuilder("\"" + exe + "\" " + (check ? "check" : "run") + " --disable-color -c stdin");
                        Require(CreateProcess(exe, command, IntPtr.Zero, IntPtr.Zero, true, suspendedWithConsole,
                            IntPtr.Zero, directory, ref startup, out child));
                        using (var childHandle = new SafeFileHandle(child.Process, true))
                        using (var thread = new SafeFileHandle(child.Thread, true))
                        {
                            if (!AssignProcessToJobObject(job, childHandle))
                            {
                                int error = Marshal.GetLastWin32Error();
                                TerminateProcess(childHandle, 1);
                                throw new Win32Exception(error);
                            }
                            process = Process.GetProcessById(child.Id);
                            GC.KeepAlive(process.Handle);
                            Require(ResumeThread(thread) != UInt32.MaxValue);
                        }
                        stdoutWrite.Dispose();
                        stdinRead.Dispose();
                        output = Task.Run(() => ReadOutput(stdoutRead));
                        outputOwned = true;
                        using (var input = new FileStream(stdinWrite, FileAccess.Write))
                        {
                            var bytes = Encoding.UTF8.GetBytes(configuration);
                            try { input.Write(bytes, 0, bytes.Length); }
                            finally { Array.Clear(bytes, 0, bytes.Length); }
                        }
                        Exit = Task.Run(() =>
                        {
                            process.WaitForExit();
                            int code = process.ExitCode;
                            output.GetAwaiter().GetResult();
                            Ready.TrySetResult(false);
                            return code;
                        });
                    }
                    finally
                    {
                        stdoutWrite.Dispose();
                        if (!outputOwned)
                            stdoutRead.Dispose();
                    }
                }
            }
            catch
            {
                job.Dispose();
                if (process != null)
                    process.Dispose();
                throw;
            }
        }

        private void ReadOutput(SafeFileHandle stream)
        {
            using (stream)
            using (var reader = new StreamReader(new FileStream(stream, FileAccess.Read)))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("FATAL", StringComparison.Ordinal))
                    {
                        if (line.IndexOf("lookup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            line.IndexOf("resolve", StringComparison.OrdinalIgnoreCase) >= 0)
                            failure = "WireGuard sunucu adı çözümlenemedi";
                        else if (line.IndexOf("inbound/tun", StringComparison.OrdinalIgnoreCase) >= 0)
                            failure = "Windows TUN bağdaştırıcısı veya rotası açılamadı";
                        else if (check)
                            failure = "sing-box WireGuard yapılandırmasını reddetti";
                        Ready.TrySetResult(false);
                    }
                    if (line.IndexOf("sing-box started", StringComparison.Ordinal) >= 0)
                        Ready.TrySetResult(true);
                    if (signal != null && line.IndexOf("endpoint/wireguard[discord-wireguard]", StringComparison.Ordinal) >= 0
                        && line.IndexOf("received handshake response", StringComparison.OrdinalIgnoreCase) >= 0)
                        signal();
                }
            }
        }

        internal async Task Stop()
        {
            if (!Exit.IsCompleted)
            {
                SendInterrupt(process.Id);
                if (await Task.WhenAny(Exit, Task.Delay(4500)) != Exit)
                    job.Dispose();
            }
            if (await Task.WhenAny(Exit, Task.Delay(2000)) != Exit)
                throw new TimeoutException("Motorun kapandığı doğrulanamadı. Normal ağ durumunu kontrol et.");
            await Exit;
        }

        public void Dispose()
        {
            job.Dispose();
            Exit.ContinueWith(ignored => process.Dispose(), TaskScheduler.Default);
        }

        private static void SendInterrupt(int pid)
        {
            if (!AttachConsole((uint)pid))
                return;
            try
            {
                if (SetConsoleCtrlHandler(IntPtr.Zero, true))
                    GenerateConsoleCtrlEvent(0, 0);
            }
            finally { FreeConsole(); }
        }

        private static void Verify(string path, string expected)
        {
            if (!File.Exists(path))
                throw new InvalidDataException("Motor dosyaları bulunamadı. KURULUM.md içindeki Motoru-Hazirla.ps1 adımını uygula.");
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") != expected)
                    throw new InvalidDataException("Motor dosyası beklenen sing-box 1.14.4 sürümüyle eşleşmedi.");
            }
        }

        private static void Require(bool success)
        {
            if (!success)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            internal int Length;
            internal IntPtr Descriptor;
            internal bool Inherit;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Startup
        {
            internal int Size;
            internal IntPtr Reserved, Desktop, Title;
            internal int X, Y, Width, Height, Columns, Rows, Fill, Flags;
            internal short Show, ReservedSize;
            internal IntPtr Reserved2, Stdin, Stdout, Stderr;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInfo
        {
            internal IntPtr Process, Thread;
            internal int Id, ThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            internal long ProcessTime, JobTime;
            internal uint Flags;
            internal UIntPtr MinimumSet, MaximumSet;
            internal uint ActiveProcesses;
            internal UIntPtr Affinity;
            internal uint Priority, Scheduling;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobLimits
        {
            internal BasicLimits Basic;
            internal ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
            internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes attributes, uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes,
            IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory,
            ref Startup startup, out ProcessInfo child);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(SafeFileHandle thread);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(SafeFileHandle process, uint code);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref JobLimits limits, int length);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint pid);
        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();
        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
        [DllImport("kernel32.dll")]
        private static extern bool GenerateConsoleCtrlEvent(uint kind, uint processGroup);
    }
}
