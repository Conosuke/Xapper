using System.Text.Json;

namespace Xapper.McpServer.Infrastructure;

/// <summary>
/// 도구가 받지 않는 인자 이름을 걸러 낸다.
///
/// MCP SDK 는 도구 메서드에 없는 인자를 아무 말 없이 버린다. 그러면 부른 쪽은 자기가 준 값이 쓰였다고 믿은 채
/// 다음으로 넘어간다. 실측: <c>xapper_launch</c> 에 파일 경로를 <c>args</c> 대신 <c>arguments</c> 로 넘겼더니,
/// 서버는 성공으로 답했고 앱은 파일 없이 떴다. 원인을 찾는 데 한참이 걸렸다. 안 된 것이 된 것처럼 보이는 응답은
/// 실패보다 비싸다.
///
/// 판단 근거는 도구의 입력 스키마다. 도구마다 따로 검사를 두지 않고, 모든 호출이 지나가는 자리에서 한 번에 막는다.
/// </summary>
public static class UnknownArgumentGuard
{
    #region Public Methods

    /// <summary>
    /// 도구가 받지 않는 인자가 섞여 있으면 그 사실을 알리는 문장을 돌려줍니다.
    /// </summary>
    /// <param name="toolName">불린 도구의 이름.</param>
    /// <param name="inputSchema">그 도구의 입력 스키마. <c>properties</c> 에 받는 인자가 나열되어 있다.</param>
    /// <param name="givenNames">부른 쪽이 넘긴 인자 이름들.</param>
    /// <returns>모르는 인자가 있으면 오류 문장, 없으면 null.</returns>
    public static string? Check(string toolName, JsonElement inputSchema, IEnumerable<string> givenNames)
    {
        var known = KnownNames(inputSchema);

        // 스키마를 읽을 수 없으면 판단하지 않는다. 막을 근거가 없는데 막으면 멀쩡한 호출까지 실패한다.
        if (known is null)
            return null;

        var unknown = givenNames.Where(name => !known.Contains(name)).ToList();
        if (unknown.Count == 0)
            return null;

        var accepted = known.Count == 0 ? "none" : string.Join(", ", known.Order(StringComparer.Ordinal));
        return $"Error: {toolName} does not take {Describe(unknown)}, so nothing was done - an argument with an " +
               $"unrecognised name would otherwise be dropped without a word and the call would look like it worked. " +
               $"It takes: {accepted}.";
    }

    #endregion

    #region Private Methods

    /// <summary>스키마의 <c>properties</c> 에 나열된 이름들. 스키마가 그런 모양이 아니면 null.</summary>
    private static HashSet<string>? KnownNames(JsonElement inputSchema)
    {
        if (inputSchema.ValueKind != JsonValueKind.Object)
            return null;

        if (!inputSchema.TryGetProperty("properties", out var properties))
            return [];

        if (properties.ValueKind != JsonValueKind.Object)
            return null;

        return properties.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>모르는 이름들을 한 조각으로 적습니다.</summary>
    private static string Describe(IReadOnlyList<string> unknown)
    {
        var quoted = unknown.Select(name => $"\"{name}\"").ToList();
        return quoted.Count == 1 ? $"an argument named {quoted[0]}" : $"arguments named {string.Join(", ", quoted)}";
    }

    #endregion
}
