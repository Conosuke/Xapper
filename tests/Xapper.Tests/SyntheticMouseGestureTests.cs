using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>
/// 후킹 경로 제스처가 실제 WPF 창에서 의도한 라우팅 이벤트를 일으키는지 검증한다. 커서를 옮기지 않고
/// WM 메시지 + 입력 상태 스푸프만으로 휠·우클릭·Ctrl+클릭이 WPF 에 진짜 입력으로 받아들여지는지 측정한다.
/// 창은 화면 밖에 둔다. 같은 스레드에서 SendMessage 하면 WndProc 로 동기 전달되므로 펌프가 필요 없고, 프로덕션 경로를
/// 검증하는 테스트는 <see cref="FromWorker"/> 로 워커 스레드에서 보내며 창 스레드가 펌프한다.
/// </summary>
[Collection(DesktopWindowCollection.Name)]
public class SyntheticMouseGestureTests
{
    [Fact]
    public void TryWheel_ScrollsTheScrollViewerUnderThePoint()
    {
        WithWindow(
            () =>
            {
                var content = new StackPanel();
                for (var i = 0; i < 60; i++)
                    content.Children.Add(new TextBlock { Text = $"line {i}", Height = 20 });
                return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            },
            (window, hwnd, viewer) =>
            {
                Assert.Equal(0, viewer.VerticalOffset);

                var ok = SyntheticMouse.TryWheel(hwnd, CentreOf(viewer), -3) == GestureResult.Delivered;
                window.UpdateLayout();

                Assert.True(ok);
                Assert.True(viewer.VerticalOffset > 0, $"VerticalOffset={viewer.VerticalOffset}");
            });
    }

    [Fact]
    public void TryRightClick_RaisesContextMenuOpeningOnTheElement()
    {
        WithWindow(
            () => new TextBox { Text = "hello", ContextMenu = new ContextMenu { Items = { new MenuItem { Header = "Probe" } } } },
            (window, hwnd, box) =>
            {
                var opening = 0;
                var rightUp = 0;
                box.ContextMenuOpening += (_, e) => { opening++; e.Handled = true; };
                box.MouseRightButtonUp += (_, _) => rightUp++;

                var ok = SyntheticMouse.TryRightClick(hwnd, CentreOf(box)) == GestureResult.Delivered;

                Assert.True(ok);
                Assert.Equal(1, rightUp);
                Assert.Equal(1, opening);
            });
    }

    [Fact]
    public void TryClick_WithControl_HandlerSeesTheControlModifier()
    {
        WithWindow(
            () => new Border { Background = System.Windows.Media.Brushes.LightGray, Width = 120, Height = 60 },
            (window, hwnd, border) =>
            {
                var seen = ModifierKeys.None;
                var downs = 0;
                border.PreviewMouseLeftButtonDown += (_, _) => { downs++; seen = Keyboard.Modifiers; };
                var point = CentreOf(border);

                // 프로덕션처럼 다른 스레드(IPC 스레드)에서 보낸다 — 창 스레드는 그동안 메시지를 펌프한다.
                var ok = FromWorker(() => SyntheticMouse.TryClick(hwnd, point, ModifierKeys.Control) == GestureResult.Delivered);

                Assert.True(ok);
                Assert.Equal(1, downs);
                Assert.Equal(ModifierKeys.Control, seen);
                Assert.Equal(ModifierKeys.None, Keyboard.Modifiers); // 제스처가 끝나면 스푸프가 꺼진다.
            });
    }

