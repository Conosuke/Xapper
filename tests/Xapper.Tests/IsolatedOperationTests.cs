using System.Windows.Controls;
using Xapper.Inspector;
using Xapper.Inspector.Actions;
using Xapper.McpServer;
using Xapper.McpServer.Infrastructure;
using Xapper.McpServer.Tools;

namespace Xapper.Tests;

/// <summary>Checks policy at operation entry points, including the marker passed into a real launched app.</summary>
[Collection(DesktopWindowCollection.Name)]
public sealed class IsolatedOperationTests
{
    #region Tests

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
