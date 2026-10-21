using System.Windows.Controls;
using Xapper.Inspector;
using Xapper.Inspector.Actions;
using Xapper.McpServer;
using Xapper.McpServer.Infrastructure;
using Xapper.McpServer.Tools;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.Tests;

/// <summary>Checks policy at operation entry points, including the marker passed into a real launched app.</summary>
[Collection(DesktopWindowCollection.Name)]
public sealed class IsolatedOperationTests
{
    #region Tests

    [Theory]
    [InlineData(false, "screen", 1)]
    [InlineData(true, "render", 0)]
    public async Task ScriptFailure_CapturesTheCorrectDesktopAndKeepsIsolatedNoticesInvisible(bool isolated, string expectedMode, int expectedNotices)
    {
        var exe = isolated ? WindowsTestApps.Find("Xapper.TestApp") : "";
        using var app = isolated ? IsolatedDesktopApp.Start(IsolatedDesktopApp.NextDesktopName(), exe, null,
            Path.GetDirectoryName(exe) ?? ".", new Dictionary<string, string> { ["DOTNET_STARTUP_HOOKS"] = "" }) : null;
        var pid = app?.Process.Id ?? FakeInspector.NextProcessId();
        await using var pipe = FakeInspector.Create(pid);
        var accepting = pipe.WaitForConnectionAsync();
        await using var sessions = new SessionManager();
        await sessions.AttachAsync(pid);
        await accepting;
        if (app is not null) sessions.OwnIsolated(app);
        var responding = Task.Run(async () =>
        {
            using var deadline = new CancellationTokenSource(10000);
            var request = await IpcSerializer.DeserializeAsync(pipe, deadline.Token);
            Assert.NotNull(request);
            Assert.Equal("screenshot", request.Method);
            Assert.Equal(expectedMode, request.Payload?.GetProperty("mode").GetString());
            await pipe.WriteAsync(IpcSerializer.Serialize(IpcSerializer.CreateResponse(request.Id, new ScreenshotResponse
            {
                Width = 1, Height = 1, Base64Png = Convert.ToBase64String([1, 2, 3])
            })), deadline.Token);
        });
        var notice = new TestOperatorNotice();
        var result = await new RunTools(sessions, notice).Run("xapper.notice.show('test'); fail('expected failure');");
        await responding;
        Assert.StartsWith("FAIL", result);
        Assert.Equal(expectedNotices, notice.ShowCount);
        var prefix = "screenshot: ";
        var path = result.Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Trim();
        Assert.NotNull(path);
        try { Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DirectRealClick_RefusesBeforeCoordinateOrForegroundSideEffects()
    {
        var previous = IsolatedMode.IsOn;
        try
        {
            IsolatedMode.IsOn = true;
            StaThread.Run(() =>
            {
                // No presentation source: reaching coordinate conversion would throw.
                var result = ClickAction.Execute(new Border(), .5, .5);
                Assert.NotNull(result.Error);
                Assert.Contains("isolated desktop", result.Error);
            });
        }
        finally { IsolatedMode.IsOn = previous; }
    }

    [Fact]
    public async Task LaunchedInspector_RefusesScreenAndForeground_AndServerSkipsNoticesAndRecording()
    {
        await using var sessions = new SessionManager();
        var notice = new TestOperatorNotice();
        var result = await IsolatedLaunchTests.Tools(sessions, notice: notice)
            .Launch(WindowsTestApps.Find("Xapper.TestApp"), isolated: true);
        Assert.StartsWith("Launched process", result);
        var screenshot = await sessions.GetActive().ScreenshotAsync(mode: "screen");
        Assert.Equal("error", screenshot.Type);
        Assert.Contains("isolated desktop", screenshot.Payload?.ToString());
        var windows = new WindowTools(sessions);
        Assert.Contains("isolated desktop", await windows.Window("front"));
        Assert.DoesNotContain("Error:", await windows.Window("maximize"));
        Assert.DoesNotContain("Error:", await windows.Window("restore"));
        Assert.Contains("not needed", await new NoticeTools(sessions, notice).Show());
        Assert.Equal(0, notice.ShowCount);
        var searchedForFfmpeg = false;
        await using var recorder = new ScreenRecorder(() => { searchedForFfmpeg = true; return null; });
        var record = await new RecordTools(recorder, sessions).Start(Path.Combine(Path.GetTempPath(), "isolated.mp4"));
        Assert.StartsWith("Error:", record);
        Assert.Contains("isolated desktop", record);
        Assert.False(searchedForFfmpeg);
        var script = await new RunTools(sessions, notice).Run("xapper.notice.show('test'); return 1;");
        Assert.StartsWith("OK", script);
        Assert.Equal(0, notice.ShowCount);
    }

    #endregion
}
