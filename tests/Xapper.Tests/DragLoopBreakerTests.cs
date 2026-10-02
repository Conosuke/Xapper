using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>
/// 합성 드래그가 앱의 OLE 끌어놓기 루프를 열었을 때 무슨 일이 일어나는지 재현하고, 그 루프를 취소로 끝낼 수 있는지 잰다.
///
/// 재현 대상은 실제 앱에서 본 그대로다. 버튼을 누른 채 마우스가 움직이면 <c>DragDrop.DoDragDrop</c> 을 부르는 요소를
/// 두고, 그 위에서 합성 드래그를 보낸다. 루프가 끝나지 않으면 시험 스레드가 영영 돌아오지 않으므로, 마지막 방편으로
/// 실제 Esc 키를 한 번 눌러 루프를 풀어 주고 실패로 보고한다.
/// </summary>
[Collection(DesktopWindowCollection.Name)]
public class DragLoopBreakerTests
{
    [Fact]
    public void ASyntheticDragThatOpensDragDrop_LeavesTheLoopRunning_UntilItIsCancelled()
    {
        StaThread.Run(() =>
        {
            if (!InputSpoof.EnsureInstalled())
                return;

            var loopEnded = false;
            var effect = DragDropEffects.Copy;
            var dragStarted = false;

            var source = new Border
            {
                Width = 200,
                Height = 120,
                Background = System.Windows.Media.Brushes.SteelBlue
            };
            source.MouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || dragStarted)
                    return;

                dragStarted = true;
                effect = DragDrop.DoDragDrop(source, "payload", DragDropEffects.Copy | DragDropEffects.Move);
                loopEnded = true;
            };

            var window = new Window
            {
                Content = source,
                Width = 300,
                Height = 200,
                Left = 200,
                Top = 200,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                if (PresentationSource.FromVisual(window) is not HwndSource hwndSource)
                    throw new InvalidOperationException("창에 HwndSource 가 없다.");

                var hwnd = hwndSource.Handle;
                var uiThread = GetCurrentThreadId();
                var start = source.PointToScreen(new Point(40, 60));
                var end = source.PointToScreen(new Point(160, 60));

                var report = FromWorker(() =>
                {
                    var gesture = SyntheticMouse.TryDrag(hwnd, start, end);

                    Thread.Sleep(600);
                    var stuckAfterDrag = dragStarted && !loopEnded;
                    var captured = DragLoopBreaker.CaptureHolderOf(uiThread) != IntPtr.Zero;

                    var posted = DragLoopBreaker.TryCancel(uiThread, hwnd);

                    var waited = 0;
                    while (!loopEnded && waited < 3000)
                    {
                        Thread.Sleep(50);
                        waited += 50;
                    }

                    var endedByCancel = loopEnded;

                    // 취소가 듣지 않았다면 이 스레드는 영영 돌아오지 않는다. 실제 Esc 를 한 번 눌러 풀어 주고 실패로 남긴다.
                    if (!loopEnded)
                        PressRealEscape();

                    return (gesture, stuckAfterDrag, captured, posted, endedByCancel);
                });

                Assert.True(dragStarted, "드래그가 끌어놓기를 열지 않았다. 재현이 성립하지 않는다.");
                Assert.Equal(GestureResult.Stalled, report.gesture);
                Assert.True(report.stuckAfterDrag, "합성 드래그의 놓음만으로 루프가 끝났다. 문제가 재현되지 않았다.");
                Assert.True(report.captured, "루프가 도는 동안 마우스를 붙잡은 창이 없었다.");
                Assert.True(report.posted, "Esc 를 큐에 넣지 못했다.");
                Assert.True(report.endedByCancel, "Esc 를 넣어도 끌어놓기 루프가 끝나지 않았다.");
                Assert.Equal(DragDropEffects.None, effect);
            }
            finally
            {
                window.Close();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }

    [Fact]
    public void CaptureHolderOf_IsZero_WhenNothingHoldsTheMouse()
    {
        // 드래그 핸들러가 메시지 상자를 연 경우는 마우스를 붙잡지 않는다. 그런 정체에 Esc 를 넣으면 사람이 답해야 할
        // 대화상자를 Xapper 가 대신 닫아 버리므로, 붙잡은 창이 없을 때는 끌어놓기 루프로 보지 않아야 한다.
        StaThread.Run(() =>
        {
            Assert.Equal(IntPtr.Zero, DragLoopBreaker.CaptureHolderOf(GetCurrentThreadId()));
        });
    }

    #region Helpers

    /// <summary>
    /// 다른 스레드에서 제스처를 보내고, 그동안 창 스레드는 메시지를 펌프합니다. 끌어놓기 루프가 그 펌프 안에 중첩되므로
    /// 작업이 끝나도 루프가 풀릴 때까지는 이 함수가 돌아오지 않는다.
    /// </summary>
    private static T FromWorker<T>(Func<T> gesture)
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        var frame = new System.Windows.Threading.DispatcherFrame();
        var task = Task.Run(gesture);
        task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        return task.GetAwaiter().GetResult();
    }

    /// <summary>시험이 멎지 않도록 실제 Esc 를 한 번 누릅니다. 취소가 실패했을 때만 쓰는 마지막 방편이다.</summary>
    private static void PressRealEscape()
    {
        var inputs = new INPUT[2];
        inputs[0].type = 1;
        inputs[0].ki.wVk = 0x1B;
        inputs[1].type = 1;
        inputs[1].ki.wVk = 0x1B;
        inputs[1].ki.dwFlags = 0x0002;
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // 64비트 INPUT 의 크기는 가장 큰 공용체 멤버(MOUSEINPUT)에 맞춰 40 바이트다. 크기가 다르면 SendInput 이 거절한다.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    #endregion
}
