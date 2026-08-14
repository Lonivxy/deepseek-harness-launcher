using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DshLauncher.Services;

/// <summary>
/// Owns the DeepSeek Harness engine process.
///
/// The engine (pnpm dsh web) is started with a Windows Job Object whose
/// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE flag is set, so the entire process tree
/// (cmd -> pnpm -> node) is guaranteed to die when we close the job — no
/// orphaned background servers.
/// </summary>
public class BackendService : IDisposable
{
    public event Action<string>? LogLine;
    public event Action<bool>? ReadinessChanged;

    private Process? _process;
    private IntPtr _job = IntPtr.Zero;
    private CancellationTokenSource? _readinessCts;

    private readonly string _harnessPath;
    private readonly string _url;

    public bool IsRunning => _process is { HasExited: false };

    public BackendService(string harnessPath, string url)
    {
        _harnessPath = harnessPath;
        _url = url;
    }

    /// <summary>Starts the engine unless it is already responding on its port.</summary>
    public void Start()
    {
        if (IsRunning)
        {
            Emit("Engine is already running (process active).");
            return;
        }

        if (IsReachable())
        {
            Emit($"Engine already responds at {_url} — reusing it.");
            SetReady(true);
            StartReadinessWatcher();
            return;
        }

        Emit($"Starting engine in {_harnessPath} ...");
        CreateKillOnCloseJob();

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            // Nested quotes work for paths without spaces; the default D:\dsh fits.
            Arguments = $"/c \"cd /d \"{_harnessPath}\" && pnpm dsh web\"",
            WorkingDirectory = _harnessPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Emit(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Emit(e.Data); };
        _process.Exited += (_, _) =>
        {
            Emit("Engine process exited.");
            SetReady(false);
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        // Assign the whole tree to the job so Stop() kills everything.
        if (_job != IntPtr.Zero)
        {
            AssignProcessToJobObject(_job, _process.Handle);
        }

        StartReadinessWatcher();
    }

    /// <summary>Stops the engine cleanly by terminating the job (kills the full tree).</summary>
    public void Stop()
    {
        _readinessCts?.Cancel();

        if (_job != IntPtr.Zero)
        {
            TerminateJobObject(_job, 0);
            CloseHandle(_job);
            _job = IntPtr.Zero;
        }

        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    // Belt and braces: kill the whole tree even if the job
                    // could not be assigned (e.g. launched from another job).
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Process already gone.
            }
            _process.Dispose();
            _process = null;
        }

        SetReady(false);
        Emit("Engine stopped.");
    }

    public void Restart()
    {
        Emit("Restarting engine ...");
        Stop();
        Start();
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private void StartReadinessWatcher()
    {
        _readinessCts?.Cancel();
        _readinessCts = new CancellationTokenSource();
        var token = _readinessCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                if (IsReachable())
                {
                    SetReady(true);
                    Emit($"Engine ready at {_url}");
                    return;
                }
                await Task.Delay(2000, token);
            }
        }, token);
    }

    private bool IsReachable()
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(2);
            var response = http.GetAsync(_url).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void SetReady(bool ready)
    {
        try
        {
            ReadinessChanged?.Invoke(ready);
        }
        catch
        {
            // UI may be closing; ignore.
        }
    }

    private void Emit(string line) => LogLine?.Invoke(line);

    private void CreateKillOnCloseJob()
    {
        _job = CreateJobObject(IntPtr.Zero, null);
        if (_job == IntPtr.Zero)
        {
            return;
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(info, ptr, false);
        // JobObjectExtendedLimitInformation = 9
        SetInformationJobObject(_job, 9, ptr, (uint)size);
        Marshal.FreeHGlobal(ptr);
    }

    // ------------------------------------------------------------------
    // Win32 interop for job objects
    // ------------------------------------------------------------------

    private const int JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(
        IntPtr job, int infoClass, IntPtr info, uint infoLength);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll")]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
