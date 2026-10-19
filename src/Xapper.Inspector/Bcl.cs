using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Xapper.Inspector;

/// <summary>
/// Inspector 가 쓰는 BCL API 중 .NET Framework 4.6.2 에 없는 것을 같은 의미로 채운다.
/// 최신 TFM 에서는 원래 API 를 그대로 부르고, net462 에서만 그 API 가 내부에서 쓰는 것과 같은 Win32 함수를 부른다.
/// 호출부마다 <c>#if</c> 를 두지 않으려고 한곳에 모았다 — 차이가 늘어나면 여기에만 더한다.
/// </summary>
internal static class Bcl
{
    #region Public Properties

    /// <summary>현재 프로세스의 ID. <c>Environment.ProcessId</c> 와 같다.</summary>
    public static int ProcessId
    {
        get
        {
#if NETFRAMEWORK
            return (int)GetCurrentProcessId();
#else
            return Environment.ProcessId;
#endif
        }
    }

    /// <summary>
    /// 부팅 이후 지난 밀리초. <c>Environment.TickCount64</c> 와 같은 시계(GetTickCount64)라 Win32 가 주는 시각과도
    /// 같은 기준이다. Stopwatch 로 대신하지 않는 이유가 그것이다.
    /// </summary>
    public static long TickCount64
    {
        get
        {
#if NETFRAMEWORK
            return (long)GetTickCount64();
#else
            return Environment.TickCount64;
#endif
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 문자열이 null 이거나 공백뿐인지 봅니다. <c>string.IsNullOrWhiteSpace</c> 와 같지만, .NET Framework 참조 어셈블리에는
    /// 널 주석이 없어 false 일 때 null 이 아님을 컴파일러가 모르므로 그 사실을 여기서 알려 준다.
    /// </summary>
    /// <param name="value">검사할 문자열.</param>
    /// <returns>null, 빈 문자열, 공백뿐이면 true.</returns>
    public static bool IsBlank([NotNullWhen(false)] string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// <paramref name="source"/> 에 <paramref name="value"/> 가 들어 있는지 지정한 비교 방식으로 봅니다.
    /// <c>string.Contains(string, StringComparison)</c> 와 같다.
    /// </summary>
    /// <param name="source">찾을 대상 문자열.</param>
    /// <param name="value">찾을 문자열.</param>
    /// <param name="comparison">비교 방식.</param>
    /// <returns>들어 있으면 true.</returns>
    public static bool Contains(string source, string value, StringComparison comparison)
        => source.IndexOf(value, comparison) >= 0;

    /// <summary>값을 [min, max] 범위로 자릅니다. <c>Math.Clamp</c> 와 같다.</summary>
    /// <param name="value">자를 값.</param>
    /// <param name="min">하한.</param>
    /// <param name="max">상한.</param>
    /// <returns>범위 안으로 잘린 값.</returns>
    public static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    #endregion

    #region Native Methods

#if NETFRAMEWORK
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();
#endif

    #endregion
}