    [Fact]
    public void TryClick_SpoofsModifiersOnlyWhileHandlingItsOwnMessages()
    {
        WithWindow(
            () => new Border { Background = System.Windows.Media.Brushes.LightGray, Width = 120, Height = 60 },
            (window, hwnd, border) =>
            {
                // 제스처 중에도 우리 메시지 밖에서 읽는 수식키는 진짜여야 한다 — 그래야 같은 시간에 사람이 누른 진짜
                // 키가 엉뚱한 조합으로 처리되지 않는다(측정: 느린 핸들러 동안 프로세스 전체가 가짜 상태를 봤던 결함).
                // 버튼은 다르다. 대상 UI 스레드에서는 제스처 동안 계속 눌림으로 답한다 — WPF 의 재동기화가 메시지 밖에서
                // 버튼을 다시 읽기 때문이다(TryDrag_KeepsTheButtonPressed_ForEveryMoveAndEveryLateRead).
                var insideHandler = ModifierKeys.None;
                var outsideSamples = new List<ModifierKeys>();
                border.PreviewMouseLeftButtonDown += (_, _) =>
                {
                    insideHandler = Keyboard.Modifiers;
                    Thread.Sleep(20); // 느린 핸들러 흉내(워치독 한도보다 훨씬 짧게): 이 동안 스푸프가 켜져 있다.
                };
                var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input)
                {
                    Interval = TimeSpan.FromMilliseconds(5)
                };
                timer.Tick += (_, _) => outsideSamples.Add(Keyboard.Modifiers); // 우리 메시지 사이(펌프 중)에 읽는다.
                timer.Start();
                var point = CentreOf(border);

                var ok = FromWorker(() => SyntheticMouse.TryClick(hwnd, point, ModifierKeys.Control) == GestureResult.Delivered);
                timer.Stop();

                Assert.True(ok);
                Assert.Equal(ModifierKeys.Control, insideHandler);
                Assert.NotEmpty(outsideSamples);
                Assert.All(outsideSamples, m => Assert.Equal(ModifierKeys.None, m));
            });
    }

    [Fact]
    public void TryClick_WhenTheHandlerStalls_EndsTheSpoofEarlyAndReportsStalled()
    {
        WithWindow(
            () => new Border { Background = System.Windows.Media.Brushes.LightGray, Width = 120, Height = 60 },
            (window, hwnd, border) =>
            {
                // 핸들러가 제한 시간(250ms)을 넘기면 보낸 쪽 워치독이 스푸프를 끈다: 핸들러 시작 시엔 Ctrl 이 보이고,
                // 350ms 뒤(같은 핸들러 안, 아직 우리 메시지 처리 중)엔 진짜 상태(None)가 보여야 한다.
                var atStart = ModifierKeys.None;
                var later = ModifierKeys.Control;
                var ups = 0;
                border.PreviewMouseLeftButtonDown += (_, _) =>
                {
                    atStart = Keyboard.Modifiers;
                    Thread.Sleep(SyntheticMouse.StallTimeoutMs + 100);
                    later = Keyboard.Modifiers;
                };
                border.PreviewMouseLeftButtonUp += (_, _) => ups++;
                var point = CentreOf(border);

                var dispatcher = window.Dispatcher;
                var result = FromWorker(() => SyntheticMouse.TryClick(hwnd, point, ModifierKeys.Control));
                // 느린 핸들러는 결국 끝난다: 응답을 "아직 처리 중" 으로 돌리지 않도록 정체 뒤 기다리면 곧 true 여야 한다.
                var finished = FromWorker(() => SyntheticMouse.WaitUntilHandledAsync(dispatcher, TimeSpan.FromSeconds(2)).Result);

                Assert.Equal(GestureResult.Stalled, result);
                Assert.Equal(ModifierKeys.Control, atStart);
                Assert.Equal(ModifierKeys.None, later);
                Assert.Equal(ModifierKeys.None, Keyboard.Modifiers);
                Assert.True(finished);
                Assert.Equal(1, ups); // 스푸프가 꺼져도 뗌은 자기 좌표로 라우팅되므로 클릭은 완성된다.
            });
    }

    [Fact]
    public void WaitUntilHandled_WhileTheHandlerRunsANestedLoop_ReportsStillHandlingUntilItReturns()
    {
        WithWindow(
            () => new Border { Background = System.Windows.Media.Brushes.LightGray, Width = 120, Height = 60 },
            (window, hwnd, border) =>
            {
                // 모달 대화상자 흉내: 핸들러가 중첩 메시지 루프를 돌린다. 그 안에서도 디스패처 작업은 돌지만 우리 메시지는
                // 아직 처리 중이므로 "끝났다" 로 보면 안 되고, 루프가 끝난 뒤에야 끝난 것으로 봐야 한다.
                var modal = new System.Windows.Threading.DispatcherFrame();
                border.PreviewMouseLeftButtonDown += (_, _) => System.Windows.Threading.Dispatcher.PushFrame(modal);
                var point = CentreOf(border);
                var dispatcher = window.Dispatcher;

                // 중첩 루프가 도는 동안 이 스레드는 거기 붙잡혀 있으므로, 루프를 끝내는 것도 워커가 디스패처에 걸어 준다.
                var (result, whileModal, afterModal) = FromWorker(() =>
                {
                    var click = SyntheticMouse.TryClick(hwnd, point, ModifierKeys.None);
                    var during = SyntheticMouse.WaitUntilHandledAsync(dispatcher, TimeSpan.FromMilliseconds(400)).Result;
                    dispatcher.BeginInvoke(() => modal.Continue = false);
                    var after = SyntheticMouse.WaitUntilHandledAsync(dispatcher, TimeSpan.FromSeconds(2)).Result;
                    return (click, during, after);
                });

                Assert.Equal(GestureResult.Stalled, result);
                Assert.False(whileModal);
                Assert.True(afterModal);
            });
    }

    [Fact]
    public void TryClick_WhenAWindowOfThisAppCoversThePoint_FallsBackInsteadOfClickingThrough()
    {
        WithWindow(
            () => new Border { Background = System.Windows.Media.Brushes.LightGray, Width = 120, Height = 60 },
            (window, hwnd, border) =>
            {
                var downs = 0;
                border.PreviewMouseLeftButtonDown += (_, _) => downs++;

                // 같은 앱의 다른 창(팝업·대화상자에 해당)이 그 지점을 덮는다. 실제 사용자는 그 창을 누르게 되므로
                // 스푸프로 뚫지 않고 false 를 돌려 호출자가 실제 입력으로 폴백하게 해야 한다.
                var cover = new Window
                {
                    Width = window.Width, Height = window.Height, Left = window.Left, Top = window.Top,
                    Topmost = true, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None
                };
                cover.Show();
                try
                {
                    Assert.True(InputSpoof.IsCoveredByOwnWindow(hwnd, CentreOf(border)));
                    Assert.Equal(GestureResult.Unavailable, SyntheticMouse.TryClick(hwnd, CentreOf(border)));
                    Assert.Equal(0, downs);
                }
                finally
                {
                    cover.Close();
                }

                // 덮개가 사라지면 다시 후킹 경로로 전달된다.
                Assert.False(InputSpoof.IsCoveredByOwnWindow(hwnd, CentreOf(border)));
                Assert.Equal(GestureResult.Delivered, SyntheticMouse.TryClick(hwnd, CentreOf(border)));
                Assert.Equal(1, downs);
            });
    }

    [Fact]
    public void WindowFromPoint_WhileSpoofing_AnswersTheTargetForAPointOverAnotherProcess()
    {
        WithWindow(
            () => new Border { Width = 120, Height = 60 },
            (window, hwnd, _) =>
            {
                // 화면 원점 부근은 다른 프로세스의 창(바탕화면·작업표시줄 등)이다. WPF 는 마우스를 활성화할 때
                // WindowFromPoint(커서) 가 자기 창인지 확인하므로, 스푸프 중에는 대상 창을 답해야 한다.
                var far = new NativePoint { X = 5, Y = 5 };
                var real = WindowFromPoint(far);
                GetWindowThreadProcessId(real, out var pid);
                if (real == IntPtr.Zero || pid == (uint)Environment.ProcessId)
                    return; // 이 화면 배치에서는 검증 조건이 안 만들어진다.

                InputSpoof.BeginMouse(hwnd, new Point(5, 5), ModifierKeys.None);
                try
                {
                    Assert.Equal(hwnd, WindowFromPoint(far));
                }
                finally
                {
                    InputSpoof.EndMouse();
                }

                Assert.Equal(real, WindowFromPoint(far));
            });
    }

    #region Helpers

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    /// <summary>
    /// 제스처를 워커 스레드에서 실행하고 창 스레드는 그동안 메시지를 펌프합니다. 프로덕션에서 IPC 스레드가 보내는
    /// 것과 같은 경로라, 같은 스레드에서 SendMessage 할 때는 false 인 InSendMessage 스코프가 실제로 검증된다.
    /// </summary>
    [Fact]
    public void TryDrag_KeepsTheButtonPressed_ForEveryMoveAndEveryLateRead()
    {
        // 요소가 움직이면 WPF 는 레이아웃 뒤에 스스로 마우스 상태를 다시 읽어 MouseMove 를 한 번 더 낸다. 버튼 스푸프가
        // 우리 메시지 안으로만 좁혀져 있으면 그 이동은 안 눌린 것으로 보여, 드래그가 걸음마다 누름과 뗌을 깜빡였다
        // (실측: 여덟 걸음에 MouseMove 18번 중 9번이 안 눌림). 이벤트마다 버튼을 보는 핸들러는 버티지만, 상태 기계로
        // 제스처를 판정하는 캔버스(yFiles)는 그 틈을 드래그의 끝으로 보고 아무것도 옮기지 않았다.
        WithWindow(
            () => new Canvas { Width = 280, Height = 180, Background = System.Windows.Media.Brushes.White },
            (window, hwnd, canvas) =>
            {
                var node = new Border { Width = 40, Height = 30, Background = System.Windows.Media.Brushes.SteelBlue };
                Canvas.SetLeft(node, 20);
                Canvas.SetTop(node, 60);
                canvas.Children.Add(node);
                window.UpdateLayout();

                Point? grab = null;
                var releasedWhileHeld = 0;
                var lateReleased = 0;

                node.MouseLeftButtonDown += (_, e) => { grab = e.GetPosition(canvas); node.CaptureMouse(); };
                node.MouseLeftButtonUp += (_, _) => { grab = null; node.ReleaseMouseCapture(); };
                node.MouseMove += (_, e) =>
                {
                    if (grab is not { } g)
                        return;

                    if (e.LeftButton != MouseButtonState.Pressed)
                        releasedWhileHeld++;

                    // 디스패처 작업으로 미뤄 버튼을 다시 읽는 캔버스를 흉내 낸다.
                    node.Dispatcher.BeginInvoke(() =>
                    {
                        if (grab is not null && Mouse.LeftButton != MouseButtonState.Pressed)
                            lateReleased++;
                    });

                    // 끄는 동안 자기를 옮긴다. 이 레이아웃 변화가 WPF 의 재동기화를 부른다.
                    Canvas.SetLeft(node, 20 + (e.GetPosition(canvas).X - g.X));
                };

                var start = node.PointToScreen(new Point(20, 15));
                var end = new Point(start.X + 120, start.Y);

                var result = FromWorker(() => SyntheticMouse.TryDrag(hwnd, start, end));
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                Assert.Equal(GestureResult.Delivered, result);
                Assert.Equal(0, releasedWhileHeld);
                Assert.Equal(0, lateReleased);
                Assert.InRange(Canvas.GetLeft(node), 130, 150);
            });
    }

    [Fact]
    public void TryDrag_SaysWhetherAnythingTookHoldOfTheMouse()
    {
        // 메시지가 전달된 것과 앱이 그것을 끌기로 받아들인 것은 다르다. 끌기를 받아들이는 컨트롤은 누르는 순간 마우스를
        // 붙잡으므로, 아무도 붙잡지 않았다면 앱은 지나가는 마우스로 보았을 가능성이 크다(실측: 노드가 그대로인 캔버스에서
        // 응답만 성공이었다).
        WithWindow(
            () => new Canvas { Width = 280, Height = 180, Background = System.Windows.Media.Brushes.White },
            (window, hwnd, canvas) =>
            {
                var plain = new Border { Width = 60, Height = 40, Background = System.Windows.Media.Brushes.LightGray };
                var draggable = new Border { Width = 60, Height = 40, Background = System.Windows.Media.Brushes.SteelBlue };
                Canvas.SetLeft(plain, 10);
                Canvas.SetTop(plain, 10);
                Canvas.SetLeft(draggable, 10);
                Canvas.SetTop(draggable, 100);
                canvas.Children.Add(plain);
                canvas.Children.Add(draggable);
                draggable.MouseLeftButtonDown += (_, _) => draggable.CaptureMouse();
                draggable.MouseLeftButtonUp += (_, _) => draggable.ReleaseMouseCapture();
                window.UpdateLayout();

                var overPlain = plain.PointToScreen(new Point(30, 20));
                var overDraggable = draggable.PointToScreen(new Point(30, 20));

                var plainCaptured = true;
                var draggableCaptured = false;
                FromWorker(() => SyntheticMouse.TryDrag(
                    hwnd, overPlain, new Point(overPlain.X + 80, overPlain.Y), ModifierKeys.None, out plainCaptured));
                FromWorker(() => SyntheticMouse.TryDrag(
                    hwnd, overDraggable, new Point(overDraggable.X + 80, overDraggable.Y), ModifierKeys.None,
                    out draggableCaptured));

                Assert.False(plainCaptured);
                Assert.True(draggableCaptured);
            });
    }

    private static T FromWorker<T>(Func<T> gesture)
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        var frame = new System.Windows.Threading.DispatcherFrame();
        var task = Task.Run(gesture);
        task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        return task.GetAwaiter().GetResult();
    }

    private static Point CentreOf(FrameworkElement element)
        => element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));

    /// <summary>
    /// 화면 밖에 창을 세워 요소 하나를 담고, 후크가 있을 때만 본문을 실행합니다. 후크를 설치할 수 없는 환경에서는
    /// 검증 대상이 없으므로 조용히 통과한다.
    /// </summary>
    private static void WithWindow<T>(Func<T> createContent, Action<Window, IntPtr, T> body) where T : FrameworkElement
    {
        StaThread.Run(() =>
        {
            if (!InputSpoof.EnsureInstalled())
                return;

            var content = createContent();
            var window = new Window
            {
                Content = content, Width = 300, Height = 200,
                Left = -10000, Top = -10000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            try
            {
                window.Show();
                window.UpdateLayout();

                if (PresentationSource.FromVisual(window) is not HwndSource source)
                    throw new InvalidOperationException("창에 HwndSource 가 없다.");

                body(window, source.Handle, content);
            }
            finally
            {
                window.Close();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }

    #endregion
}
