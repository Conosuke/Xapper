using System.Diagnostics;
using System.IO.Pipes;
using Xapper.McpServer;
using Xapper.McpServer.Infrastructure;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.Tests;

/// <summary>Concurrent activation must not change a launch's client, ownership, or isolation policy.</summary>
[Collection(DesktopWindowCollection.Name)]
public sealed class SessionSnapshotTests
{
    #region Tests

    [Fact]
    public async Task IsolatedConnection_PublishesClientPidAndPolicyTogether()
    {
        var exe = WindowsTestApps.Find("Xapper.TestApp");
        using var app = IsolatedDesktopApp.Start(IsolatedDesktopApp.NextDesktopName(), exe, null,
            Path.GetDirectoryName(exe) ?? ".", new Dictionary<string, string> { ["DOTNET_STARTUP_HOOKS"] = "" });
        var pid = app.Process.Id;
        await using var pipe = FakeInspector.Create(pid);
        var accepting = pipe.WaitForConnectionAsync();
        await using var sessions = new SessionManager();
        var client = await sessions.AttachIsolatedAsync(app);
        await accepting;
        var snapshot = sessions.GetActiveSession();
        Assert.Equal(pid, snapshot.ProcessId);
        Assert.Same(client, snapshot.Client);
        Assert.True(snapshot.Isolated);

        var otherPid = FakeInspector.NextProcessId();
        await using var other = FakeInspector.Create(otherPid);
        var acceptOther = other.WaitForConnectionAsync();
        await sessions.AttachAsync(otherPid);
        await acceptOther;
        Assert.Equal(otherPid, sessions.GetActiveSession().ProcessId);
        Assert.False(sessions.GetActiveSession().Isolated);
        Assert.Equal(pid, snapshot.ProcessId);
        Assert.True(snapshot.Isolated);
    }

    [Fact]
    public async Task BuildCheck_UsesItsClientEvenWhenAnotherSessionIsActive()
    {
        await using var sessions = new SessionManager();
        var pid = FakeInspector.NextProcessId();
        await using var pipe = FakeInspector.Create(pid);
        var accepting = pipe.WaitForConnectionAsync();
        var client = await sessions.AttachAsync(pid);
        await accepting;
        var otherPid = FakeInspector.NextProcessId();
        await using var other = FakeInspector.Create(otherPid);
        var otherAccepting = other.WaitForConnectionAsync();
        await sessions.AttachAsync(otherPid);
        await otherAccepting;
        var responding = Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(5000);
            var ping = await IpcSerializer.DeserializeAsync(pipe, timeout.Token);
            Assert.NotNull(ping);
            await pipe.WriteAsync(IpcSerializer.Serialize(IpcSerializer.CreateResponse(ping.Id,
                new PongResponse { InspectorBuild = "not-shipped" })), timeout.Token);
        });
        using var deadline = new CancellationTokenSource(5000);
        var refusal = await IsolatedLaunchTests.Tools(sessions).RefuseOtherBuildAsync(pid, client, deadline.Token);
        await responding;
        Assert.NotNull(refusal);
        Assert.Contains("different Xapper build", refusal);
        Assert.Equal(otherPid, sessions.ActiveProcessId);
    }

    [Fact]
    public async Task FailedLaunchPing_ClosesItsOwnedAppAfterAnotherSessionBecomesActive()
    {
        var exe = WindowsTestApps.Find("Xapper.TestApp");
        using var app = IsolatedDesktopApp.Start(IsolatedDesktopApp.NextDesktopName(), exe, null,
            Path.GetDirectoryName(exe) ?? ".", new Dictionary<string, string> { ["DOTNET_STARTUP_HOOKS"] = "" });
        using var observer = Process.GetProcessById(app.Process.Id);
        _ = observer.Handle;
        await using var sessions = new SessionManager();
        await using var pipe = FakeInspector.Create(observer.Id);
        var otherPid = FakeInspector.NextProcessId();
        await using var other = FakeInspector.Create(otherPid);
        var acceptOther = other.WaitForConnectionAsync();
        var switched = Task.Run(async () =>
        {
            using var deadline = new CancellationTokenSource(10000);
            await pipe.WaitForConnectionAsync(deadline.Token);
            Assert.NotNull(await IpcSerializer.DeserializeAsync(pipe, deadline.Token));
            // The launched app is already owned. Switch active sessions before breaking its build-check ping.
            Assert.True(sessions.IsIsolated(observer.Id));
            await sessions.AttachAsync(otherPid, deadline.Token);
            await acceptOther;
            pipe.Disconnect();
        });
        using var timeout = new CancellationTokenSource(10000);
        await Assert.ThrowsAnyAsync<Exception>(() => IsolatedLaunchTests.Tools(sessions)
            .AttachLaunchedAsync(observer.Id, app, timeout.Token));
        await switched;
        Assert.True(observer.WaitForExit(5000));
        Assert.False(sessions.IsIsolated(observer.Id));
        Assert.False(WindowsTestApps.DesktopExists(app.DesktopName));
        Assert.Equal(otherPid, sessions.ActiveProcessId);
    }

    #endregion
}
