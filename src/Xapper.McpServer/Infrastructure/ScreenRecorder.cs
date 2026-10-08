using System.Diagnostics;
using System.Globalization;

namespace Xapper.McpServer.Infrastructure;

/// <summary>
/// 데스크톱 전체를 ffmpeg 로 MP4 에 기록한다. 무인 머신에서 돈 E2E 시나리오를, 실패했을 때 사람이 다시 보게 하려는 것이다.
///
/// 모니터 전체를 찍으므로 사람이 쓰는 PC 에서는 다른 창이 함께 찍힌다. 녹화는 대상 앱이 아니라 이 서버 프로세스에서 하므로
/// 앱이 멈추거나 죽는 순간도 남는다. 조각 MP4 로 써서 이 서버가 강제로 끝나도 끊긴 지점까지 재생된다.
/// </summary>
public sealed class ScreenRecorder : IAsyncDisposable
{
    #region Constants

    /// <summary>받는 초당 프레임의 상한. 데스크톱 녹화를 사람이 다시 보는 데 이 이상은 파일만 키운다.</summary>
    private const int MaxFps = 30;

    /// <summary>시작 직후 ffmpeg 가 살아 있는지 확인하기까지 기다리는 시간. 인자·장치 오류는 이 안에 드러난다.</summary>
    private const int StartCheckMs = 700;

    /// <summary>정상 종료를 요청한 뒤 기다리는 시간. 넘기면 끝내고, 그때까지 쓴 조각까지 재생된다.</summary>
    private const int StopTimeoutMs = 10000;

    /// <summary>오류 보고에 붙일 ffmpeg 출력의 마지막 줄 수.</summary>
    private const int TailLines = 5;

    #endregion

    #region Fields

    private readonly Func<string?> _findFfmpeg;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<string> _tail = new();
    private Process? _ffmpeg;
    private string? _path;
    private Stopwatch? _clock;

    #endregion

    #region Constructor

    /// <summary>
    /// <see cref="ScreenRecorder"/>의 새 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="findFfmpeg">ffmpeg 실행 파일 경로를 찾는 방법. 찾지 못하면 null.</param>
    public ScreenRecorder(Func<string?> findFfmpeg)
    {
        _findFfmpeg = findFfmpeg;
    }

    #endregion

    #region Public Methods

