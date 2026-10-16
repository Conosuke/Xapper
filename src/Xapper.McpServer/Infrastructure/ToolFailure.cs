using ModelContextProtocol.Protocol;

namespace Xapper.McpServer.Infrastructure;

/// <summary>
/// 도구가 돌려준 글이 실패를 뜻하는지 판정하고, 호출 결과에 그 사실을 표준 방식(<c>isError</c>)으로 표시한다.
///
/// 도구들은 실패를 글의 앞부분으로 알린다. 이 판정이 batch·run·호출 필터에 따로 있으면 실패 문구가 하나 늘 때 어느
/// 한쪽만 고쳐져 판정이 어긋난다. 특히 E2E 시나리오는 <c>isError</c> 만 보고 성패를 가리므로, 여기서 놓친 실패는
/// 시나리오에서 성공으로 넘어간다.
/// </summary>
public static class ToolFailure
{
    #region Constants

    /// <summary>
    /// 배치가 남은 단계를 실행하지 않고 멈췄다는 표지. 배치 응답은 번호 붙은 여러 줄이라 첫 글자로는 실패를 알 수 없다.
    /// xapper_batch 가 아닌 다른 도구의 성공한 글에 우연히 이 문구가 섞여 있을 수 있으므로, 이 표지는 <see cref="Mark"/>
    /// 에서 호출한 도구가 xapper_batch 일 때만 판정에 쓴다.
    /// </summary>
    public const string BatchStopped = " (batch stopped;";

    /// <summary>실패한 응답이 시작하는 글. 경고는 성공한 응답 뒤에 붙으므로 여기에 들지 않는다.</summary>
    private static readonly string[] FailurePrefixes =
    [
        "Error:",
        "Failed:",
        "FAIL",
        "SYNTAX ERROR",
        "TIMEOUT",
        "[INJECT FAILED]",
        "[CONNECT FAILED]",
        "[PING FAILED]",
        "[TIMEOUT]",
        "Cancelled"
    ];

    #endregion

    #region Public Methods

    /// <summary>도구 응답 글이 실패를 뜻하는 접두사로 시작하면 true 를 돌려줍니다.</summary>
    /// <param name="text">도구가 돌려준 글.</param>
    public static bool IsFailure(string text)
    {
        return FailurePrefixes.Any(prefix => text.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// 호출 결과의 글이 실패면 <c>isError</c> 를 켭니다. 이미 켜져 있으면(예: SDK 가 예외를 오류로 바꾼 경우) 그대로 둔다.
    /// <see cref="BatchStopped"/> 표지는 호출한 도구가 xapper_batch 일 때만 판정에 더한다 - 다른 도구의 성공한 글이
    /// 우연히 그 문구를 담고 있어도 실패로 잘못 표시되지 않도록 한다.
    /// </summary>
    /// <param name="result">도구 호출 결과.</param>
    /// <param name="toolName">호출된 도구 이름.</param>
    /// <returns>같은 결과 객체.</returns>
    public static CallToolResult Mark(CallToolResult result, string toolName)
    {
        if (result.IsError == true)
            return result;

        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        if (IsFailure(text) || (toolName == "xapper_batch" && text.Contains(BatchStopped, StringComparison.Ordinal)))
            result.IsError = true;

        return result;
    }

    #endregion
}
