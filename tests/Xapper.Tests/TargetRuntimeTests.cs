using System.Diagnostics;
using Xapper.Injector;

namespace Xapper.Tests;

/// <summary>
/// 대상 런타임의 판정 규칙을 검증한다. 어떤 런타임이 지원되는지, GenericInjector 에 어느 실행기 이름을 넘기는지가
/// 여기서 정해지고, 그 이름이 틀리면 주입이 E_NOTIMPL 로 조용히 실패한다(실측: 예전 코드가 "net4.0-windows" 를 넘겼다).
/// </summary>
public class TargetRuntimeTests
{
    [Theory]
    [InlineData(RuntimeFamily.Core, 6, true)]
    [InlineData(RuntimeFamily.Core, 9, true)]
    [InlineData(RuntimeFamily.Framework, 4, true)]
    [InlineData(RuntimeFamily.Framework, 2, false)]
    public void IsSupported_CoversCoreAndFramework4ButNotClr2(RuntimeFamily family, int major, bool expected)
    {
        Assert.Equal(expected, new TargetRuntime(family, major).IsSupported);
    }

    [Fact]
    public void InjectorFramework_UsesTheExecutorNamesGenericInjectorRecognizes()
    {
        // Snoop GenericInjector 의 GetExecutor 가 아는 이름은 이 둘뿐이다.
        Assert.Equal("net6.0-windows", new TargetRuntime(RuntimeFamily.Core, 9).InjectorFramework);
        Assert.Equal("net462", new TargetRuntime(RuntimeFamily.Framework, 4).InjectorFramework);
    }

    [Fact]
    public void Refusal_NamesTheUnsupportedRuntime()
    {
        var refusal = new TargetRuntime(RuntimeFamily.Framework, 2).Refusal("Process 7");

        Assert.NotNull(refusal);
        Assert.Contains("Process 7", refusal.Message);
        Assert.Contains(".NET Framework 2.x", refusal.Message);
    }

    [Fact]
    public void Refusal_IsNullForASupportedRuntime()
    {
        Assert.Null(new TargetRuntime(RuntimeFamily.Framework, 4).Refusal("Process 7"));
    }

    [Fact]
    public void DetectRuntime_ReadsThisProcessAsCore()
    {
        using var current = Process.GetCurrentProcess();

        var runtime = NativeInjector.DetectRuntime(current.Id);

        Assert.Equal(new TargetRuntime(RuntimeFamily.Core, Environment.Version.Major), runtime);
    }
}
