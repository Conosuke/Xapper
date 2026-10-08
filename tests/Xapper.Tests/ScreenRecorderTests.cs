using Xapper.McpServer.Infrastructure;

namespace Xapper.Tests;

/// <summary>
/// 데스크톱 녹화기의 입력 검증과 ffmpeg 연동을 잰다. ffmpeg 가 없는 머신에서는 실제 녹화 시험은 건너뛴다.
/// </summary>
public class ScreenRecorderTests
{
    private static string TempMp4() => Path.Combine(Path.GetTempPath(), "xapper-rec-" + Guid.NewGuid().ToString("N"), "run.mp4");

    [Fact]
    public async Task Start_RefusesARelativePath()
    {
        await using var recorder = new ScreenRecorder(() => "ffmpeg.exe");

        var error = await recorder.StartAsync("out\\run.mp4", 10, CancellationToken.None);

        Assert.StartsWith("Error: path must be absolute", error);
    }

    [Fact]
    public async Task Start_RefusesANonMp4Path()
    {
        await using var recorder = new ScreenRecorder(() => "ffmpeg.exe");

        var error = await recorder.StartAsync(Path.Combine(Path.GetTempPath(), "run.avi"), 10, CancellationToken.None);

        Assert.StartsWith("Error: path must end in .mp4", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public async Task Start_RefusesAnFpsOutOfRange(int fps)
    {
        await using var recorder = new ScreenRecorder(() => "ffmpeg.exe");

        var error = await recorder.StartAsync(TempMp4(), fps, CancellationToken.None);

        Assert.StartsWith("Error: fps must be between 1 and 30", error);
    }

    [Fact]
    public async Task Start_SaysHowToGetFfmpeg_WhenItIsMissing()
    {
        await using var recorder = new ScreenRecorder(() => null);

        var error = await recorder.StartAsync(TempMp4(), 10, CancellationToken.None);

        Assert.NotNull(error);
        Assert.StartsWith("Error: ffmpeg was not found", error);
        Assert.Contains("winget install", error);
    }

    [Fact]
    public async Task Stop_WithNothingRecording_IsAnError()
    {
        await using var recorder = new ScreenRecorder(() => null);

        var text = await recorder.StopAsync(CancellationToken.None);

        Assert.StartsWith("Error: nothing is being recorded", text);
    }

    [Fact]
    public void Arguments_WriteAFragmentedMp4OfTheDesktop()
    {
        // 조각 MP4 라야 서버가 강제로 끝나도 끊긴 지점까지 재생된다. 실패한 실행의 녹화가 가장 필요한 녹화다.
        var args = ScreenRecorder.Arguments("C:\\out\\run.mp4", 12);

        Assert.Equal("C:\\out\\run.mp4", args[^1]);
        Assert.Contains("gdigrab", args);
        Assert.Contains("desktop", args);
        Assert.Contains("frag_keyframe+empty_moov", args);
        Assert.Equal("12", args[args.ToList().IndexOf("-framerate") + 1]);
    }

    [Fact]
    public async Task RecordsAPlayableFile_WhenFfmpegIsInstalled()
    {
        if (ScreenRecorder.FindOnPath() is null)
            return; // ffmpeg 가 없는 머신: 실제 녹화는 확인할 수 없다.

        var path = TempMp4();
        await using var recorder = new ScreenRecorder(ScreenRecorder.FindOnPath);

        Assert.Null(await recorder.StartAsync(path, 10, CancellationToken.None));
        await Task.Delay(1500);
        var summary = await recorder.StopAsync(CancellationToken.None);

        Assert.StartsWith("Recorded", summary);
        Assert.True(new FileInfo(path).Length > 0);
    }
}
