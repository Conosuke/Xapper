using System.Text.Json;
using Xapper.McpServer.Infrastructure;

namespace Xapper.Tests;

/// <summary>
/// 도구가 받지 않는 인자를 걸러 내는 규칙을 검증하는 테스트 클래스.
/// </summary>
public class UnknownArgumentGuardTests
{
    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement;

    private static readonly JsonElement LaunchSchema = Schema(
        """{"type":"object","properties":{"exePath":{"type":"string"},"args":{"type":"string"},"timeoutMs":{"type":"integer"}}}""");

    [Fact]
    public void Check_RefusesAnArgumentTheToolDoesNotTake()
    {
        // 실측: args 대신 arguments 로 넘기자 서버는 성공으로 답했고 앱은 파일 없이 떴다.
        var refusal = UnknownArgumentGuard.Check("xapper_launch", LaunchSchema, ["exePath", "arguments"]);

        Assert.NotNull(refusal);
        Assert.Contains("\"arguments\"", refusal);
        Assert.Contains("args", refusal);
        Assert.Contains("nothing was done", refusal);
    }

    [Fact]
    public void Check_PassesWhenEveryArgumentIsKnown()
    {
        Assert.Null(UnknownArgumentGuard.Check("xapper_launch", LaunchSchema, ["exePath", "args"]));
    }

    [Fact]
    public void Check_TreatsNamesAsCaseSensitive()
    {
        // 인자는 이름 그대로 묶인다. 대소문자가 다르면 SDK 는 그것도 말없이 버린다.
        Assert.NotNull(UnknownArgumentGuard.Check("xapper_launch", LaunchSchema, ["ExePath"]));
    }

    [Fact]
    public void Check_RefusesAnyArgument_WhenTheToolTakesNone()
    {
        var refusal = UnknownArgumentGuard.Check("xapper_detach", Schema("""{"type":"object"}"""), ["force"]);

        Assert.NotNull(refusal);
        Assert.Contains("none", refusal);
    }

    [Fact]
    public void Check_StaysOutOfTheWay_WhenTheSchemaCannotBeRead()
    {
        // 막을 근거가 없는데 막으면 멀쩡한 호출까지 실패한다.
        Assert.Null(UnknownArgumentGuard.Check("anything", Schema("""{"type":"object","properties":42}"""), ["x"]));
        Assert.Null(UnknownArgumentGuard.Check("anything", Schema("[]"), ["x"]));
    }
}
