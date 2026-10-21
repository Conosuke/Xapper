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

    #endregion
}
