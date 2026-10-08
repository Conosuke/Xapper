using ModelContextProtocol.Protocol;
using Xapper.McpServer.Infrastructure;

namespace Xapper.Tests;

/// <summary>
/// 도구 응답 글이 실패인지 판정하는 규칙을 잰다. 이 판정이 틀리면 E2E 시나리오가 실패를 성공으로 넘긴다.
/// </summary>
public class ToolFailureTests
{
    [Theory]
    [InlineData("Error: pass ref or target.")]
    [InlineData("Failed: the element stayed disabled")]
    [InlineData("FAIL: Text expected \"3\" but was \"2\"")]
    [InlineData("FAIL at line 4 (click): no element matches name=Save")]
    [InlineData("SYNTAX ERROR line 2: Unexpected token")]
    [InlineData("TIMEOUT at line 7 — 3 actions completed")]
    [InlineData("[INJECT FAILED] ExecuteInDefaultAppDomain returned error code 2148734497.")]
    [InlineData("[CONNECT FAILED] The operation has timed out.")]
    [InlineData("[PING FAILED] Pipe is broken.")]
    [InlineData("[TIMEOUT] Cancelled during wait for inspector startup")]
    [InlineData("Cancelled while waiting for the inspector in App (pid 12). The app is running.")]
    public void IsFailure_IsTrue_ForEveryFailureShape(string text)
    {
        Assert.True(ToolFailure.IsFailure(text));
    }

    [Theory]
    [InlineData("PASS: Text == \"3\"")]
    [InlineData("Click succeeded")]
    [InlineData("Dragged (1,2) -> (3,4) via synthetic mouse input (no cursor movement) | WARNING: nothing took hold of the mouse")]
    [InlineData("OK in 1.2 s (3 actions)")]
    [InlineData("No WPF processes found.")]
    [InlineData("1. Click succeeded\n2. PASS: Text == \"3\"")]
    public void IsFailure_IsFalse_ForSuccessesAndWarnings(string text)
    {
        Assert.False(ToolFailure.IsFailure(text));
    }

    [Fact]
    public void IsFailure_IsTrue_WhenABatchStoppedEarly()
    {
        // 배치는 번호 붙은 여러 줄이라 첫 글자로 알 수 없다. 멈췄다는 표지로 판정한다.
        var text = $"1. Click succeeded\n2. FAIL: Text expected \"3\" but was \"2\"{ToolFailure.BatchStopped} 1 remaining step(s) not run)";

        Assert.True(ToolFailure.IsFailure(text));
    }

    [Fact]
    public void Mark_SetsIsError_OnAFailure()
    {
        var result = new CallToolResult { Content = [new TextContentBlock { Text = "Error: no file at C:\\x.exe." }] };

        Assert.True(ToolFailure.Mark(result).IsError);
    }

    [Fact]
    public void Mark_LeavesASuccessUnmarked()
    {
        var result = new CallToolResult { Content = [new TextContentBlock { Text = "Click succeeded" }] };

        Assert.NotEqual(true, ToolFailure.Mark(result).IsError);
    }

    [Fact]
    public void Mark_KeepsAnErrorTheSdkAlreadyMarked()
    {
        var result = new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "An error occurred invoking 'xapper_find'." }] };

        Assert.True(ToolFailure.Mark(result).IsError);
    }
}
