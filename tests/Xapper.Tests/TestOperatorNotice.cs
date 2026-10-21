using Xapper.McpServer.Infrastructure;

namespace Xapper.Tests;

/// <summary>Records whether a server tool actually requested a visible banner.</summary>
internal sealed class TestOperatorNotice : IOperatorNotice
{
    /// <summary>Number of requests to show the person's banner.</summary>
    public int ShowCount { get; private set; }
    /// <summary>Whether this test banner has been requested.</summary>
    public bool IsVisible { get; private set; }
    /// <summary>Observes the request without creating a real window.</summary>
    public Task<(bool Shown, string? Error)> ShowAsync(string? message, int? targetProcessId, CancellationToken ct)
    {
        ShowCount++;
        IsVisible = true;
        return Task.FromResult<(bool, string?)>((true, null));
    }
    /// <summary>Clears the recorded visibility.</summary>
    public Task HideAsync(CancellationToken ct) { IsVisible = false; return Task.CompletedTask; }
    /// <summary>Tests do not schedule an actual expiry timer.</summary>
    public void KeepAliveForAutoRaised() { }
}
