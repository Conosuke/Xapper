namespace Xapper.Injector;

/// <summary>
/// 띄울 앱에 Inspector 를 어떻게 넣을지. 런타임 계열에 따라 길이 둘로 갈린다.
/// </summary>
public abstract record LaunchPlan
{
    #region Constructor

    /// <summary>아래의 두 경우만 있도록 바깥에서 상속하지 못하게 한다.</summary>
    private LaunchPlan()
    {
    }

    #endregion

    #region Cases

    /// <summary>
    /// .NET (Core) 앱: <c>DOTNET_STARTUP_HOOKS</c> 로 앱의 진입점보다 먼저 Inspector 를 올린다.
    /// 주입이 없고, 주 창보다 먼저 뜨는 화면과 그 화면의 바인딩 오류까지 잡힌다.
    /// </summary>
    /// <param name="InspectorDll">startup hook 으로 넘길 Inspector DLL 경로.</param>
    public sealed record StartupHook(string InspectorDll) : LaunchPlan;

    /// <summary>
    /// .NET Framework 앱: startup hook 이 없으므로 띄운 뒤 첫 창이 뜨는 즉시 주입한다.
    /// 그 사이에 지나간 것(첫 창이 뜨기 전의 바인딩 오류 등)은 잡히지 않는다.
    /// </summary>
    /// <param name="Runtime">앱의 런타임.</param>
    public sealed record InjectAfterStart(TargetRuntime Runtime) : LaunchPlan;

    #endregion
}
