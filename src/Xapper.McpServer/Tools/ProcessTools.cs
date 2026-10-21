using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using Xapper.Injector;
using Xapper.McpServer.Infrastructure;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.McpServer.Tools;

/// <summary>
/// WPF 프로세스 목록 조회, 연결(attach), 해제(detach) MCP 도구를 제공하는 클래스.
/// </summary>
[McpServerToolType]
public sealed class ProcessTools
{
    #region Fields

    private readonly SessionManager _sessionManager;
    private readonly WpfProcessInjector _injector;
    private readonly IOperatorNotice _notice;
    private readonly InspectorBuildCheck _buildCheck;

    /// <summary>띄운 앱의 파이프가 생겼는지 다시 확인하는 간격 (밀리초).</summary>
    private const int PipePollMs = 50;

    #endregion

    #region Constructor

    /// <summary>
    /// <see cref="ProcessTools"/>의 새 인스턴스를 생성합니다.
    /// </summary>
    public ProcessTools(
        SessionManager sessionManager, WpfProcessInjector injector, IOperatorNotice notice, InspectorBuildCheck buildCheck)
    {
        _sessionManager = sessionManager;
        _injector = injector;
        _notice = notice;
        _buildCheck = buildCheck;
    }

    #endregion

    #region MCP Tools

