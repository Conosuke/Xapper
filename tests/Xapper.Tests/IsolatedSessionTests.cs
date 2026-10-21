using System.Diagnostics;
using Xapper.McpServer;
using Xapper.McpServer.Infrastructure;

namespace Xapper.Tests;

/// <summary>Ensures ownership transfer requires a connection and detach ends the invisible app.</summary>
[Collection(DesktopWindowCollection.Name)]
public sealed class IsolatedSessionTests
{
    #region Tests

    [Fact]
    public async Task Detach_ClosesTheOwnedAppAndForgetsIsolation()
    {
        var exe = WindowsTestApps.Find("Xapper.TestApp");
        using var app = IsolatedDesktopApp.Start(IsolatedDesktopApp.NextDesktopName(), exe, null,
            Path.GetDirectoryName(exe) ?? ".", new Dictionary<string, string> { ["DOTNET_STARTUP_HOOKS"] = "" });
        using var observer = Process.GetProcessById(app.Process.Id);
        _ = observer.Handle;
        await using var pipe = FakeInspector.Create(observer.Id);
        var accepting = pipe.WaitForConnectionAsync();
        await using var sessions = new SessionManager();
        await sessions.AttachAsync(observer.Id);
        await accepting;
        sessions.OwnIsolated(app);
        Assert.True(sessions.IsIsolated(observer.Id));
        Assert.Equal(app.DesktopName, sessions.IsolatedDesktopOf(observer.Id));
        Assert.Throws<InvalidOperationException>(() => sessions.OwnIsolated(app));
        await sessions.DetachAsync(observer.Id);
        Assert.True(observer.WaitForExit(5000));
        Assert.False(sessions.IsIsolated(observer.Id));
        Assert.Null(sessions.ActiveProcessId);
        Assert.False(WindowsTestApps.DesktopExists(app.DesktopName));
    }

    [Fact]
    public async Task OwnIsolated_RejectsAnUnconnectedAppWithoutClosingIt()
    {
        var exe = WindowsTestApps.Find("Xapper.TestApp");
        using var app = IsolatedDesktopApp.Start(IsolatedDesktopApp.NextDesktopName(), exe, null,
            Path.GetDirectoryName(exe) ?? ".", new Dictionary<string, string>());
        await using var sessions = new SessionManager();
        Assert.Throws<InvalidOperationException>(() => sessions.OwnIsolated(app));
        Assert.False(app.Process.HasExited);
    }

    [Fact]
    public async Task ManagerDisposal_ClosesEveryOwnedApplication()
    {
        var exe = WindowsTestApps.Find("Xapper.TestApp");
        using var first = IsolatedDesktopApp.Start(IsolatedDesktopApp.NextDesktopName(), exe, null,
            Path.GetDirectoryName(exe) ?? ".", new Dictionary<string, string> { ["DOTNET_STARTUP_HOOKS"] = "" });
        using var second = IsolatedDesktopApp.Start(IsolatedDesktopApp.NextDesktopName(), exe, null,
            Path.GetDirectoryName(exe) ?? ".", new Dictionary<string, string> { ["DOTNET_STARTUP_HOOKS"] = "" });
        using var firstObserver = Process.GetProcessById(first.Process.Id);
        using var secondObserver = Process.GetProcessById(second.Process.Id);
        _ = firstObserver.Handle;
        _ = secondObserver.Handle;
        await using var firstPipe = FakeInspector.Create(firstObserver.Id);
        await using var secondPipe = FakeInspector.Create(secondObserver.Id);
        var firstAccept = firstPipe.WaitForConnectionAsync();
        var secondAccept = secondPipe.WaitForConnectionAsync();
        await using var sessions = new SessionManager();
        await sessions.AttachIsolatedAsync(first);
        await sessions.AttachIsolatedAsync(second);
        await Task.WhenAll(firstAccept, secondAccept);
        await sessions.DisposeAsync();
        Assert.True(firstObserver.WaitForExit(5000));
        Assert.True(secondObserver.WaitForExit(5000));
        Assert.Null(sessions.ActiveSession);
        Assert.False(WindowsTestApps.DesktopExists(first.DesktopName));
        Assert.False(WindowsTestApps.DesktopExists(second.DesktopName));
    }

    #endregion
}
