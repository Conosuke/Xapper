using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Xapper.Injector;

/// <summary>
/// Win32 P/Invoke를 사용하여 대상 프로세스에 DLL을 주입하는 네이티브 인젝터.
/// Snoop의 GenericInjector DLL을 LoadLibraryW로 로드한 뒤 ExecuteInDefaultAppDomain을 호출하여
/// .NET 어셈블리의 정적 메서드를 타겟 프로세스에서 실행.
/// </summary>
public sealed class NativeInjector
{
    #region Constants

    private const uint PROCESS_ALL_ACCESS = 0x001FFFFF;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint INFINITE = 0xFFFFFFFF;

    #endregion

    #region P/Invoke

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out uint lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    #endregion

    #region Fields

    private readonly string _genericInjectorDir;

    #endregion

    #region Constructor

    /// <summary>
    /// <see cref="NativeInjector"/>의 새 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="genericInjectorDir">Snoop GenericInjector DLL이 위치한 디렉토리 경로.</param>
    public NativeInjector(string genericInjectorDir)
    {
        if (string.IsNullOrWhiteSpace(genericInjectorDir))
            throw new ArgumentException("GenericInjector directory path is required.", nameof(genericInjectorDir));

        _genericInjectorDir = genericInjectorDir;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 대상 프로세스에 .NET 어셈블리를 주입하고 지정된 정적 메서드를 실행합니다.
    /// GenericInjector DLL을 로드한 뒤 ExecuteInDefaultAppDomain을 호출하여 타겟 프로세스에서 메서드 실행.
    /// </summary>
    /// <param name="processId">주입 대상 프로세스의 PID.</param>
    /// <param name="runtime">대상의 런타임. GenericInjector 가 어느 호스팅 API 로 들어갈지를 정한다.</param>
    /// <param name="assemblyPath">주입할 .NET 어셈블리의 전체 경로.</param>
    /// <param name="typeName">호출할 정적 메서드가 포함된 타입의 전체 이름.</param>
    /// <param name="methodName">호출할 정적 메서드 이름. <c>static int M(string)</c> 꼴이어야 한다.</param>
    /// <param name="argument">그 메서드에 넘길 문자열.</param>
    /// <exception cref="InvalidOperationException">인젝션 과정에서 오류가 발생한 경우.</exception>
    /// <exception cref="FileNotFoundException">GenericInjector DLL을 찾을 수 없는 경우.</exception>
    public void Inject(
        int processId, TargetRuntime runtime, string assemblyPath, string typeName, string methodName, string argument)
    {
        using var process = Process.GetProcessById(processId);
        var arch = GetProcessArchitecture(process);
        var injectorDllPath = ResolveInjectorDll(arch);

        if (!File.Exists(injectorDllPath))
            throw new FileNotFoundException(
                $"GenericInjector DLL not found: {injectorDllPath}", injectorDllPath);

        var hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, processId);
        if (hProcess == IntPtr.Zero)
            throw new InvalidOperationException(
                $"Failed to open process {processId}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

        try
        {
            var loadLibrary = ResolveLoadLibraryW(hProcess, IsWow64(process));
            var injectorHandle = LoadDllIntoProcess(hProcess, injectorDllPath, loadLibrary);
            CallExecuteInDefaultAppDomain(
                hProcess, processId, runtime, injectorDllPath, injectorHandle, assemblyPath, typeName, methodName, argument);
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 타겟 프로세스의 아키텍처에 맞는 GenericInjector DLL 경로를 결정합니다.
    /// </summary>
    private string ResolveInjectorDll(ProcessArchitecture arch)
    {
        var dllName = arch switch
        {
            ProcessArchitecture.X64 => "Snoop.GenericInjector.x64.dll",
            ProcessArchitecture.X86 => "Snoop.GenericInjector.x86.dll",
            ProcessArchitecture.Arm64 => "Snoop.GenericInjector.ARM64.dll",
            _ => throw new InvalidOperationException($"Unsupported process architecture: {arch}")
        };

        return Path.Combine(_genericInjectorDir, dllName);
    }

    /// <summary>
    /// 타겟 프로세스의 CPU 아키텍처를 판별합니다.
    /// </summary>
    private static ProcessArchitecture GetProcessArchitecture(Process process)
    {
        if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            return IsWow64(process) ? ProcessArchitecture.X86 : ProcessArchitecture.Arm64;
        }

        return IsWow64(process) ? ProcessArchitecture.X86 : ProcessArchitecture.X64;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

    /// <summary>
    /// 프로세스가 WOW64에서 실행 중인지 확인합니다 (32비트 프로세스 on 64비트 OS).
    /// </summary>
    private static bool IsWow64(Process process)
    {
        try
        {
            if (IsWow64Process(process.Handle, out var isWow64))
                return isWow64;
        }
        catch
        {
            // 접근 거부 시 현재 프로세스와 동일한 아키텍처로 가정
        }
        return false;
    }

    /// <summary>
    /// 대상 프로세스 안의 LoadLibraryW 주소를 구합니다.
    ///
    /// 비트 수가 같으면 kernel32 는 모든 프로세스에서 같은 주소에 올라가므로 이 서버의 주소를 그대로 쓴다.
    /// 32비트(WOW64) 대상에는 32비트 kernel32 가 따로 올라가 있어 그 베이스와 SysWOW64 파일의 RVA 로 계산한다.
    /// </summary>
    /// <param name="hProcess">대상 프로세스 핸들.</param>
    /// <param name="isWow64">대상이 64비트 OS 위의 32비트 프로세스인지.</param>
    /// <returns>대상 프로세스 기준의 LoadLibraryW 주소.</returns>
    /// <exception cref="InvalidOperationException">주소를 구하지 못한 경우.</exception>
    private static IntPtr ResolveLoadLibraryW(IntPtr hProcess, bool isWow64)
    {
        if (!isWow64)
        {
            var kernel32 = GetModuleHandleW("kernel32.dll");
            if (kernel32 == IntPtr.Zero)
                throw new InvalidOperationException("Failed to get kernel32.dll handle");

            var local = GetProcAddress(kernel32, "LoadLibraryW");
            if (local == IntPtr.Zero)
                throw new InvalidOperationException("Failed to get LoadLibraryW address");

            return local;
        }

        var wowDir = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
        return ResolveRemoteExport(hProcess, Path.Combine(wowDir, "kernel32.dll"), "LoadLibraryW");
    }

    /// <summary>
    /// 대상 프로세스에 이미 올라가 있는 모듈의 export 주소를 그 모듈의 베이스와 파일의 RVA 로 구합니다.
    /// export 가 다른 DLL 로 전달(forward)되면 같은 폴더의 그 DLL 에서 한 번 더 찾는다.
    /// </summary>
    /// <param name="hProcess">대상 프로세스 핸들.</param>
    /// <param name="dllPath">대상에 올라간 것과 같은 DLL 파일의 경로.</param>
    /// <param name="exportName">찾을 함수 이름.</param>
    /// <returns>대상 프로세스 기준의 함수 주소.</returns>
    /// <exception cref="InvalidOperationException">모듈이나 export 를 찾지 못한 경우.</exception>
    private static IntPtr ResolveRemoteExport(IntPtr hProcess, string dllPath, string exportName)
    {
        var export = PeExports.Find(dllPath, exportName)
            ?? throw new InvalidOperationException($"Export '{exportName}' not found in {dllPath}");

        if (export.Forwarder is { } forwarder)
        {
            var dot = forwarder.IndexOf('.');
            if (dot <= 0)
                throw new InvalidOperationException($"Export '{exportName}' in {dllPath} has a malformed forwarder '{forwarder}'");

            var targetDll = Path.Combine(
                Path.GetDirectoryName(dllPath) ?? string.Empty, forwarder[..dot] + ".dll");
            return ResolveRemoteExport(hProcess, targetDll, forwarder[(dot + 1)..]);
        }

        var remoteBase = FindModuleInProcess(hProcess, dllPath);
        return new IntPtr((long)remoteBase + export.Rva);
    }

    /// <summary>
    /// CreateRemoteThread + LoadLibraryW를 사용하여 타겟 프로세스에 DLL을 로드합니다.
    /// 로드 후 EnumProcessModulesEx로 정확한 64비트 HMODULE을 얻습니다.
    /// </summary>
    /// <param name="hProcess">대상 프로세스 핸들.</param>
    /// <param name="dllPath">올릴 DLL 경로.</param>
    /// <param name="loadLibraryAddr">대상 프로세스 기준의 LoadLibraryW 주소.</param>
    /// <returns>로드된 DLL의 모듈 핸들 (HMODULE).</returns>
    private static IntPtr LoadDllIntoProcess(IntPtr hProcess, string dllPath, IntPtr loadLibraryAddr)
    {
        var dllPathBytes = Encoding.Unicode.GetBytes(dllPath + '\0');
        var allocSize = (uint)dllPathBytes.Length;

        var remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, allocSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (remoteMem == IntPtr.Zero)
            throw new InvalidOperationException(
                $"VirtualAllocEx failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

        try
        {
            if (!WriteProcessMemory(hProcess, remoteMem, dllPathBytes, allocSize, out _))
                throw new InvalidOperationException(
                    $"WriteProcessMemory failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

            var hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibraryAddr, remoteMem, 0, out _);
            if (hThread == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"CreateRemoteThread (LoadLibraryW) failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

            try
            {
                WaitForSingleObject(hThread, INFINITE);

                if (!GetExitCodeThread(hThread, out var exitCode))
                    throw new InvalidOperationException("Failed to get LoadLibraryW thread exit code");

                if (exitCode == 0)
                    throw new InvalidOperationException(
                        $"LoadLibraryW failed in target process. DLL path: {dllPath}");
            }
            finally
            {
                CloseHandle(hThread);
            }
        }
        finally
        {
            VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        }

        // GetExitCodeThread는 32비트 DWORD만 반환하므로 64비트 HMODULE이 잘릴 수 있음.
        // EnumProcessModulesEx로 리모트 프로세스의 모듈을 열거하여 정확한 핸들을 얻음.
        return FindModuleInProcess(hProcess, dllPath);
    }

    private const uint LIST_MODULES_ALL = 0x03;

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumProcessModulesEx(IntPtr hProcess, IntPtr[] lphModule, int cb, out int lpcbNeeded, uint dwFilterFlag);

    [DllImport("psapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetModuleFileNameExW(IntPtr hProcess, IntPtr hModule, StringBuilder lpFilename, int nSize);

    /// <summary>
    /// 리모트 프로세스에서 지정된 DLL의 모듈 핸들을 검색합니다.
    /// </summary>
    private static IntPtr FindModuleInProcess(IntPtr hProcess, string dllPath)
    {
        var dllFileName = Path.GetFileName(dllPath);
        var modules = new IntPtr[1024];

        if (!EnumProcessModulesEx(hProcess, modules, modules.Length * IntPtr.Size, out var cbNeeded, LIST_MODULES_ALL))
            throw new InvalidOperationException(
                $"EnumProcessModulesEx failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

        var moduleCount = cbNeeded / IntPtr.Size;
        var sb = new StringBuilder(1024);

        for (int i = 0; i < moduleCount; i++)
        {
            sb.Clear();
            if (GetModuleFileNameExW(hProcess, modules[i], sb, sb.Capacity) > 0)
            {
                var moduleName = Path.GetFileName(sb.ToString());
                if (moduleName.Equals(dllFileName, StringComparison.OrdinalIgnoreCase))
                    return modules[i];
            }
        }

        throw new InvalidOperationException($"Module '{dllFileName}' is not loaded in the target process");
    }

    /// <summary>
    /// 대상 프로세스에 올라간 런타임 DLL 로 .NET 런타임의 계열과 주 버전을 알아냅니다.
    /// coreclr.dll 은 버전 폴더(Microsoft.NETCore.App\9.0.16)에서 주 버전을, clr.dll 은 CLR 4, mscorwks.dll 은 CLR 2 다.
    /// </summary>
    /// <param name="processId">대상 프로세스 PID.</param>
    /// <returns>감지된 런타임. .NET 프로세스가 아니면 null.</returns>
    public static TargetRuntime? DetectRuntime(int processId)
    {
        using var process = Process.GetProcessById(processId);
        TargetRuntime? framework = null;

        foreach (ProcessModule module in process.Modules)
        {
            var moduleName = module.ModuleName;

            if (moduleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase))
            {
                var versionDir = Path.GetFileName(Path.GetDirectoryName(module.FileName));
                if (versionDir is not null && Version.TryParse(versionDir, out var version))
                    return new TargetRuntime(RuntimeFamily.Core, version.Major);
            }
            else if (moduleName.Equals("clr.dll", StringComparison.OrdinalIgnoreCase))
            {
                framework = new TargetRuntime(RuntimeFamily.Framework, 4);
            }
            else if (moduleName.Equals("mscorwks.dll", StringComparison.OrdinalIgnoreCase))
            {
                framework ??= new TargetRuntime(RuntimeFamily.Framework, 2);
            }
        }

        return framework;
    }

    /// <summary>
    /// 이미 로드된 GenericInjector DLL의 ExecuteInDefaultAppDomain 함수를 호출합니다.
    /// 파라미터 문자열을 타겟 프로세스 메모리에 써넣고 CreateRemoteThread로 실행.
    /// </summary>
    private static void CallExecuteInDefaultAppDomain(
        IntPtr hProcess, int processId, TargetRuntime runtime, string injectorDllPath,
        IntPtr injectorHandle, string assemblyPath, string typeName, string methodName, string argument)
    {
        // GenericInjector가 기대하는 파라미터 포맷:
        // "{framework}<|>{assemblyPath}<|>{typeName}<|>{methodName}<|>{argument}<|>{logFile}"
        // framework 는 GenericInjector 가 실행기를 고르는 이름이다: "net6.0-windows"(Core) 또는 "net462"(Framework 4).
        var logFile = Path.Combine(Path.GetTempPath(), $"xapper_inject_{processId}.log");
        var paramString =
            $"{runtime.InjectorFramework}<|>{assemblyPath}<|>{typeName}<|>{methodName}<|>{argument}<|>{logFile}";
        var paramBytes = Encoding.Unicode.GetBytes(paramString + '\0');
        var allocSize = (uint)paramBytes.Length;

        var remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, allocSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (remoteMem == IntPtr.Zero)
            throw new InvalidOperationException(
                $"VirtualAllocEx for params failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

        try
        {
            if (!WriteProcessMemory(hProcess, remoteMem, paramBytes, allocSize, out _))
                throw new InvalidOperationException(
                    $"WriteProcessMemory for params failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

            // 리모트에 올라간 GenericInjector 의 export 주소 = 리모트 모듈 베이스 + 파일의 RVA.
            // 이 서버에 같은 DLL 을 올려 재는 방식은 32비트 대상의 DLL 을 64비트 서버가 올릴 수 없어 쓰지 않는다.
            var funcAddr = GetRemoteExportAddress(injectorDllPath, injectorHandle, "ExecuteInDefaultAppDomain");

            var hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, funcAddr, remoteMem, 0, out _);
            if (hThread == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"CreateRemoteThread (ExecuteInDefaultAppDomain) failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

            try
            {
                WaitForSingleObject(hThread, INFINITE);

                if (!GetExitCodeThread(hThread, out var exitCode))
                    throw new InvalidOperationException("Failed to get ExecuteInDefaultAppDomain thread exit code");

                if (exitCode != 0)
                    throw new InvalidOperationException(
                        $"ExecuteInDefaultAppDomain returned error code {exitCode}. Check log: {logFile}");
            }
            finally
            {
                CloseHandle(hThread);
            }
        }
        finally
        {
            VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        }
    }

    /// <summary>
    /// 리모트에 이미 올라간 DLL 의 export 주소를 그 모듈 핸들과 파일의 RVA 로 구합니다.
    /// </summary>
    /// <param name="dllPath">리모트에 올라간 것과 같은 DLL 파일.</param>
    /// <param name="remoteModuleHandle">리모트에서의 모듈 핸들(베이스 주소).</param>
    /// <param name="exportName">찾을 함수 이름.</param>
    /// <returns>리모트 프로세스 기준의 함수 주소.</returns>
    /// <exception cref="InvalidOperationException">export 가 없거나 다른 DLL 로 전달되는 경우.</exception>
    private static IntPtr GetRemoteExportAddress(string dllPath, IntPtr remoteModuleHandle, string exportName)
    {
        var export = PeExports.Find(dllPath, exportName);
        if (export is not { Forwarder: null } found)
            throw new InvalidOperationException($"Export '{exportName}' not found in {Path.GetFileName(dllPath)}");

        return new IntPtr((long)remoteModuleHandle + found.Rva);
    }

    #endregion
}

/// <summary>
/// 프로세스 CPU 아키텍처를 나타내는 열거형.
/// </summary>
public enum ProcessArchitecture
{
    /// <summary>x86 (32비트).</summary>
    X86,

    /// <summary>x64 (64비트).</summary>
    X64,

    /// <summary>ARM64.</summary>
    Arm64
}
