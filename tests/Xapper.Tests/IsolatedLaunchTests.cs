using System.Diagnostics;
using System.Text.RegularExpressions;
using Xapper.Injector;
using Xapper.McpServer;
using Xapper.McpServer.Infrastructure;
using Xapper.McpServer.Tools;

namespace Xapper.Tests;

/// <summary>Exercises real startup hooks, Framework injection, and failure cleanup on Windows.</summary>
[Collection(DesktopWindowCollection.Name)]
public sealed class IsolatedLaunchTests
{
    #region Helpers

    /// <summary>Builds tools from the same embedded payloads as the production server.</summary>
    internal static ProcessTools Tools(SessionManager sessions, bool wrongBuild = false, TestOperatorNotice? notice = null)
    {
        var payloads = new PayloadExtractor();
        payloads.ExtractAll();
        return new ProcessTools(sessions, new WpfProcessInjector(payloads.InspectorBaseDir, payloads.GenericInjectorDir),
            notice ?? new TestOperatorNotice(), new InspectorBuildCheck(wrongBuild ? ["different"] : payloads.InspectorBuildIds()));
    }

    #endregion

    #region Tests

    [Theory]
    [InlineData("net9.0-windows")]
    [InlineData("net48")]
    public async Task Launch_AttachesOnTheOwnedDesktop_AndDetachClosesIt(string framework)
    {
        await using var sessions = new SessionManager();
        var tools = Tools(sessions);
        var result = await tools.Launch(WindowsTestApps.Find("Xapper.TestApp", framework), isolated: true, timeoutMs: 20000);
        Assert.StartsWith("Launched process", result);
        var pid = Assert.IsType<int>(sessions.ActiveProcessId);
        Assert.True(sessions.IsIsolated(pid));
        using var observer = Process.GetProcessById(pid);
        _ = observer.Handle;
        var name = sessions.IsolatedDesktopOf(pid);
        Assert.NotNull(name);
        var detached = await tools.Detach();
        Assert.Contains("closed", detached);
        Assert.True(observer.WaitForExit(5000));
        Assert.False(WindowsTestApps.DesktopExists(name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedStartup_ClosesInsteadOfOfferingAttach(bool wrongBuild)
    {
        await using var sessions = new SessionManager();
        var tools = Tools(sessions, wrongBuild);
        var result = await tools.Launch(WindowsTestApps.Find("Xapper.TestApp"),
            isolated: true, timeoutMs: wrongBuild ? 15000 : 1);
        Assert.StartsWith("Error:", result);
        Assert.Contains("app was closed", result);
        Assert.Null(sessions.ActiveProcessId);
    }

    [Fact]
    public async Task CancelledStartup_ClosesTheIsolatedApp()
    {
        await using var sessions = new SessionManager();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var result = await Tools(sessions).Launch(WindowsTestApps.Find("Xapper.TestApp"), isolated: true, ct: cancelled.Token);
        Assert.StartsWith("Cancelled", result);
        Assert.Contains("app was closed", result);
        Assert.Null(sessions.ActiveProcessId);
    }

    [Fact]
    public async Task OrdinaryDetach_LeavesTheAppRunning()
    {
        await using var sessions = new SessionManager();
        var tools = Tools(sessions);
        var result = await tools.Launch(WindowsTestApps.Find("Xapper.TestApp"));
        Assert.StartsWith("Launched process", result);
        using var observer = Process.GetProcessById(Assert.IsType<int>(sessions.ActiveProcessId));
        try
        {
            await tools.Detach();
            Assert.False(observer.HasExited);
        }
        finally { if (!observer.HasExited) { observer.Kill(true); observer.WaitForExit(5000); } }
    }

    [Fact]
    public void LaunchInjection_RejectsAWindowThatDoesNotBelongToTheProcess()
    {
        var injector = new WpfProcessInjector(AppContext.BaseDirectory, AppContext.BaseDirectory);
        Assert.Throws<InvalidOperationException>(() => injector.InjectLaunched(Environment.ProcessId, IntPtr.Zero));
    }

    #endregion
}