    /// <summary>
    /// 현재 실행 중인 WPF 프로세스 목록을 반환합니다.
    /// </summary>
    [McpServerTool(Name = "xapper_list_processes"), Description("List running WPF processes available for attachment")]
    public string ListProcesses()
    {
        var processes = WpfProcessInjector.GetWpfProcesses();

        if (processes.Count == 0)
            return "No WPF processes found.";

        var lines = new List<string> { $"Found {processes.Count} WPF process(es):", "" };
        foreach (var p in processes)
        {
            lines.Add($"  PID={p.ProcessId}  {p.ProcessName}  \"{p.MainWindowTitle}\"");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 대상 WPF 프로세스에 Inspector를 주입하고 IPC 연결을 수립합니다.
    /// 각 단계(주입, 연결, 핑)에서 실패 시 구체적인 에러 메시지를 반환.
    /// </summary>
    [McpServerTool(Name = "xapper_attach"), Description("Attach to a WPF process by injecting the Xapper inspector")]
    public async Task<string> Attach(
        [Description("Process ID of the target WPF application")] int pid,
        [Description("Timeout in ms to wait for connection (default 10000)")] int timeout = 10000,
        CancellationToken ct = default)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        // 이 서버가 재시작됐거나 다른 세션이 붙었다 떠난 프로세스에는 Inspector 가 이미 들어 있고, 그 파이프가 다음
        // 연결을 기다린다. 거기에 다시 주입하면 같은 이름의 어셈블리가 이미 올라와 있다며 실패하므로(0x80131621),
        // 파이프가 있으면 주입 없이 바로 붙는다. 파이프 이름 목록만 본다: File.Exists 는 확인하려고 파이프에 한 번
        // 연결했다 끊어서, 연결을 하나만 받는 Inspector 의 차례를 빼앗는다.
        var alreadyInside = Directory.EnumerateFiles(@"\\.\pipe\", IpcPipeNames.ForProcess(pid)).Any();
        if (!alreadyInside)
        {
            try
            {
                _injector.Inject(pid);
            }
            catch (Exception ex)
            {
                return $"[INJECT FAILED] {ex.Message}";
            }

            try
            {
                await Task.Delay(2000, linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                return "[TIMEOUT] Cancelled during wait for inspector startup";
            }
        }

        try
        {
            await _sessionManager.AttachAsync(pid, linkedCts.Token);
        }
        catch (Exception ex)
        {
            return $"[CONNECT FAILED] {ex.Message}";
        }

        try
        {
            if (await RefuseOtherBuildAsync(pid, linkedCts.Token) is { } refusal)
                return refusal;
        }
        catch (Exception ex)
        {
            return $"[PING FAILED] {ex.Message}";
        }

        return alreadyInside
            ? $"Attached to process {pid}, whose inspector was already running. Connection verified."
            : $"Attached to process {pid}. Connection verified.";
    }

    /// <summary>
    /// 앱을 직접 띄우되 Inspector 가 이미 들어간 채로 시작하게 합니다.
    /// .NET 런타임의 startup hook 은 앱의 진입점보다 먼저 돌므로 주입 절차도, 그 뒤의 고정 대기도 필요 없고
    /// 스플래시·로그인 창처럼 주 창보다 먼저 뜨는 화면도 사정권에 들어온다.
    /// </summary>
    [McpServerTool(Name = "xapper_launch"), Description(
        "Start a WPF app with the inspector already inside it, then attach. Prefer this over xapper_attach " +
        "whenever you can start the app yourself: there is no injection step, so it is faster, and it catches " +
        "the app from its first moment - splash screens and login dialogs that open before the main window. " +
        "The launched app becomes the active session; xapper_detach later disconnects without closing it. " +
        ".NET Framework 4.x apps work too, but have no startup hook: the inspector goes in the moment their first " +
        "window appears, so binding errors raised before that window are not heard. .NET Framework 3.5 and older " +
        "are not supported. For .NET 6+ apps, two things to know: the app's own child processes inherit the hook, " +
        "so a helper that runs on an OLDER .NET than the app will fail to start - attach instead when the app " +
        "spawns such helpers; and a trimmed self-contained build silently never loads the hook, so the call ends " +
        "in a timeout.")]
    public async Task<string> Launch(
        [Description("Absolute path to the app's .exe")] string exePath,
        [Description("Command-line arguments to pass to the app")] string? args = null,
        [Description("Timeout in ms to wait for the app's inspector to come up (default 15000)")] int timeoutMs = 15000,
        [Description("Launch on an unseen isolated desktop (default false). Ask once per task whether the user wants isolated or visible testing.")] bool isolated = false,
        CancellationToken ct = default)
    {
        if (!File.Exists(exePath))
            return $"Error: no file at {exePath}.";

        // 제한 시간은 프로세스를 띄우기 전에 거른다. 띄운 뒤에 거절하면 앱은 돌고 있는데 pid 를 알릴 길이 없어진다.
        if (timeoutMs <= 0)
            return $"Error: timeoutMs must be greater than 0 (got {timeoutMs}).";

        LaunchPlan plan;
        try
        {
            plan = _injector.PlanLaunch(exePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            return $"Error: {ex.Message}";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            // 환경변수를 넘기려면 셸을 거치지 않아야 한다. 작업 디렉터리는 exe 폴더 — WPF 앱이 리소스를
            // 상대 경로로 찾는 것이 보통이다.
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
            // 셸을 거치지 않은 자식은 이 서버의 표준 입출력을 물려받는데, 이 서버의 stdin/stdout 은 MCP 통신 그 자체다.
            // 앱(또는 앱 안에서 도는 주입기)이 콘솔에 한 줄만 써도 JSON-RPC 스트림이 깨지고, 앱이 stdin 을 읽으면
            // 요청을 가로챈다(실측: GenericInjector 의 로그 한 줄이 stdout 에 섞여 클라이언트가 파싱에 실패했다).
            // 그래서 셋 다 따로 잇고, 출력은 버린다.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (!string.IsNullOrWhiteSpace(args))
            startInfo.Arguments = args;
        if (plan is LaunchPlan.StartupHook hook)
            startInfo.Environment["DOTNET_STARTUP_HOOKS"] = hook.InspectorDll;
        startInfo.Environment[IsolatedDesktopEnvironment.Variable] = isolated ? "1" : "0";

        Process? started;
        IsolatedDesktopApp? isolatedApp = null;
        try
        {
            if (isolated)
            {
                var environment = new Dictionary<string, string> { [IsolatedDesktopEnvironment.Variable] = "1" };
                if (plan is LaunchPlan.StartupHook isolatedHook)
                    environment["DOTNET_STARTUP_HOOKS"] = isolatedHook.InspectorDll;
                isolatedApp = IsolatedDesktopApp.Start(IsolatedDesktopApp.NextDesktopName(), exePath, args,
                    startInfo.WorkingDirectory, environment);
                started = isolatedApp.Process;
            }
            else
                started = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return $"Error: could not start {Path.GetFileName(exePath)} ({ex.Message}).";
        }

        if (started is null)
            return $"Error: could not start {Path.GetFileName(exePath)}.";

        // 이 Process 는 Dispose 하지 않는다: Dispose 는 출력 파이프의 읽는 쪽을 닫는데, 앱은 이 메서드가 끝난 뒤에도
        // 계속 돌며 출력을 쓸 수 있다. 버려진 객체의 핸들은 SafeHandle 이 거둔다.
        var process = started;

        // 앱의 출력은 읽어서 버린다. 읽지 않으면 파이프 버퍼가 차는 순간 앱이 콘솔 쓰기에서 멈춘다.
        // 입력은 바로 닫아, stdin 을 읽는 앱이 기다리지 않고 끝을 보게 한다.
        if (!isolated)
        {
            process.StandardInput.Close();
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        var name = Path.GetFileNameWithoutExtension(exePath);
        var watch = Stopwatch.StartNew();
        var pid = process.Id;
        var desktopName = isolatedApp?.DesktopName;
        var transferred = false;
        var closed = isolated
            ? " The app was closed because an app on an isolated desktop cannot be reached later with xapper_attach."
            : "";

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            if (plan is LaunchPlan.InjectAfterStart
                && await InjectAtFirstWindowAsync(process, name, timeoutCts.Token, isolatedApp) is { } injectFailure)
                return injectFailure + closed;

            // 훅은 진입점보다 먼저 돌지만 CLR 기동 자체에 시간이 걸린다. 고정 대기 대신 파이프가 생기는 즉시 붙는다.
            while (!Directory.EnumerateFiles(@"\\.\pipe\", IpcPipeNames.ForProcess(pid)).Any())
            {
                if (process.HasExited)
                    return $"Error: {name} exited with code {process.ExitCode} before the inspector came up." + closed;

                await Task.Delay(PipePollMs, timeoutCts.Token);
            }

            await _sessionManager.AttachAsync(pid, timeoutCts.Token);
            if (isolatedApp is not null)
            {
                _sessionManager.OwnIsolated(isolatedApp);
                transferred = true;
                isolatedApp = null;
            }

            // 이 서버가 방금 넣은 훅이라 대개 같은 빌드다. 그래도 같은 판정을 거쳐야 attach 와 말이 어긋나지 않는다.
            if (await RefuseOtherBuildAsync(pid, timeoutCts.Token) is { } refusal)
                return refusal + closed;
        }
        catch (OperationCanceledException)
        {
            if (isolated && transferred) await _sessionManager.DetachAsync(pid);
            // 바깥 토큰이 취소된 것과 제한 시간이 지난 것은 원인이 다르다. 둘을 같은 문장으로 알리면
            // 호출자가 있지도 않은 기동 문제를 찾아 나서게 된다.
            if (ct.IsCancellationRequested)
                return $"Cancelled while waiting for the inspector in {name} (pid {pid})." + (isolated ? closed : " The app is running.");

            return $"Error: the inspector in {name} (pid {pid}) did not come up within {timeoutMs} ms." +
                   (isolated ? closed : " The app is running - you can retry with xapper_attach.");
        }
        catch (Exception ex)
        {
            // Attach may have succeeded just before ownership transfer or build validation failed.
            if (isolated && _sessionManager.ActiveProcessId == pid) await _sessionManager.DetachAsync(pid);
            return $"Error: connecting to {name} (pid {pid}) failed ({ex.Message})." + closed;
        }
        finally { isolatedApp?.Dispose(); }

        watch.Stop();
        var launched = $"Launched process {process.Id} ({name}) in {watch.Elapsed.TotalSeconds:F1} s. " +
                       "The app may still be starting; UI tools wait for its WPF Application.";
        if (isolated)
            launched += $" It is on isolated desktop {desktopName}: its windows are not on the person's screen. " +
                        "Use render screenshots; detach or server exit closes this app and its children.";

        return plan is LaunchPlan.InjectAfterStart afterStart
            ? launched + $" It runs on {afterStart.Runtime}, so the inspector went in when its first window " +
              "appeared: binding errors raised before then were not heard."
            : launched;
    }

    /// <summary>
    /// 현재 연결된 WPF 프로세스와의 세션을 해제합니다.
    /// </summary>
    [McpServerTool(Name = "xapper_detach"), Description("Detach from the currently attached WPF process")]
    public async Task<string> Detach(
        [Description("Process ID (optional, defaults to active session)")] int? pid = null,
        CancellationToken ct = default)
    {
        var targetPid = pid ?? _sessionManager.ActiveProcessId;
        var wasIsolated = targetPid is { } ownedPid && _sessionManager.IsIsolated(ownedPid);
        try
        {
            await _sessionManager.DetachAsync(pid, ct);
        }
        catch (InvalidOperationException) when (targetPid is null)
        {
            // DetachAsync 는 활성 세션이 없을 때만 이 예외를 던진다 — 이미 분리된 상태이므로 오류가 아니다.
            // (다른 예외는 삼키지 않는다: 연결 정리가 실제로 실패한 것을 성공으로 위장하면 세션이 어긋난 채 남는다.)
            await _notice.HideAsync(ct);
            return "Already detached (no active session).";
        }

        // 세션이 끝나면 조작도 끝난 것이다. 알림이 남아 사람이 헛되이 기다리지 않게 내린다 — 분리가 거절된
        // 경우(다른 pid)는 세션이 살아 있으므로 그대로 둔다.
        await _notice.HideAsync(ct);

        if (wasIsolated)
            return $"Detached from process {targetPid}; the isolated app and its children were closed.";

        return pid.HasValue
            ? $"Detached from process {pid}."
            : "Detached from active session.";
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 방금 띄운 .NET Framework 앱의 첫 창이 뜨기를 기다렸다가 Inspector 를 주입합니다.
    /// startup hook 이 없는 런타임이라 이것이 가장 이른 때다. 주입은 창이 있어야 하므로(<see cref="WpfProcessInjector.Inject"/>)
    /// 창이 뜨기를 기다리고, 그 창이 Application 보다 먼저 뜨는 스플래시일 수 있어 Inspector 에게 Application 을 기다리게 한다.
    /// </summary>
    /// <param name="process">방금 띄운 프로세스.</param>
    /// <param name="name">오류 문장에 쓸 앱 이름.</param>
    /// <param name="ct">제한 시간이 걸린 취소 토큰.</param>
    /// <returns>주입했으면 null, 아니면 실패 문장.</returns>
    /// <exception cref="OperationCanceledException">창이 뜨기 전에 제한 시간이 지난 경우.</exception>
    private async Task<string?> InjectAtFirstWindowAsync(Process process, string name, CancellationToken ct,
        IsolatedDesktopApp? isolatedApp = null)
    {
        var window = IntPtr.Zero;
        while (true)
        {
            if (process.HasExited)
                return $"Error: {name} exited with code {process.ExitCode} before its first window appeared.";

            process.Refresh();
            window = isolatedApp is not null ? isolatedApp.FindVisibleWindow() : process.MainWindowHandle;
            if (window != IntPtr.Zero)
                break;

            await Task.Delay(PipePollMs, ct);
        }

        try
        {
            if (isolatedApp is not null)
                _injector.InjectLaunched(process.Id, window);
            else
                _injector.Inject(process.Id, waitForApplication: true);
            return null;
        }
        catch (Exception ex)
        {
            return $"[INJECT FAILED] {ex.Message}" + (isolatedApp is null ? $" The app is running (pid {process.Id})." : $" (pid {process.Id}).");
        }
    }

    /// <summary>
    /// 방금 연결한 Inspector 가 이 서버와 같은 빌드인지 핑으로 확인하고, 아니면 연결을 끊고 거절 문장을 돌려줍니다.
    /// </summary>
    /// <param name="pid">연결한 프로세스.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>같은 빌드면 null, 아니면 거절 문장.</returns>
    private async Task<string?> RefuseOtherBuildAsync(int pid, CancellationToken ct)
    {
        var pong = await _sessionManager.GetActive().PingAsync(ct);
        var reported = pong.Payload is { } payload
            ? IpcSerializer.DeserializePayload<PongResponse>(payload).InspectorBuild
            : null;

        if (_buildCheck.Refusal(pid, reported) is not { } refusal)
            return null;

        await _sessionManager.DetachAsync(pid, ct);
        return refusal;
    }

    #endregion
}
