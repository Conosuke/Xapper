using System.Diagnostics;
using System.Runtime.InteropServices;
using Xapper.Protocol;

namespace Xapper.Injector;

/// <summary>
/// NativeInjector를 사용하여 대상 WPF 프로세스에 Xapper.Inspector를 주입하는 클래스.
/// 대상 프로세스의 .NET 런타임 버전에 맞는 Inspector DLL을 자동 선택하여 주입.
/// </summary>
public sealed class WpfProcessInjector
{
    #region Fields

    private readonly string _inspectorBaseDir;
    private readonly NativeInjector _nativeInjector;

    /// <summary>Inspector 가 제공하는 가장 높은 런타임 세대. 앱이 이보다 새로우면 이 세대의 빌드를 쓴다.</summary>
    private const int HighestSupportedMajor = 9;

    /// <summary>Inspector 가 제공하는 가장 낮은 런타임 세대.</summary>
    private const int LowestSupportedMajor = 6;

    /// <summary>
    /// 이 중 하나라도 올라와 있으면 WPF 프로세스다. wpfgfx_cor3 은 .NET (Core), wpfgfx_v0400 은 .NET Framework 4,
    /// wpfgfx_v0300 은 .NET Framework 3.x 의 렌더링 DLL 이다. 3.x 는 붙을 수 없지만, 목록에 보여야 붙으려 할 때
    /// 지원하지 않는 이유를 들을 수 있다.
    /// </summary>
    private static readonly HashSet<string> WpfModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "PresentationFramework.dll",
        "PresentationFramework.ni.dll",
        "wpfgfx_cor3.dll",
        "wpfgfx_v0400.dll",
        "wpfgfx_v0300.dll"
    };

    #endregion

    #region Constructor

    /// <summary>
    /// <see cref="WpfProcessInjector"/>의 새 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="inspectorBaseDir">TFM별 Inspector DLL이 위치한 기본 디렉토리 경로.</param>
    /// <param name="genericInjectorDir">Snoop GenericInjector DLL이 위치한 디렉토리 경로.</param>
    public WpfProcessInjector(string inspectorBaseDir, string genericInjectorDir)
    {
        if (string.IsNullOrWhiteSpace(inspectorBaseDir))
            throw new ArgumentException("Inspector base directory is required.", nameof(inspectorBaseDir));

        _inspectorBaseDir = inspectorBaseDir;
        _nativeInjector = new NativeInjector(genericInjectorDir);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 대상 프로세스에 Xapper.Inspector.dll을 주입합니다.
    /// 대상의 .NET 런타임을 감지하여 해당 TFM의 Inspector DLL을 선택.
    /// </summary>
    /// <param name="processId">주입 대상 WPF 프로세스의 PID.</param>
    /// <param name="waitForApplication">
    /// 앱의 WPF Application 이 아직 없을 수 있는지. 방금 띄워 첫 창에 주입하는 경우에만 true — 그때는 첫 창이
    /// Application 보다 먼저 뜨는 스플래시일 수 있다.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// 프로세스에 메인 윈도우가 없거나, .NET 앱이 아니거나, 지원하지 않는 런타임이거나, 주입에 실패한 경우.
    /// </exception>
    /// <exception cref="FileNotFoundException">Inspector DLL을 찾을 수 없는 경우.</exception>
    public void Inject(int processId, bool waitForApplication = false)
    {
        using var process = Process.GetProcessById(processId);

        if (process.MainWindowHandle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"Process {processId} ({process.ProcessName}) has no visible main window");

        InjectCore(processId, waitForApplication);
    }

    /// <summary>Injects a launched app after verifying its window on the launcher-owned desktop.</summary>
    public void InjectLaunched(int processId, IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || GetWindowThreadProcessId(windowHandle, out var owner) == 0 || owner != (uint)processId)
            throw new InvalidOperationException($"The launch window does not belong to process {processId}.");
        InjectCore(processId, waitForApplication: true);
    }

    /// <summary>Shares runtime and payload selection after the caller has verified the target window.</summary>
    private void InjectCore(int processId, bool waitForApplication)
    {

        var runtime = NativeInjector.DetectRuntime(processId)
            ?? throw new InvalidOperationException($"Process {processId} does not appear to be a .NET application.");
        if (runtime.Refusal($"Process {processId}") is { } refusal)
            throw refusal;

        var inspectorDllPath = ResolveInspectorDll(runtime);

        if (!File.Exists(inspectorDllPath))
            throw new FileNotFoundException(
                $"Xapper.Inspector.dll not found at: {inspectorDllPath}",
                inspectorDllPath);

        _nativeInjector.Inject(
            processId,
            runtime,
            inspectorDllPath,
            "Xapper.Inspector.EntryPoint",
            "Initialize",
            waitForApplication ? InspectorStartArguments.WaitForApplication : string.Empty);
    }

    /// <summary>
    /// 아직 띄우지 않은 exe 에 Inspector 를 어떻게 넣을지 정합니다.
    /// .NET (Core) 앱은 startup hook 으로 얹을 Inspector DLL 을 고르는데, 앱 런타임보다 높은 TFM 으로 빌드된 어셈블리는
    /// 로드되지 않으므로 앱의 세대에서 시작해 아래로만 내려간다. .NET Framework 앱은 띄운 뒤 주입한다.
    /// </summary>
    /// <param name="exePath">대상 앱의 실행 파일 경로.</param>
    /// <returns>런치 방법.</returns>
    /// <exception cref="InvalidOperationException">런타임을 알 수 없거나 지원하지 않는 경우.</exception>
    /// <exception cref="FileNotFoundException">쓸 수 있는 Inspector 빌드가 없는 경우.</exception>
    public LaunchPlan PlanLaunch(string exePath)
    {
        var name = Path.GetFileName(exePath);
        var runtime = LaunchTargetFramework.RuntimeOf(exePath)
            ?? throw new InvalidOperationException(
                $"'{name}' does not look like a .NET application this server can launch (no readable " +
                "runtimeconfig.json next to it, and not a .NET Framework executable). Self-contained single-file " +
                "apps cannot be launched this way - start the app yourself and use xapper_attach.");
        if (runtime.Refusal($"'{name}'") is { } refusal)
            throw refusal;

        if (runtime.Family == RuntimeFamily.Framework)
            return new LaunchPlan.InjectAfterStart(runtime);

        for (var candidate = Math.Min(runtime.Major, HighestSupportedMajor); candidate >= LowestSupportedMajor; candidate--)
        {
            var path = Path.Combine(_inspectorBaseDir, $"net{candidate}.0-windows", "Xapper.Inspector.dll");
            if (File.Exists(path))
                return new LaunchPlan.StartupHook(path);
        }

        throw new FileNotFoundException(
            $"No Inspector build for .NET {runtime.Major} or lower in {_inspectorBaseDir}.");
    }

    /// <summary>
    /// 지정된 프로세스가 WPF 애플리케이션인지 확인합니다.
    /// WPF 의 네이티브 렌더링 DLL(wpfgfx_*) 또는 PresentationFramework 어셈블리의 로드 여부로 판단.
    /// .NET Framework 에서는 PresentationFramework 가 NGEN 이미지(.ni.dll)로 올라오므로 렌더링 DLL 을 함께 본다.
    /// </summary>
    /// <param name="process">확인할 프로세스.</param>
    /// <returns>WPF 프로세스이면 true.</returns>
    public static bool IsWpfProcess(Process process)
    {
        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                if (module.ModuleName is null)
                    continue;

                if (WpfModules.Contains(module.ModuleName))
                    return true;
            }
        }
        catch
        {
            // 접근 거부 또는 프로세스 종료
        }
        return false;
    }

    /// <summary>
    /// 현재 실행 중인 모든 WPF 프로세스 목록을 반환합니다.
    /// 메인 윈도우가 있는 프로세스만 포함.
    /// </summary>
    /// <returns>WPF 프로세스 정보 목록.</returns>
    public static List<WpfProcessInfo> GetWpfProcesses()
    {
        var result = new List<WpfProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero && IsWpfProcess(process))
                {
                    result.Add(new WpfProcessInfo
                    {
                        ProcessId = process.Id,
                        ProcessName = process.ProcessName,
                        MainWindowTitle = process.MainWindowTitle
                    });
                }
            }
            catch
            {
                // 접근 불가 프로세스 건너뜀
            }
        }
        return result;
    }

    #endregion

    #region Private Methods

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    /// <summary>
    /// 대상 프로세스의 .NET 런타임에 맞는 Inspector DLL 경로를 결정합니다.
    /// .NET Framework 4.x 는 net462 빌드 하나가 모두 맡는다. .NET (Core) 는 TFM이 정확히 일치하지 않으면
    /// 가장 가까운 하위 버전으로 폴백.
    /// </summary>
    /// <param name="runtime">대상의 런타임. 지원하는 런타임이어야 한다.</param>
    private string ResolveInspectorDll(TargetRuntime runtime)
    {
        if (runtime.Family == RuntimeFamily.Framework)
            return Path.Combine(_inspectorBaseDir, TargetRuntime.FrameworkInspectorTfm, "Xapper.Inspector.dll");

        var tfm = $"net{runtime.Major}.0-windows";

        // 정확한 TFM 디렉토리가 있으면 사용
        var exactPath = Path.Combine(_inspectorBaseDir, tfm, "Xapper.Inspector.dll");
        if (File.Exists(exactPath))
            return exactPath;

        // 정확한 TFM이 없으면 가장 가까운 하위 버전으로 폴백
        // (예: net10.0-windows 앱은 net9.0-windows Inspector 사용)
        string[] supportedTfms = ["net9.0-windows", "net8.0-windows", "net7.0-windows", "net6.0-windows"];
        foreach (var fallback in supportedTfms)
        {
            var fallbackPath = Path.Combine(_inspectorBaseDir, fallback, "Xapper.Inspector.dll");
            if (File.Exists(fallbackPath))
                return fallbackPath;
        }

        throw new FileNotFoundException(
            $"No Inspector DLL found for TFM '{tfm}' in {_inspectorBaseDir}");
    }

    #endregion
}

/// <summary>
/// WPF 프로세스의 기본 정보를 담는 데이터 클래스.
/// </summary>
public sealed class WpfProcessInfo
{
    /// <summary>프로세스 ID.</summary>
    public int ProcessId { get; set; }

    /// <summary>프로세스 이름.</summary>
    public required string ProcessName { get; set; }

    /// <summary>메인 윈도우 제목.</summary>
    public string? MainWindowTitle { get; set; }
}
