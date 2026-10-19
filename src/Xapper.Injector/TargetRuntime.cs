namespace Xapper.Injector;

/// <summary>
/// 대상 앱이 도는 .NET 런타임의 계열.
/// 계열마다 Inspector 빌드도, GenericInjector 가 런타임에 들어가는 길(호스팅 API)도 다르다.
/// </summary>
public enum RuntimeFamily
{
    /// <summary>.NET (Core) 계열. coreclr.dll 위에서 돈다.</summary>
    Core,

    /// <summary>.NET Framework 계열. 4.x 는 clr.dll, 3.5 이하는 mscorwks.dll 위에서 돈다.</summary>
    Framework
}

/// <summary>
/// 대상 앱의 런타임: 계열과 주 버전.
/// .NET Framework 4.x 는 같은 CLR 4 위의 제자리 업그레이드라 주 버전 4 하나로 모두를 나타낸다.
/// </summary>
/// <param name="Family">런타임 계열.</param>
/// <param name="Major">주 버전. Core 는 6, 7, 8, ..., Framework 는 CLR 세대인 2 또는 4.</param>
public readonly record struct TargetRuntime(RuntimeFamily Family, int Major)
{
    #region Constants

    /// <summary>.NET Framework 4.x 앱에 넣는 Inspector 빌드의 TFM. GenericInjector 도 이 이름으로 CLR 4 실행기를 고른다.</summary>
    public const string FrameworkInspectorTfm = "net462";

    /// <summary>.NET (Core) 앱이면 GenericInjector 에 넘기는 실행기 이름. Core 실행기는 하나뿐이다.</summary>
    public const string CoreInjectorFramework = "net6.0-windows";

    #endregion

    #region Public Properties

    /// <summary>
    /// Xapper 가 이 런타임에 Inspector 를 넣을 수 있는지. .NET Framework 3.5 이하(CLR 2)는 지원하지 않는다 —
    /// async·Task·Dispatcher.InvokeAsync 가 없어 Inspector 를 따로 써야 하는데, 그런 앱은 드물다.
    /// </summary>
    public bool IsSupported => Family == RuntimeFamily.Core || Major >= 4;

    /// <summary>GenericInjector 의 <c>ExecuteInDefaultAppDomain</c> 에 넘기는 실행기 이름.</summary>
    public string InjectorFramework => Family == RuntimeFamily.Framework ? FrameworkInspectorTfm : CoreInjectorFramework;

    #endregion

    #region Public Methods

    /// <summary>사람이 읽는 런타임 이름. 오류 문장에 쓴다.</summary>
    public override string ToString() => Family switch
    {
        RuntimeFamily.Core => $".NET {Major}",
        _ when Major >= 4 => ".NET Framework 4.x",
        _ => $".NET Framework {Major}.x (CLR {Major})"
    };

    /// <summary>
    /// 지원하지 않는 런타임이면 그 이유를 담은 예외를, 지원하면 null 을 돌려줍니다.
    /// 붙이기와 띄우기가 같은 문장으로 거절하도록 한곳에 둔다.
    /// </summary>
    /// <param name="subject">거절 문장의 주어 (예: "Process 1234", "'App.exe'").</param>
    /// <returns>거절 예외, 또는 지원하면 null.</returns>
    public InvalidOperationException? Refusal(string subject)
    {
        if (IsSupported)
            return null;

        return new InvalidOperationException(
            $"{subject} runs on {this}, which Xapper does not support. Only .NET Framework 4.x and .NET 6 or later " +
            "can be inspected.");
    }

    #endregion
}
