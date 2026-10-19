namespace Xapper.Protocol;

/// <summary>
/// Inspector 진입점(<c>EntryPoint.Initialize(string)</c>)이 받는 인자.
/// 주입하는 쪽(Injector, startup hook)과 Inspector 가 같은 문자열을 봐야 하므로 공유 계약으로 둔다.
/// 상수라 컴파일 때 값이 박히므로, Inspector 가 의존성 리졸버를 등록하기 전에 읽어도 이 어셈블리를 올리지 않는다.
/// </summary>
public static class InspectorStartArguments
{
    /// <summary>
    /// 앱의 WPF Application 이 아직 없을 수 있으니 생길 때까지 기다리라는 뜻. 앱의 진입점보다 먼저 도는 startup hook 과,
    /// 띄우자마자 첫 창에 주입하는 .NET Framework 런치 경로가 쓴다. 이미 돌고 있는 앱에 붙을 때는 빈 문자열을 넘긴다.
    /// </summary>
    public const string WaitForApplication = "wait-for-application";
}
