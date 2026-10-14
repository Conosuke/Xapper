using System.Text.Json;
using Xapper.McpServer.Scripting;
using Xapper.Protocol;

namespace Xapper.Tests;

/// <summary>
/// 스크립트가 모달을 띄운 클릭 뒤에도 계속 실행되어 그 대화상자에 답할 수 있는지, 창 API 가 Inspector 에 올바른 요청을
/// 보내고 응답을 객체로 돌려주는지, 드래그 좌표가 그대로 전달되는지 확인한다. 전송은 메서드별로 답하는 가짜다.
/// </summary>
public sealed class ScriptWindowTests
{
    #region Tests

    /// <summary>모달을 띄운 클릭은 실패가 아니므로 다음 줄의 messageBox 가 실행된다.</summary>
    [Fact]
    public async Task Click_that_opened_a_modal_lets_the_script_answer_it()
    {
        var sent = new List<IpcMessage>();
        var runner = RunnerAnswering(sent, method => method switch
        {
            "click" => new { success = true, pending = true, modalDialog = "\"Save?\" (0x1A2B)", message = "opened a modal dialog" },
            _ => new { success = true, message = "Answered" }
        });

        var result = await runner.RunAsync("xapper.click(1, {x: 0.5, y: 0.5}); xapper.messageBox('yes');", 5000, CancellationToken.None);

        Assert.Equal(ScriptOutcome.Ok, result.Outcome);
        Assert.Equal(["click", "messageBox"], sent.Select(message => message.Method));
        Assert.Equal("yes", PayloadOf(sent[1]).GetProperty("button").GetString());
    }

    /// <summary>모달 없이 처리가 끝나지 않은 클릭은 예전처럼 실패다.</summary>
    [Fact]
    public async Task Click_still_pending_without_a_modal_fails_the_script()
    {
        var runner = RunnerAnswering([], _ => new { success = true, pending = true, message = "still busy" });

        var result = await runner.RunAsync("xapper.click(1, {x: 0.5, y: 0.5}); log('after');", 5000, CancellationToken.None);

        Assert.Equal(ScriptOutcome.Fail, result.Outcome);
        Assert.DoesNotContain("after", result.Logs);
    }

    /// <summary>windows 는 since 를 실어 보내고, 응답 JSON 을 그대로 객체로 돌려준다.</summary>
    [Fact]
    public async Task Windows_passes_since_and_returns_an_object()
    {
        var sent = new List<IpcMessage>();
        var runner = RunnerAnswering(sent, _ => new
        {
            now = "2026-10-14T05:12:03.123Z",
            open = Array.Empty<object>(),
            shown = new[] { new { hwnd = "0x1A2B", title = "Save?", className = "#32770", kind = "dialog", modal = true } }
        });

        var result = await runner.RunAsync(
            "var w = xapper.windows({since: '2026-10-14T05:12:00.000Z'}); return w.shown[0].modal && w.now.length > 0;",
            5000, CancellationToken.None);

        Assert.Equal(ScriptOutcome.Ok, result.Outcome);
        Assert.Equal("true", result.ReturnValueJson);
        Assert.Equal("2026-10-14T05:12:00.000Z", PayloadOf(sent[0]).GetProperty("since").GetString());
    }

    /// <summary>드래그의 비율 좌표가 잘리지 않고 그대로 전달된다.</summary>
    [Fact]
    public async Task Drag_sends_fractional_coordinates_as_given()
    {
        var sent = new List<IpcMessage>();
        var runner = RunnerAnswering(sent, _ => new { success = true, message = "Dragged" });

        var result = await runner.RunAsync(
            "xapper.drag(3, {sourceX: 0.25, sourceY: 0.75, ref: 4, targetX: 0.5, targetY: 0.5});", 5000, CancellationToken.None);

        Assert.Equal(ScriptOutcome.Ok, result.Outcome);
        var payload = PayloadOf(Assert.Single(sent));
        Assert.Equal(3, payload.GetProperty("sourceRef").GetInt32());
        Assert.Equal(0.25, payload.GetProperty("sourceX").GetDouble());
        Assert.Equal(0.75, payload.GetProperty("sourceY").GetDouble());
        Assert.Equal(4, payload.GetProperty("targetRef").GetInt32());
        Assert.Equal(0.5, payload.GetProperty("targetX").GetDouble());
    }

    #endregion

    #region Private Methods

    /// <summary>보낸 요청을 기록하고, 메서드 이름에 따라 정한 응답을 돌려주는 러너.</summary>
    private static ScriptRunner RunnerAnswering(List<IpcMessage> sent, Func<string, object> answer)
    {
        return new ScriptRunner(
            send: (request, _) =>
            {
                lock (sent)
                    sent.Add(request);
                return Task.FromResult(new IpcMessage
                {
                    Id = request.Id,
                    Type = "response",
                    Method = request.Method,
                    Payload = JsonSerializer.SerializeToElement(answer(request.Method))
                });
            },
            processId: 1,
            notice: null);
    }

    /// <summary>요청의 페이로드. 없으면 실패.</summary>
    private static JsonElement PayloadOf(IpcMessage message)
    {
        Assert.True(message.Payload.HasValue, $"{message.Method} was sent without a payload");
        return message.Payload.Value;
    }

    #endregion
}
