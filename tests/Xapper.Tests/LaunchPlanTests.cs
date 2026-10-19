using Xapper.Injector;

namespace Xapper.Tests;

/// <summary>
/// 띄울 앱에 Inspector 를 넣는 방법을 고르는 규칙을 검증한다. .NET (Core) 는 startup hook, .NET Framework 4.x 는
/// 띄운 뒤 주입, CLR 2 는 거절 — 이 갈림이 틀리면 Framework 앱에 아무 효과 없는 환경 변수만 걸고 시간 초과로 끝난다.
/// </summary>
public class LaunchPlanTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "xapper-plan-" + Guid.NewGuid().ToString("N"));
    private readonly WpfProcessInjector _injector;

    public LaunchPlanTests()
    {
        Directory.CreateDirectory(_directory);
        _injector = new WpfProcessInjector(_directory, _directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void PlanLaunch_HooksACoreAppWithTheNearestLowerInspector()
    {
        var inspectorDir = Path.Combine(_directory, "net8.0-windows");
        Directory.CreateDirectory(inspectorDir);
        var inspector = Path.Combine(inspectorDir, "Xapper.Inspector.dll");
        File.WriteAllText(inspector, "");
        var exe = Path.Combine(_directory, "App.exe");
        File.WriteAllText(exe, "");
        File.WriteAllText(Path.Combine(_directory, "App.runtimeconfig.json"), """{"runtimeOptions":{"tfm":"net10.0"}}""");

        var plan = _injector.PlanLaunch(exe);

        Assert.Equal(new LaunchPlan.StartupHook(inspector), plan);
    }

    [Fact]
    public void PlanLaunch_InjectsAFramework4AppAfterItStarts()
    {
        var exe = Path.Combine(_directory, "Legacy.exe");
        ManagedImage.Write(exe, "v4.0.30319");

        var plan = _injector.PlanLaunch(exe);

        Assert.Equal(new LaunchPlan.InjectAfterStart(new TargetRuntime(RuntimeFamily.Framework, 4)), plan);
    }

    [Fact]
    public void PlanLaunch_RefusesAClr2AppBeforeStartingIt()
    {
        var exe = Path.Combine(_directory, "Ancient.exe");
        ManagedImage.Write(exe, "v2.0.50727");

        var error = Assert.Throws<InvalidOperationException>(() => _injector.PlanLaunch(exe));

        Assert.Contains("Ancient.exe", error.Message);
        Assert.Contains("does not support", error.Message);
    }

    [Fact]
    public void PlanLaunch_RefusesAnExeItCannotRead()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => _injector.PlanLaunch(Path.Combine(Environment.SystemDirectory, "notepad.exe")));

        Assert.Contains("xapper_attach", error.Message);
    }
}
