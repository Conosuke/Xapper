using System.Runtime.InteropServices;

namespace Xapper.Inspector.Actions;

/// <summary>
/// 합성 드래그가 앱의 OLE 끌어놓기 루프를 열어 버렸을 때, 그 루프를 취소로 끝내는 장치.
///
/// 어떤 앱은 버튼을 누른 채 마우스가 조금만 움직여도 <c>DragDrop.DoDragDrop</c> 을 부른다. 그것은 놓거나 취소할 때까지
/// 돌아오지 않는 모달 루프이고, 루프는 <b>입력 큐에 들어온</b> 메시지를 보고서야 버튼 상태를 다시 확인한다.
/// 그런데 합성 드래그는 메시지를 앱 창으로 <b>보낸다</b>. 보낸 메시지는 큐를 거치지 않으므로 루프는 놓음을 끝내 보지
/// 못하고, 사람이 실제로 키나 마우스를 건드릴 때까지 앱이 그 안에 갇힌다(실측: 노드를 끄는 드래그 뒤로 앱이 멎었고,
/// 나중에 들어온 실제 키 입력이 그제야 루프를 끝냈다).
///
/// 그래서 Esc 를 그 스레드의 입력 큐에 넣는다. 놓기가 아니라 취소인 이유가 있다. 루프가 갇힌 시점에는 정체 감시가
/// 이미 스푸프를 껐으므로, 놓게 하면 사람의 실제 커서가 있는 자리에 떨어진다. 엉뚱한 대상에 붙는 것보다 아무 일도
/// 없었던 편이 낫다.
///
/// 예외를 던지지 않는다. 대상 앱 안에서 도는 코드이므로(결함 2 원칙) 실패하면 false 로 알린다.
/// </summary>
internal static class DragLoopBreaker
{
    #region Constants

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const int VK_ESCAPE = 0x1B;

    #endregion

    #region Public Methods

    /// <summary>창을 만든 UI 스레드의 운영체제 스레드 ID. 알 수 없으면 0.</summary>
    /// <param name="hwnd">살펴볼 창.</param>
    public static uint ThreadOf(IntPtr hwnd)
    {
        try
        {
            return hwnd == IntPtr.Zero ? 0 : GetWindowThreadProcessId(hwnd, IntPtr.Zero);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// 그 스레드가 지금 다른 창에 마우스를 붙잡혀 있는지 알아봅니다.
    /// 드래그 핸들러가 모달 대화상자를 연 경우와 끌어놓기 루프를 연 경우를 가르는 데 쓴다. 대화상자는 마우스를 붙잡지
    /// 않지만, 끌어놓기 루프는 드래그 내내 마우스를 붙잡고 있다.
    /// </summary>
    /// <param name="threadId">살펴볼 UI 스레드.</param>
    /// <returns>붙잡은 창의 핸들. 아무도 붙잡지 않았으면 0.</returns>
    public static IntPtr CaptureHolderOf(uint threadId)
    {
        try
        {
            var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            return GetGUIThreadInfo(threadId, ref info) ? info.hwndCapture : IntPtr.Zero;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// 그 스레드의 입력 큐에 Esc 를 넣어 끌어놓기를 취소합니다.
    /// 마우스를 붙잡은 창이 있으면 그 창으로, 없으면 대신 받을 창으로 넣는다. 키보드 메시지는 창이 아니라 스레드의 큐로
    /// 들어가므로, 루프가 어느 창을 보고 있든 같은 스레드라면 집어 든다.
    /// </summary>
    /// <param name="threadId">루프가 도는 UI 스레드.</param>
    /// <param name="fallbackWindow">마우스를 붙잡은 창이 없을 때 대신 받을 창.</param>
    /// <returns>Esc 를 큐에 넣었으면 true.</returns>
    public static bool TryCancel(uint threadId, IntPtr fallbackWindow)
    {
        try
        {
            var target = CaptureHolderOf(threadId);
            if (target == IntPtr.Zero)
                target = fallbackWindow;

            if (target == IntPtr.Zero)
                return false;

            // 스캔 코드 1(Esc)을 lParam 에 담는다. 반복 횟수 1, 키를 뗄 때는 이전 상태·전환 비트를 켠다.
            var down = (IntPtr)(1 | (0x01 << 16));
            var up = (IntPtr)(1 | (0x01 << 16) | (1 << 30) | unchecked((int)0x80000000));

            return PostMessageW(target, WM_KEYDOWN, (IntPtr)VK_ESCAPE, down)
                && PostMessageW(target, WM_KEYUP, (IntPtr)VK_ESCAPE, up);
        }
        catch (Exception)
        {
            return false;
        }
    }

    #endregion

    #region Interop

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    #endregion
}
