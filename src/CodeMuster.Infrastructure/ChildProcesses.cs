using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodeMuster.Infrastructure;

/// <summary>
/// Starts every child process CodeMuster runs, so none outlives it or reads its standard input (D77). On Windows each child
/// joins one job object that the OS closes, killing its members, however CodeMuster exits. Everywhere, the children still
/// running when CodeMuster exits normally or receives SIGTERM or SIGHUP are killed with their trees. SIGKILL cannot be
/// caught, so on macOS and Linux a child outlives a SIGKILLed CodeMuster.
/// </summary>
public static class ChildProcesses
{
    // Process-lifetime resources, created once and never replaced: the OS job handle and the table of live children mirror
    // state the OS already keeps per process, so they are the one exemption from AGENTS.md's "no static mutable state".
    private static readonly Lazy<nint> Job = new(CreateJob);
    private static readonly ConcurrentDictionary<int, Process> Running = new();
    private static readonly Lazy<PosixSignalRegistration[]> Watch = new(StartWatching);

    // Counts starts so a test can prove work stays at a few processes instead of timing it on a shared runner.
    private static long started;

    /// <summary>How many child processes this process has started.</summary>
    internal static long StartedCount => Interlocked.Read(ref started);

    /// <summary>
    /// Starts <paramref name="startInfo"/> tied to this process's lifetime. Its standard input is closed at once unless the
    /// caller redirected it to write input, in which case the caller closes it after writing.
    /// </summary>
    public static Process Start(ProcessStartInfo startInfo)
    {
        _ = Watch.Value;
        _ = Job.Value;
        var closeInput = !startInfo.RedirectStandardInput;
        startInfo.RedirectStandardInput = true;
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{startInfo.FileName} did not start.");
        Interlocked.Increment(ref started);

        // A grandchild the child starts before this line escapes the job; the window is the few microseconds between the two calls.
        if (OperatingSystem.IsWindows() && Job.Value != 0)
        {
            _ = AssignProcessToJobObject(Job.Value, process.Handle);
        }

        var id = process.Id;
        Running[id] = process;
        process.Exited += (_, _) => Running.TryRemove(KeyValuePair.Create(id, process));
        process.EnableRaisingEvents = true;
        if (closeInput)
        {
            process.StandardInput.Close();
        }

        return process;
    }

    private static PosixSignalRegistration[] StartWatching()
    {
        // Ctrl+C (SIGINT) is not here: it cancels the run, which drains and kills its own children (SIG1); ProcessExit catches the rest.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAll();
        return [PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => KillAll()), PosixSignalRegistration.Create(PosixSignal.SIGHUP, _ => KillAll())];
    }

    private static void KillAll()
    {
        foreach (var process in Running.Values)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // Already exited or disposed by its launcher; there is nothing left to kill.
            }
        }
    }

    private static nint CreateJob()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        // A null security descriptor makes the handle non-inheritable, so no child holds the job open after CodeMuster exits.
        var job = CreateJobObjectW(0, 0);
        if (job == 0)
        {
            return 0;
        }

        var limits = new ExtendedLimitInformation { BasicLimitInformation = new BasicLimitInformation { LimitFlags = KillOnJobClose } };
        return SetInformationJobObject(job, ExtendedLimitInformationClass, ref limits, (uint)Marshal.SizeOf<ExtendedLimitInformation>()) != 0 ? job : 0;
    }

    private const uint KillOnJobClose = 0x2000;
    private const int ExtendedLimitInformationClass = 9;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateJobObjectW(nint jobAttributes, nint name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int SetInformationJobObject(nint job, int infoClass, ref ExtendedLimitInformation info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int AssignProcessToJobObject(nint job, nint process);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
