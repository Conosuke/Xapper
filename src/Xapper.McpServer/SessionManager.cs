using Xapper.McpServer.Ipc;
using Xapper.McpServer.Infrastructure;

namespace Xapper.McpServer;

/// <summary>
/// 주입된 Inspector와의 Named Pipe 연결 세션을 관리하는 클래스.
/// PID별 InspectorClient를 추적하며, 활성 세션(active session) 개념으로 현재 작업 대상 프로세스를 지정.
/// </summary>
public sealed class SessionManager : IAsyncDisposable
{
    #region Fields

    private readonly Dictionary<int, InspectorClient> _sessions = new();
    private readonly Dictionary<int, IsolatedDesktopApp> _isolated = new();
    private readonly HashSet<int> _connecting = new();
    private readonly object _gate = new();
    private bool _disposed;
    private int? _activeProcessId;

    #endregion

    #region Properties

    /// <summary>현재 활성 세션의 프로세스 ID. 세션이 없으면 null.</summary>
    public int? ActiveProcessId { get { lock (_gate) return _activeProcessId; } }

    /// <summary>Captures identity, connection, and isolation together, even if the connection was lost.</summary>
    public InspectorSession? ActiveSession
    {
        get
        {
            lock (_gate)
                return _activeProcessId is { } pid && _sessions.TryGetValue(pid, out var client)
                    ? new InspectorSession(client, pid, _isolated.ContainsKey(pid)) : null;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 지정된 프로세스의 Inspector에 연결하고 활성 세션으로 설정합니다.
    /// </summary>
    /// <param name="processId">연결할 프로세스 ID.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>연결된 <see cref="InspectorClient"/>.</returns>
    /// <exception cref="InvalidOperationException">이미 해당 프로세스에 연결된 경우.</exception>
    public Task<InspectorClient> AttachAsync(int processId, CancellationToken ct = default) => AttachCoreAsync(processId, null, ct);

    /// <summary>Publishes the connected client and isolated ownership atomically, before exposing the active app.</summary>
    public Task<InspectorClient> AttachIsolatedAsync(IsolatedDesktopApp app, CancellationToken ct = default) =>
        AttachCoreAsync(app.Process.Id, app, ct);

    private async Task<InspectorClient> AttachCoreAsync(int processId, IsolatedDesktopApp? app, CancellationToken ct)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sessions.ContainsKey(processId) || !_connecting.Add(processId))
                throw new InvalidOperationException($"Already attached to process {processId}");
        }
        var client = new InspectorClient(processId);
        try
        {
            await client.ConnectAsync(ct);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _sessions.Add(processId, client);
                if (app is not null) _isolated.Add(processId, app);
                _activeProcessId = processId;
            }
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
        finally { lock (_gate) _connecting.Remove(processId); }
    }

    /// <summary>Takes ownership of an existing connection. Launch uses AttachIsolatedAsync to publish policy atomically.</summary>
    public void OwnIsolated(IsolatedDesktopApp app)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var pid = app.Process.Id;
            if (!_sessions.TryGetValue(pid, out var client) || !client.IsConnected || _isolated.ContainsKey(pid))
                throw new InvalidOperationException($"Cannot own isolated process {pid}: a unique connected session is required.");
            _isolated.Add(pid, app);
        }
    }

    /// <summary>Whether this server owns an isolated app for the given session.</summary>
    public bool IsIsolated(int pid) { lock (_gate) return _isolated.ContainsKey(pid); }

    /// <summary>The owned desktop name, or null for an ordinary or absent session.</summary>
    public string? IsolatedDesktopOf(int pid)
    {
        lock (_gate) return _isolated.TryGetValue(pid, out var app) ? app.DesktopName : null;
    }

    /// <summary>
    /// 지정된 프로세스(또는 활성 세션)와의 연결을 해제합니다.
    /// </summary>
    /// <param name="processId">해제할 프로세스 ID. null이면 활성 세션.</param>
    /// <param name="ct">취소 토큰.</param>
    public async Task DetachAsync(int? processId = null, CancellationToken ct = default)
    {
        InspectorClient? client;
        IsolatedDesktopApp? app;
        lock (_gate)
        {
            var pid = processId ?? _activeProcessId
                ?? throw new InvalidOperationException("No active session");
            _sessions.Remove(pid, out client);
            _isolated.Remove(pid, out app);
            if (_activeProcessId == pid)
                _activeProcessId = _sessions.Count > 0 ? _sessions.Keys.First() : null;
        }
        try { if (client is not null) await client.DisposeAsync(); }
        finally { app?.Dispose(); }
    }

    /// <summary>
    /// 현재 활성 세션의 InspectorClient를 반환합니다.
    /// </summary>
    /// <returns>활성 상태의 <see cref="InspectorClient"/>.</returns>
    /// <exception cref="InvalidOperationException">활성 세션이 없거나 연결이 끊어진 경우.</exception>
    public InspectorClient GetActive()
    {
        return GetActiveSession().Client;
    }

    /// <summary>Returns one consistent, connected snapshot instead of joining separately read mutable state.</summary>
    public InspectorSession GetActiveSession()
    {
        var session = ActiveSession
            ?? throw new InvalidOperationException("No active session. Call xapper_attach or xapper_launch first.");
        if (!session.Client.IsConnected)
            throw new InvalidOperationException(session.Isolated
                ? $"Connection to isolated process {session.ProcessId} lost. Detach to close it, then use xapper_launch again."
                : $"Connection to process {session.ProcessId} lost. Detach and re-attach.");
        return session;
    }

    /// <summary>
    /// 모든 세션을 해제하고 리소스를 정리합니다.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        InspectorClient[] clients;
        IsolatedDesktopApp[] apps;
        lock (_gate)
        {
            _disposed = true;
            clients = _sessions.Values.ToArray();
            apps = _isolated.Values.ToArray();
            _sessions.Clear();
            _isolated.Clear();
            _activeProcessId = null;
        }
        var errors = new List<Exception>();
        foreach (var client in clients)
        {
            try { await client.DisposeAsync(); }
            catch (Exception ex) { errors.Add(ex); }
        }
        foreach (var app in apps)
        {
            try { app.Dispose(); }
            catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count > 0)
            throw new AggregateException("One or more sessions could not be cleaned up.", errors);
    }

    #endregion
}
