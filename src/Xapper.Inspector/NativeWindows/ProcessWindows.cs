using System.Diagnostics;
using System.Globalization;

namespace Xapper.Inspector.NativeWindows;

/// <summary>
/// 이 프로세스가 소유한 최상위 창을 열거하고 설명한다.
/// 바탕화면 전체에서 찾으면 다른 프로그램의 대화상자(메신저의 확인 창 등)를 건드리게 되므로, 언제나 프로세스 ID 로 거른다.
/// </summary>
internal static class ProcessWindows
{
    #region Constants

    /// <summary>표준 대화상자(MessageBox, 공용 파일 대화상자)의 창 클래스.</summary>
    internal const string DialogClass = "#32770";

    #endregion

    #region Fields

    private static readonly uint Self = (uint)Bcl.ProcessId;

    #endregion

    #region Public Methods

    /// <summary>살아 있고 이 프로세스가 소유한 창인지.</summary>
    public static bool IsOwn(IntPtr hwnd)
    {
        return hwnd != IntPtr.Zero
            && NativeMethods.IsWindow(hwnd)
            && NativeMethods.GetWindowThreadProcessId(hwnd, out var processId) != 0
            && processId == Self;
    }

    /// <summary>보이는 최상위 창. Z 순서의 앞(위)부터.</summary>
    public static List<IntPtr> Visible()
    {
        var found = new List<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (IsOwn(hwnd) && NativeMethods.IsWindowVisible(hwnd))
                found.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// 창 하나를 설명합니다. 기록할 값어치가 없는 창(모달이 아닌 메뉴·툴팁·드롭다운)이면 null.
    /// 팝업은 클릭 한 번에도 여럿 뜨고 사라지므로, 담으면 정작 찾는 대화상자가 기록에서 밀려난다.
    /// </summary>
    public static NativeWindow? Describe(IntPtr hwnd)
    {
        var className = NativeMethods.ClassOf(hwnd);
        var isDialog = className == DialogClass;
        var owner = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);
        var modal = isDialog || IsModal(hwnd, owner);
        var hasCaption = (NativeMethods.StyleOf(hwnd) & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION;

        if (!isDialog && !hasCaption && !modal)
            return null;

        return new NativeWindow(hwnd, NativeMethods.TitleOf(hwnd), className, isDialog ? "dialog" : "window", modal, owner);
    }

    /// <summary>
    /// 앱의 주 창. 지정한 핸들이 없을 때 창 조작이 겨눌 곳이다.
    /// </summary>
    public static IntPtr Main()
    {
        using var process = Process.GetCurrentProcess();
        var main = process.MainWindowHandle;
        return IsOwn(main) ? main : Visible().FirstOrDefault(hwnd => Describe(hwnd)?.Kind == "window");
    }

    /// <summary>핸들을 응답에 쓰는 모양으로. 예: 0x1A2B.</summary>
    public static string Format(IntPtr hwnd)
    {
        return "0x" + hwnd.ToInt64().ToString("X", CultureInfo.InvariantCulture);
    }

    /// <summary>"0x1A2B" 또는 10진수 문자열을 핸들로 읽습니다.</summary>
    public static bool TryParse(string text, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        var trimmed = text.Trim();
        var isHex = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        var parsed = isHex
            ? long.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            : long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        // 32비트 프로세스의 IntPtr 는 int 범위를 넘는 값을 담지 못한다. 그런 값은 이 프로세스의 핸들일 수 없다.
        if (!parsed || (IntPtr.Size == 4 && (value > int.MaxValue || value < int.MinValue)))
            return false;

        hwnd = new IntPtr(value);
        return true;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 뜰 때 다른 창을 막았는지. MessageBox 는 소유 창을, WPF 의 ShowDialog 는 같은 스레드의 다른 창을 모두 비활성화한 뒤에
    /// 자신을 보이므로, 보이는 순간에 이미 막힌 창이 있으면 모달이다.
    /// </summary>
    private static bool IsModal(IntPtr hwnd, IntPtr owner)
    {
        if (owner != IntPtr.Zero && !NativeMethods.IsWindowEnabled(owner))
            return true;

        var thread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        return Visible().Any(other => other != hwnd
                                      && NativeMethods.GetWindowThreadProcessId(other, out _) == thread
                                      && !NativeMethods.IsWindowEnabled(other));
    }

    #endregion
}

/// <summary>이 프로세스의 최상위 창 하나.</summary>
/// <param name="Hwnd">창 핸들.</param>
/// <param name="Title">제목.</param>
/// <param name="ClassName">창 클래스 이름.</param>
/// <param name="Kind">"dialog"(#32770) 또는 "window".</param>
/// <param name="Modal">뜰 때 다른 창을 막았는지.</param>
/// <param name="Owner">소유 창. 없으면 0.</param>
internal sealed record NativeWindow(IntPtr Hwnd, string Title, string ClassName, string Kind, bool Modal, IntPtr Owner);
