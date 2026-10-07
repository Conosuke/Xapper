using System.IO.Pipes;
using Xapper.Injector;
using Xapper.McpServer;
using Xapper.McpServer.Infrastructure;
using Xapper.McpServer.Tools;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.Tests;

/// <summary>
/// Inspector 가 이미 들어 있는 프로세스에 다시 붙을 때 주입을 건너뛰는지 잰다.
///
/// 실측: MCP 서버를 다시 띄운 뒤 같은 앱에 붙으려 하자, 다시 주입하다가 같은 이름의 어셈블리가 이미 올라와 있다는
/// 0x80131621 로 실패했다. 그 앱 안의 Inspector 는 멀쩡히 다음 연결을 기다리고 있었다.
/// 다만 그 Inspector 가 이 서버와 다른 빌드면 붙지 않는다: 그 앱에서는 이후의 수정이 하나도 적용되지 않는다.
/// </summary>
public class AttachToRunningInspectorTests
{
    [Fact]
    public async Task Attach_ConnectsWithoutInjecting_WhenTheProcessAlreadyHostsAnInspector()
    {
        // 없는 PID 다. 주입을 시도했다면 반드시 실패하므로, 붙었다는 것은 주입을 건너뛰었다는 뜻이다.
        const int pid = 99_999_917;
        await using var pipe = new NamedPipeServerStream(
            IpcPipeNames.ForProcess(pid), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var answering = AnswerOnePingAsync(pipe, ShippedBuild);

        await using var sessions = new SessionManager();
        var tools = NewTools(sessions);

        var result = await tools.Attach(pid, timeout: 5000);
        await answering;

        Assert.Equal($"Attached to process {pid}, whose inspector was already running. Connection verified.", result);
    }

    [Fact]
    public async Task Attach_RefusesAndLetsGo_WhenTheInspectorInsideIsAnotherBuild()
    {
        const int pid = 99_999_918;
        await using var pipe = new NamedPipeServerStream(
            IpcPipeNames.ForProcess(pid), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        // 빌드를 알리기 전의 Inspector 처럼 빌드 없이 답한다.
        var answering = AnswerOnePingAsync(pipe, build: null);

        await using var sessions = new SessionManager();
        var tools = NewTools(sessions);

        var result = await tools.Attach(pid, timeout: 5000);
        await answering;

        Assert.StartsWith($"Error: process {pid} is running an inspector from a different Xapper build", result);
        Assert.Null(sessions.ActiveProcessId);
    }

    #region Helpers

    /// <summary>이 시험에서 서버가 싣고 있다고 보는 Inspector 빌드.</summary>
    private const string ShippedBuild = "0123456789abcdef0123456789abcdef";

    private static ProcessTools NewTools(SessionManager sessions)
    {
        var injector = new WpfProcessInjector(AppContext.BaseDirectory, AppContext.BaseDirectory);
        return new ProcessTools(sessions, injector, new SilentNotice(), new InspectorBuildCheck([ShippedBuild]));
    }

    /// <summary>Inspector 대신 연결을 받아 핑 하나에 답합니다. <paramref name="build"/> 는 핑에 실을 빌드.</summary>
    private static async Task AnswerOnePingAsync(NamedPipeServerStream pipe, string? build)
    {
        await pipe.WaitForConnectionAsync();
        var request = await IpcSerializer.DeserializeAsync(pipe);
        if (request is null)
            throw new InvalidOperationException("핑 요청을 받지 못했다.");

        var reply = IpcSerializer.Serialize(IpcSerializer.CreateResponse(request.Id, new PongResponse { InspectorBuild = build }));
        await pipe.WriteAsync(reply);
        await pipe.FlushAsync();
    }

    /// <summary>알림을 띄우지 않는 대역. attach 는 알림을 쓰지 않는다.</summary>
    private sealed class SilentNotice : IOperatorNotice
    {
        public bool IsVisible => false;

        public Task<(bool Shown, string? Error)> ShowAsync(string? message, int? targetProcessId, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((false, null));

        public Task HideAsync(CancellationToken ct) => Task.CompletedTask;

        public void KeepAliveForAutoRaised()
        {
        }
    }

    #endregion
}
