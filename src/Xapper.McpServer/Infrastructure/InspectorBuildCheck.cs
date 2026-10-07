namespace Xapper.McpServer.Infrastructure;

/// <summary>
/// 앱 안의 Inspector 가 이 서버가 싣고 있는 것과 같은 빌드인지 판정한다.
///
/// 같은 이름의 어셈블리는 한 프로세스에 두 번 올릴 수 없어서, 예전에 다른 서버가 넣은 Inspector 는 앱을 다시 띄우기
/// 전까지 그대로 남는다. 그런 앱에 붙으면 조작은 그 옛 코드로 돌고, 그 뒤의 수정은 하나도 적용되지 않는데 응답은
/// 여전히 성공으로 나온다(실측: 10월 1일 Inspector 가 든 앱에서 끌기 수정이 닿지 않았다). 그래서 다른 빌드면 붙지 않는다.
/// </summary>
public sealed class InspectorBuildCheck
{
    #region Fields

    private readonly HashSet<string> _shipped;

    #endregion

    #region Constructor

    /// <summary>
    /// <see cref="InspectorBuildCheck"/>의 새 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="shipped">이 서버가 싣고 있는 Inspector 들의 빌드 식별자(TFM마다 하나).</param>
    public InspectorBuildCheck(IEnumerable<string> shipped)
    {
        _shipped = new HashSet<string>(shipped, StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 앱 안의 Inspector 가 알린 빌드가 이 서버의 것이 아니면 거절 문장을 돌려줍니다.
    /// </summary>
    /// <param name="processId">붙으려던 프로세스.</param>
    /// <param name="reported">Inspector 가 핑으로 알린 빌드. 빌드를 알리기 전의 Inspector 면 null.</param>
    /// <returns>같은 빌드면 null, 아니면 거절 문장.</returns>
    public string? Refusal(int processId, string? reported)
    {
        if (reported is not null && _shipped.Contains(reported))
            return null;

        return $"Error: process {processId} is running an inspector from a different Xapper build, so nothing was " +
               "attached - its actions would run that build's code rather than this server's, and every fix made " +
               "since would silently not apply. An inspector cannot be replaced inside a running process: close the " +
               "app and start it again with xapper_launch, or start it yourself and use xapper_attach.";
    }

    #endregion
}
