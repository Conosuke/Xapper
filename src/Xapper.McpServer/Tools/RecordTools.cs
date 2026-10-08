using System.ComponentModel;
using ModelContextProtocol.Server;
using Xapper.McpServer.Infrastructure;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 무인 머신에서 돈 시나리오를 사람이 다시 볼 수 있게 데스크톱 녹화를 시작하고 멈추는 MCP 도구.
/// </summary>
[McpServerToolType]
public sealed class RecordTools
{
    #region Fields

    private readonly ScreenRecorder _recorder;

    #endregion

    #region Constructor

    /// <summary>
    /// <see cref="RecordTools"/>의 새 인스턴스를 생성합니다.
    /// </summary>
    public RecordTools(ScreenRecorder recorder)
    {
        _recorder = recorder;
    }

    #endregion

    #region MCP Tools

    /// <summary>데스크톱 녹화를 시작합니다.</summary>
    [McpServerTool(Name = "xapper_record_start"), Description(
        "Start recording the whole desktop to an MP4 with ffmpeg, so an unattended run can be watched back - " +
        "most of all a run that failed. It captures every monitor and every window on them, not only the app: on " +
        "a machine a person is using, their other windows end up in the video, so use it on unattended machines. " +
        "The file plays up to the last second written even if the run is killed. One recording at a time; finish " +
        "it with xapper_record_stop. Needs ffmpeg on the server's PATH.")]
    public async Task<string> Start(
        [Description("Absolute path of the .mp4 to write; its folder is created if missing")] string path,
        [Description("Frames per second, 1-30 (default 10)")] int fps = 10,
        CancellationToken ct = default)
    {
        return await _recorder.StartAsync(path, fps, ct)
               ?? $"Recording the desktop to {path} at {fps} fps. Call xapper_record_stop to finish the file.";
    }

    /// <summary>녹화를 멈추고 파일을 마무리합니다.</summary>
    [McpServerTool(Name = "xapper_record_stop"), Description(
        "Stop the recording xapper_record_start began and finish the file. Returns its path, length and size.")]
    public Task<string> Stop(CancellationToken ct = default)
    {
        return _recorder.StopAsync(ct);
    }

    #endregion
}
