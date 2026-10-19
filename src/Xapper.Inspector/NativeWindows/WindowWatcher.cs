namespace Xapper.Inspector.NativeWindows;

/// <summary>
/// 이 프로세스의 최상위 창이 보이고 사라지는 순간을 시각과 함께 기록한다.
///
/// "조작 뒤에 모달이 떴는가" 는 지금 열린 창만 봐서는 답할 수 없다. 금방 닫힌 대화상자도 있고, 조작 전부터 열려 있던 창도
/// 있기 때문이다. 그래서 뜬 시각을 남겨 두고, 묻는 쪽이 조작 시각 이후의 것만 고르게 한다.
///
/// 이벤트는 전용 스레드의 메시지 루프로 받는다. 앱의 UI 스레드에 걸면 그 스레드가 핸들러나 모달 루프에 붙잡혀 있을 때
/// 기록이 밀리는데, 바로 그때가 모달이 뜨는 순간이다.
/// </summary>
public sealed class WindowWatcher : IDisposable
{
    #region Constants

    /// <summary>담아 두는 최대 기록 수. 넘으면 오래된 것부터 버린다.</summary>
    public const int Capacity = 100;

    /// <summary>모달을 기다릴 때 기록을 다시 보는 간격.</summary>
    private const int ModalPollMs = 20;

    #endregion

    #region Fields

    private readonly object _gate = new();
    private readonly List<WindowSighting> _sightings = [];

    /// <summary>네이티브 쪽이 붙든 콜백. 필드에 두지 않으면 GC 가 거두어 훅이 죽은 대리자를 부른다.</summary>
    private NativeMethods.WinEventProc? _callback;

    private Thread? _thread;
    private uint _nativeThreadId;
    private volatile bool _running;

    #endregion

    #region Public Properties

    /// <summary>훅이 걸려 기록 중인지.</summary>
    internal bool IsRunning => _running;

    #endregion

    #region Public Methods

    /// <summary>전용 스레드를 세우고 훅을 겁니다.</summary>
    /// <returns>훅이 걸렸으면 true.</returns>
    public bool Start()
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Xapper window watcher" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        return _running;
    }

    /// <summary>
    /// <paramref name="since"/> 이후에 뜬 창의 기록. 처음 본 순서대로다. null 이면 담아 둔 전부.
    /// </summary>
    internal List<WindowSighting> Since(DateTime? since)
    {
        lock (_gate)
            return _sightings.Where(sighting => since is null || sighting.ShownAt >= since).ToList();
    }

    /// <summary>창이 가장 최근에 보인 시각. 기록이 없으면 null(감시 시작 전부터 열려 있던 창).</summary>
    internal DateTime? ShownAtOf(IntPtr hwnd)
    {
        lock (_gate)
            return _sightings.LastOrDefault(sighting => sighting.Window.Hwnd == hwnd)?.ShownAt;
    }

    /// <summary><paramref name="since"/> 이후에 뜬 첫 모달. 없으면 null.</summary>
    internal WindowSighting? FirstModalSince(DateTime since)
    {
        lock (_gate)
            return _sightings.FirstOrDefault(sighting => sighting.Window.Modal && sighting.ShownAt >= since);
    }

    /// <summary><paramref name="since"/> 이후에 모달이 뜰 때까지 기다립니다. 취소되면 null.</summary>
    internal async Task<WindowSighting?> WaitForModalAsync(DateTime since, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (FirstModalSince(since) is { } modal)
                return modal;

            try
            {
                await Task.Delay(ModalPollMs, ct);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>메시지 루프를 끝내 훅을 풉니다.</summary>
    public void Dispose()
    {
        if (_running)
            NativeMethods.PostThreadMessage(_nativeThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Private Methods

    /// <summary>훅을 걸고 메시지 루프를 돕니다. 밖으로 나온 이벤트(out-of-context)는 이 루프 안에서 콜백으로 온다.</summary>
    private void Run(ManualResetEventSlim ready)
    {
        _nativeThreadId = NativeMethods.GetCurrentThreadId();
        _callback = OnWinEvent;
        var hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_HIDE, IntPtr.Zero, _callback,
            (uint)Bcl.ProcessId, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);

        _running = hook != IntPtr.Zero;
        ready.Set();
        if (!_running)
            return;

        try
        {
            while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessage(ref message);
            }
        }
        finally
        {
            NativeMethods.UnhookWinEvent(hook);
            _running = false;
        }
    }

    /// <summary>
    /// 창 하나가 보이거나 사라졌을 때. 네이티브 콜백이므로 예외를 밖으로 내지 않는다.
    /// </summary>
    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)
            return;

        try
        {
            var at = TimeOf(eventTime);
            if (eventType == NativeMethods.EVENT_OBJECT_SHOW)
                RecordShown(hwnd, at);
            else
                RecordHidden(hwnd, at);
        }
        catch (Exception)
        {
            // 기록 하나를 놓쳐도 앱과 훅은 계속 돌아야 한다.
        }
    }

    /// <summary>최상위 창이 보였으면 기록합니다. 자식 컨트롤이 보이는 이벤트도 같이 오므로 걸러 낸다.</summary>
    private void RecordShown(IntPtr hwnd, DateTime at)
    {
        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd)
            return;

        if (ProcessWindows.Describe(hwnd) is not { } window)
            return;

        lock (_gate)
        {
            _sightings.Add(new WindowSighting(window, at, null));
            if (_sightings.Count > Capacity)
                _sightings.RemoveAt(0);
        }
    }

    /// <summary>열려 있다고 기록된 창이 사라졌으면 그 시각을 적습니다.</summary>
    private void RecordHidden(IntPtr hwnd, DateTime at)
    {
        lock (_gate)
        {
            var index = _sightings.FindLastIndex(sighting => sighting.Window.Hwnd == hwnd);
            if (index >= 0 && _sightings[index].HiddenAt is null)
                _sightings[index] = _sightings[index] with { HiddenAt = at };
        }
    }

    /// <summary>
    /// 이벤트가 난 시각. 콜백은 메시지 루프를 거쳐 늦게 올 수 있으므로, 이벤트에 붙은 틱으로 지연만큼 되돌린다.
    /// </summary>
    private static DateTime TimeOf(uint eventTime)
    {
        var lagMs = unchecked((uint)Environment.TickCount - eventTime);
        return lagMs > 60_000 ? DateTime.UtcNow : DateTime.UtcNow.AddMilliseconds(-lagMs);
    }

    #endregion
}

/// <summary>창 하나가 보였다가(아직 열려 있으면 HiddenAt 이 null) 사라진 기록.</summary>
/// <param name="Window">보인 순간의 창 설명.</param>
/// <param name="ShownAt">보인 시각(UTC).</param>
/// <param name="HiddenAt">사라진 시각(UTC). 아직 열려 있으면 null.</param>
internal sealed record WindowSighting(NativeWindow Window, DateTime ShownAt, DateTime? HiddenAt);