    /// <summary>PATH 에서 ffmpeg.exe 를 찾습니다.</summary>
    /// <returns>찾은 전체 경로, 없으면 null.</returns>
    public static string? FindOnPath()
    {
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return folders
            .Select(folder => Path.Combine(folder.Trim('"'), "ffmpeg.exe"))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>데스크톱 전체를 조각 MP4 로 쓰는 ffmpeg 인자. 마지막 인자가 출력 경로다.</summary>
    /// <param name="path">출력 파일.</param>
    /// <param name="fps">초당 프레임.</param>
    public static IReadOnlyList<string> Arguments(string path, int fps) =>
    [
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "gdigrab", "-framerate", fps.ToString(CultureInfo.InvariantCulture), "-i", "desktop",
        // H.264 의 yuv420p 는 가로·세로가 짝수여야 한다. 홀수 해상도 데스크톱에서도 시작하도록 한 줄을 채운다.
        "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2",
        "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
        "-movflags", "frag_keyframe+empty_moov",
        path
    ];

    /// <summary>
    /// 녹화를 시작합니다.
    /// </summary>
    /// <param name="path">출력할 .mp4 의 절대 경로. 폴더가 없으면 만든다.</param>
    /// <param name="fps">초당 프레임(1~30).</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>시작했으면 null, 아니면 오류 문장.</returns>
    public async Task<string?> StartAsync(string path, int fps, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(path))
            return $"Error: path must be absolute (got \"{path}\") - the server's working folder is not one you can know.";
        if (!path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            return $"Error: path must end in .mp4 (got \"{path}\").";
        if (fps is < 1 or > MaxFps)
            return $"Error: fps must be between 1 and {MaxFps} (got {fps}).";

        await _gate.WaitAsync(ct);
        try
        {
            if (_ffmpeg is not null)
                return $"Error: already recording to {_path}. Call xapper_record_stop first.";

            if (_findFfmpeg() is not { } exe)
                return "Error: ffmpeg was not found on PATH, so nothing is being recorded. Install it (for example " +
                       "`winget install Gyan.FFmpeg`), then restart this server: it keeps the PATH it started with, " +
                       "so a running server does not see a new install.";

            if (Path.GetDirectoryName(path) is { } folder)
                Directory.CreateDirectory(folder);

            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in Arguments(path, fps))
                info.ArgumentList.Add(argument);

            lock (_tail)
                _tail.Clear();

            if (Process.Start(info) is not { } process)
                return $"Error: ffmpeg at {exe} did not start.";

            // stdout 을 비우지 않으면 자식 프로세스가 이 서버의 표준출력을 그대로 물려받는데, 그것은 MCP stdio
            // JSON-RPC 채널이다. ffmpeg 가 그 채널에 바이트 하나라도 쓰면 프로토콜이 깨지므로, 읽어서 버린다.
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, e) => Remember(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await Task.Delay(StartCheckMs, ct);
                if (process.HasExited)
                {
                    var code = process.ExitCode;
                    process.Dispose();
                    return $"Error: ffmpeg stopped right away (exit code {code}), so nothing is being recorded: {Tail()}";
                }
            }
            catch (OperationCanceledException)
            {
                // 취소됐다고 ffmpeg 를 그냥 두면 저장도, 참조도 안 된 채 떠돈다. 죽이고 치운 뒤 취소를 그대로 올린다.
                process.Kill(entireProcessTree: true);
                process.Dispose();
                throw;
            }

            _ffmpeg = process;
            _path = path;
            _clock = Stopwatch.StartNew();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 녹화를 멈추고 파일을 마무리합니다.
    /// </summary>
    /// <param name="ct">취소 토큰. 취소되면 ffmpeg 를 바로 끝낸다(쓴 조각까지는 재생된다).</param>
    /// <returns>결과 문장. 녹화 중이 아니었으면 오류 문장.</returns>
    public async Task<string> StopAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (_ffmpeg is not { } process || _path is not { } path || _clock is not { } clock)
                return "Error: nothing is being recorded. Call xapper_record_start first.";

            clock.Stop();
            bool graceful;
            try
            {
                graceful = await EndAsync(process, ct);
            }
            finally
            {
                // EndAsync 가 예외로 끝나도 녹화기가 "이미 녹화 중"인 채로 영영 멈추지 않도록, 정리는 항상 한다.
                process.Dispose();
                _ffmpeg = null;
                _path = null;
                _clock = null;
            }

            if (!File.Exists(path))
                return $"Error: ffmpeg ended but {path} was not written: {Tail()}";

            var kilobytes = new FileInfo(path).Length / 1024;
            var note = graceful
                ? string.Empty
                : " ffmpeg did not finish on request and was ended; the file plays up to the last fragment written.";
            return $"Recorded {clock.Elapsed.TotalSeconds:F1} s to {path} ({kilobytes} KB).{note}";
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 녹화 중이면 멈춰 파일을 마무리합니다. 녹화 중이 아니면 <see cref="StopAsync"/> 가 싸게 오류 문장만 돌려주고
    /// 끝나므로, 항상 그대로 부른다. 서버가 정상 종료될 때 DI 컨테이너가 부른다.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }

    #endregion

    #region Private Methods

    /// <summary>ffmpeg 에 'q' 를 보내 정상 종료를 요청하고, 제한 시간을 넘기면 끝냅니다. 정상 종료했으면 true.</summary>
    private static async Task<bool> EndAsync(Process process, CancellationToken ct)
    {
        try
        {
            await process.StandardInput.WriteAsync('q');
            await process.StandardInput.FlushAsync(ct);
        }
        catch (IOException)
        {
            // 이미 끝난 ffmpeg 에는 쓸 수 없다. 아래 대기가 곧바로 끝난다.
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(StopTimeoutMs);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            return false;
        }
    }

    /// <summary>ffmpeg 오류 출력의 마지막 몇 줄을 남깁니다. 출력을 읽지 않으면 버퍼가 차서 ffmpeg 가 멎는다.</summary>
    private void Remember(string? line)
    {
        if (line is null)
            return;

        lock (_tail)
        {
            _tail.Enqueue(line);
            while (_tail.Count > TailLines)
                _tail.Dequeue();
        }
    }

    /// <summary>남겨 둔 ffmpeg 출력. 없으면 그렇다고 적는다.</summary>
    private string Tail()
    {
        lock (_tail)
            return _tail.Count == 0 ? "(ffmpeg printed nothing)" : string.Join(" | ", _tail);
    }

    #endregion
}
