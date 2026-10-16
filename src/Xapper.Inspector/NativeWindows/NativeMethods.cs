using System.Runtime.InteropServices;
using System.Text;

namespace Xapper.Inspector.NativeWindows;

/// <summary>
/// 최상위 창과 대화상자를 다루는 Win32 함수.
/// 같은 프로세스라도 다른 스레드의 창에 보내는 메시지는 그 스레드가 처리할 때까지 막히므로, 메시지는 모두
/// <c>SendMessageTimeout</c> 이나 <c>PostMessage</c> 로 보내고 제목은 메시지 없이 읽는 <c>InternalGetWindowText</c> 로 읽는다.
/// </summary>
internal static class NativeMethods
{
    #region Constants

    internal const uint WM_SETTEXT = 0x000C;
    internal const uint WM_GETTEXT = 0x000D;
    internal const uint WM_COMMAND = 0x0111;
    internal const uint WM_QUIT = 0x0012;

    internal const uint SMTO_ABORTIFHUNG = 0x0002;

    internal const uint GW_OWNER = 4;
    internal const uint GA_ROOT = 2;

    internal const long WS_CAPTION = 0x00C00000;
    private const int GWL_STYLE = -16;

    internal const int SW_MAXIMIZE = 3;
    internal const int SW_MINIMIZE = 6;
    internal const int SW_RESTORE = 9;

    internal const uint EVENT_OBJECT_DESTROY = 0x8001;
    internal const uint EVENT_OBJECT_SHOW = 0x8002;
    internal const uint EVENT_OBJECT_HIDE = 0x8003;
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    internal const int OBJID_WINDOW = 0;

    #endregion

    #region Delegates

    /// <summary>EnumWindows·EnumChildWindows 콜백.</summary>
    internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    /// <summary>SetWinEventHook 콜백.</summary>
    internal delegate void WinEventProc(
        IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

    #endregion

    #region Structs

    /// <summary>GetMessage 가 채우는 메시지.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    #endregion

    #region P/Invoke

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowEnabled(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern int GetDlgCtrlID(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int InternalGetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hwnd, uint message, IntPtr wParam, string lParam, uint flags, uint timeoutMs, out IntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hwnd, uint message, IntPtr wParam, StringBuilder lParam, uint flags, uint timeoutMs, out IntPtr result);

    [DllImport("user32.dll")]
    internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindowAsync(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out MSG message, IntPtr hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    internal static extern IntPtr DispatchMessage(ref MSG message);

    [DllImport("user32.dll")]
    internal static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    #endregion

    #region Helpers

    /// <summary>창 클래스 이름. 대화상자는 "#32770".</summary>
    internal static string ClassOf(IntPtr hwnd)
    {
        var name = new StringBuilder(256);
        return GetClassName(hwnd, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    /// <summary>창 제목. 창에 메시지를 보내지 않으므로 멈춘 앱에서도 바로 돌아온다.</summary>
    internal static string TitleOf(IntPtr hwnd)
    {
        var text = new StringBuilder(512);
        return InternalGetWindowText(hwnd, text, text.Capacity) > 0 ? text.ToString() : "";
    }

    /// <summary>컨트롤의 글자(정적 텍스트·버튼). 창이 1초 안에 답하지 않으면 빈 문자열.</summary>
    internal static string ControlTextOf(IntPtr hwnd)
    {
        var text = new StringBuilder(4096);
        var sent = SendMessageTimeout(hwnd, WM_GETTEXT, (IntPtr)text.Capacity, text, SMTO_ABORTIFHUNG, 1000, out _);
        return sent == IntPtr.Zero ? "" : text.ToString();
    }

    /// <summary>창 스타일 비트.</summary>
    internal static long StyleOf(IntPtr hwnd)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, GWL_STYLE).ToInt64() : GetWindowLong32(hwnd, GWL_STYLE);
    }

    /// <summary>모든 자손 창(자식의 자식 포함).</summary>
    internal static List<IntPtr> DescendantsOf(IntPtr hwnd)
    {
        var found = new List<IntPtr>();
        EnumChildWindows(hwnd, (child, _) =>
        {
            found.Add(child);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    #endregion
}
