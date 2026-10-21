using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Xapper.McpServer.Infrastructure;

/// <summary>Owns a desktop and a kill-on-close job so invisible apps cannot outlive their server session.</summary>
public sealed class IsolatedDesktopApp : IDisposable
{
    #region Fields

    private static int _sequence;
    private IntPtr _desktop;
    private IntPtr _job;

    #endregion

    #region Constructor and Properties

    private IsolatedDesktopApp(string name, IntPtr desktop, IntPtr job, Process process)
    {
        DesktopName = name;
        _desktop = desktop;
        _job = job;
        Process = process;
    }

    /// <summary>The desktop retained until the application has terminated.</summary>
    public string DesktopName { get; }

    /// <summary>The application process; this owner, not its callers, disposes it.</summary>
    public Process Process { get; }

    #endregion

    #region Public Methods

    /// <summary>A server-specific name prevents accidental sharing of an existing desktop.</summary>
    public static string NextDesktopName() => $"xapper-{Environment.ProcessId}-{Interlocked.Increment(ref _sequence)}";

    /// <summary>Creates suspended, assigns the job, then resumes while retaining all lifetime handles.</summary>
    public static IsolatedDesktopApp Start(string desktopName, string exePath, string? args,
        string workingDirectory, IReadOnlyDictionary<string, string> environment)
    {
        var desktop = CreateDesktopW(desktopName, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
        if (desktop == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not create the isolated desktop {desktopName}");

        var job = IntPtr.Zero;
        var info = default(PROCESS_INFORMATION);
        Process? process = null;
        var assigned = false;
        try
        {
            job = CreateKillOnCloseJob();
            info = CreateSuspended(desktopName, exePath, args, workingDirectory, environment);
            if (!AssignProcessToJobObject(job, info.hProcess))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not put the app into its isolated job");
            assigned = true;
            process = Process.GetProcessById(info.dwProcessId);
            _ = process.Handle;
            if (ResumeThread(info.hThread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resume the isolated app");
            return new IsolatedDesktopApp(desktopName, desktop, job, process);
        }
        catch
        {
            // An unassigned suspended process would otherwise survive closing the empty job.
            if (info.hProcess != IntPtr.Zero && !assigned)
                TerminateProcess(info.hProcess, 1);
            if (job != IntPtr.Zero)
                CloseHandle(job);
            if (info.hProcess != IntPtr.Zero)
                WaitForSingleObject(info.hProcess, 5000);
            process?.Dispose();
            CloseDesktop(desktop);
            throw;
        }
        finally
        {
            if (info.hThread != IntPtr.Zero) CloseHandle(info.hThread);
            if (info.hProcess != IntPtr.Zero) CloseHandle(info.hProcess);
        }
    }

    /// <summary>Finds a window on the owned desktop, including Framework apps invisible to MainWindowHandle.</summary>
    public IntPtr FindVisibleWindow()
    {
        var desktop = _desktop;
        if (desktop == IntPtr.Zero)
            return IntPtr.Zero;
        var found = IntPtr.Zero;
        EnumDesktopWindows(desktop, (hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != (uint)Process.Id || !IsWindowVisible(hwnd))
                return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Closing the job terminates the app and all children before releasing its desktop.</summary>
    public void Dispose()
    {
        var job = Interlocked.Exchange(ref _job, IntPtr.Zero);
        if (job == IntPtr.Zero)
            return;
        try
        {
            if (!CloseHandle(job))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not close the isolated app's job");
            if (!Process.WaitForExit(5000))
                throw new TimeoutException("The isolated app did not exit within 5000 ms after its job was closed.");
        }
        finally
        {
            Process.Dispose();
            var desktop = Interlocked.Exchange(ref _desktop, IntPtr.Zero);
            if (desktop != IntPtr.Zero) CloseDesktop(desktop);
        }
    }

    #endregion

    #region Environment and Creation

    /// <summary>Merges overrides without case-sensitive duplicates and supplies the required double NUL.</summary>
    internal static char[] EnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            variables[(string)entry.Key] = (string?)entry.Value ?? "";
        foreach (var entry in overrides)
            variables[entry.Key] = entry.Value;
        var block = new StringBuilder();
        foreach (var entry in variables)
            block.Append(entry.Key).Append('=').Append(entry.Value).Append('\0');
        if (block.Length == 0) block.Append('\0');
        return block.Append('\0').ToString().ToCharArray();
    }

    private static IntPtr CreateKillOnCloseJob()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a job for the isolated app");
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
            return job;
        var error = Marshal.GetLastWin32Error();
        CloseHandle(job);
        throw new Win32Exception(error, "Could not configure kill-on-close for the isolated app");
    }

    private static PROCESS_INFORMATION CreateSuspended(string desktopName, string exePath, string? args,
        string workingDirectory, IReadOnlyDictionary<string, string> environment)
    {
        var startup = new STARTUPINFOW { cb = Marshal.SizeOf<STARTUPINFOW>(), lpDesktop = @"WinSta0\" + desktopName };
        var command = new StringBuilder().Append('"').Append(exePath).Append('"');
        if (!string.IsNullOrWhiteSpace(args)) command.Append(' ').Append(args);
        var block = GCHandle.Alloc(EnvironmentBlock(environment), GCHandleType.Pinned);
        try
        {
            // CREATE_NO_WINDOW also keeps console helpers away from the server's console/stdout.
            if (!CreateProcessW(exePath, command, IntPtr.Zero, IntPtr.Zero, false, 0x4 | 0x400 | 0x08000000,
                    block.AddrOfPinnedObject(), workingDirectory, ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"Could not start {Path.GetFileName(exePath)} on the isolated desktop");
            return info;
        }
        finally { block.Free(); }
    }

    #endregion

    #region Interop

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOW
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    private delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDesktopWindows(IntPtr desktop, EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment,
        string directory, ref STARTUPINFOW startup, out PROCESS_INFORMATION info);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    #endregion
}
