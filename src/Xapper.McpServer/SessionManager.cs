using Xapper.McpServer.Ipc;

namespace Xapper.McpServer;

/// <summary>
/// 주입된 Inspector와의 Named Pipe 연결 세션을 관리하는 클래스.
/// PID별 InspectorClient를 추적하며, 활성 세션(active session) 개념으로 현재 작업 대상 프로세스를 지정.
/// </summary>
public sealed class SessionManager : IAsyncDisposable
{
    #region Fields

    private readonly Dictionary<int, InspectorClient> _sessions = new();
    private int? _activeProcessId;

    #endregion

    #region Properties

    /// <summary>현재 활성 세션의 프로세스 ID. 세션이 없으면 null.</summary>
    public int? ActiveProcessId => _activeProcessId;

    #endregion

    #region Public Methods

    /// <summary>
    /// 지정된 프로세스의 Inspector에 연결하고 활성 세션으로 설정합니다.
    /// </summary>
    /// <param name="processId">연결할 프로세스 ID.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>연결된 <see cref="InspectorClient"/>.</returns>
    /// <exception cref="InvalidOperationException">이미 해당 프로세스에 연결된 경우.</exception>
    public async Task<InspectorClient> AttachAsync(int processId, CancellationToken ct = default)
    {
        if (_sessions.ContainsKey(processId))
            throw new InvalidOperationException($"Already attached to process {processId}");

        var client = new InspectorClient(processId);
        await client.ConnectAsync(ct);

        _sessions[processId] = client;
        _activeProcessId = processId;
        return client;
    }

    /// <summary>
    /// 지정된 프로세스(또는 활성 세션)와의 연결을 해제합니다.
    /// </summary>
    /// <param name="processId">해제할 프로세스 ID. null이면 활성 세션.</param>
    /// <param name="ct">취소 토큰.</param>
    public async Task DetachAsync(int? processId = null, CancellationToken ct = default)
    {
        var pid = processId ?? _activeProcessId
            ?? throw new InvalidOperationException("No active session");

        if (_sessions.TryGetValue(pid, out var client))
        {
            await client.DisposeAsync();
            _sessions.Remove(pid);
        }

        // 남은 세션이 없으면 null 이어야 한다. FirstOrDefault 는 빈 목록에서 0 을 내서, 없는 프로세스 0 이 활성인 것처럼 보였다.
        if (_activeProcessId == pid)
            _activeProcessId = _sessions.Count > 0 ? _sessions.Keys.First() : null;
    }

    /// <summary>
    /// 현재 활성 세션의 InspectorClient를 반환합니다.
    /// </summary>
    /// <returns>활성 상태의 <see cref="InspectorClient"/>.</returns>
    /// <exception cref="InvalidOperationException">활성 세션이 없거나 연결이 끊어진 경우.</exception>
    public InspectorClient GetActive()
    {
        if (_activeProcessId == null || !_sessions.TryGetValue(_activeProcessId.Value, out var client))
            throw new InvalidOperationException("No active session. Call xapper_attach first.");

        if (!client.IsConnected)
            throw new InvalidOperationException(
                $"Connection to process {_activeProcessId} lost. Detach and re-attach.");

        return client;
    }

    /// <summary>
    /// 모든 세션을 해제하고 리소스를 정리합니다.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var client in _sessions.Values)
            await client.DisposeAsync();
        _sessions.Clear();
        _activeProcessId = null;
    }

    #endregion
}
