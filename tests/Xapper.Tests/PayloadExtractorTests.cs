using Xapper.McpServer.Infrastructure;

namespace Xapper.Tests;

/// <summary>
/// 서버에 실린 Inspector 페이로드가 TFM 마다 제대로 풀리는지 검증한다. net462 는 System.Text.Json 과 그 의존성을
/// 함께 실어야 하는데, 하나라도 빠지면 .NET Framework 앱 안에서 Inspector 가 첫 직렬화에서 죽는다.
/// </summary>
public class PayloadExtractorTests
{
    [Fact]
    public void ExtractAll_LaysOutTheFrameworkInspectorWithItsDependencies()
    {
        var extractor = new PayloadExtractor();

        extractor.ExtractAll();

        var dir = Path.Combine(extractor.InspectorBaseDir, "net462");
        foreach (var file in new[]
                 {
                     "Xapper.Inspector.dll", "Xapper.Protocol.dll", "MinHook.NET.dll",
                     "System.Text.Json.dll", "System.Memory.dll", "System.Runtime.CompilerServices.Unsafe.dll"
                 })
        {
            Assert.True(File.Exists(Path.Combine(dir, file)), $"{file} was not extracted to {dir}");
        }
    }

    [Fact]
    public void ExtractAll_KeepsTheCoreInspectorsDepsFile()
    {
        var extractor = new PayloadExtractor();

        extractor.ExtractAll();

        Assert.True(File.Exists(Path.Combine(extractor.InspectorBaseDir, "net9.0-windows", "Xapper.Inspector.deps.json")));
    }

    [Fact]
    public void InspectorBuildIds_HasOneBuildPerShippedTfm()
    {
        // net462 와 net6~9: 어느 런타임의 앱에 붙든 그 안의 Inspector 가 같은 빌드인지 판정할 수 있어야 한다.
        Assert.Equal(5, new PayloadExtractor().InspectorBuildIds().Count);
    }
}
