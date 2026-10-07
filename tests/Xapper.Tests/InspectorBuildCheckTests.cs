using Xapper.McpServer.Infrastructure;

namespace Xapper.Tests;

/// <summary>
/// 앱 안의 Inspector 가 서버와 같은 빌드인지 판정하는 규칙과, 서버가 싣고 있는 Inspector 의 빌드 목록을 잰다.
///
/// 실측: VisualPro 등록이 환경 변수로 10월 1일 Inspector 를 고정해, 서버를 다시 배포해도 앱에는 옛 Inspector 가
/// 들어갔다. 응답은 매번 성공이었고 수정은 하나도 닿지 않았다.
/// </summary>
public class InspectorBuildCheckTests
{
    [Fact]
    public void Refusal_IsNull_WhenTheInspectorIsOneThisServerShips()
    {
        var check = new InspectorBuildCheck(["aaaa", "bbbb"]);

        Assert.Null(check.Refusal(42, "bbbb"));
    }

    [Fact]
    public void Refusal_NamesTheProcessAndTheWayOut_WhenTheInspectorIsAnotherBuild()
    {
        var check = new InspectorBuildCheck(["aaaa"]);

        var refusal = check.Refusal(42, "cccc");

        Assert.NotNull(refusal);
        Assert.Contains("process 42", refusal);
        Assert.Contains("xapper_launch", refusal);
    }

    [Fact]
    public void Refusal_TreatsAnInspectorThatReportsNoBuildAsAnotherBuild()
    {
        // 빌드를 알리기 전의 Inspector 는 이 값을 보내지 않는다. 그런 Inspector 가 바로 이번에 수정을 막은 옛 빌드다.
        var check = new InspectorBuildCheck(["aaaa"]);

        Assert.NotNull(check.Refusal(42, null));
    }

    [Fact]
    public void InspectorBuildIds_IncludeTheBuildOfTheInspectorThisServerShips()
    {
        // 서버는 테스트가 참조하는 것과 같은 빌드 산출물을 싣는다. 그 MVID 가 목록에 있어야 같은 빌드로 판정된다.
        var shipped = typeof(Xapper.Inspector.EntryPoint).Assembly.ManifestModule.ModuleVersionId.ToString("N");

        var ids = new PayloadExtractor().InspectorBuildIds();

        Assert.Contains(shipped, ids);
    }
}
