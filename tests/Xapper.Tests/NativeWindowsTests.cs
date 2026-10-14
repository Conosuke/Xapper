using System.IO;
using System.Windows;
using System.Windows.Threading;
using Xapper.Inspector.NativeWindows;

namespace Xapper.Tests;

/// <summary>
/// 테스트 프로세스 안에 실제 MessageBox·공용 파일 대화상자·모달 창을 띄우고, 다른 스레드에서 답하고 기록하는지 확인한다.
/// 대상 프로세스가 곧 테스트 프로세스이므로 Inspector 가 주입된 앱과 같은 조건이다. 대화상자는 프로세스 전역에서 찾으므로
/// 서로 겹치지 않게 한 클래스 안에서(순서대로) 돌린다. 이 PC 의 Windows 언어와 무관하게 통과해야 한다(버튼은 ID 로 누른다).
/// </summary>
public sealed class NativeWindowsTests
{
    #region Constants

    private const int DialogTimeoutMs = 10_000;

    #endregion

    #region Message Box

    /// <summary>버튼 ID 로 답하고, 제목·본문을 돌려주며, 모달 대화상자로 기록된다.</summary>
    [Fact]
    public async Task Message_box_is_answered_by_button_id_and_recorded_as_modal()
    {
        using var watcher = new WindowWatcher();
        Assert.True(watcher.Start());
        var since = DateTime.UtcNow;

        var shown = OnStaThread(() => MessageBox.Show("Body of the box", "XapperTest Answer", MessageBoxButton.YesNo));
        var answer = await DialogAnswerer.AnswerMessageBoxAsync("No", DialogTimeoutMs);

        Assert.True(answer.Success, answer.Text);
        Assert.Contains("XapperTest Answer", answer.Text);
        Assert.Contains("Body of the box", answer.Text);
        Assert.Equal(MessageBoxResult.No, await shown.WaitAsync(TimeSpan.FromSeconds(5)));

        var sighting = await EventuallyAsync(() => watcher.Since(since)
            .FirstOrDefault(entry => entry.Window.Title == "XapperTest Answer" && entry.HiddenAt is not null));
        Assert.True(sighting.Window.Modal);
        Assert.Equal("dialog", sighting.Window.Kind);
        Assert.True(sighting.HiddenAt >= sighting.ShownAt);
    }

    /// <summary>없는 버튼을 고르면 있는 버튼을 알려 주며 실패하고, 상자는 그대로 남는다.</summary>
    [Fact]
    public async Task Missing_button_fails_listing_the_buttons_the_box_has()
    {
        var shown = OnStaThread(() => MessageBox.Show("Only OK here", "XapperTest Missing", MessageBoxButton.OK));

        var wrong = await DialogAnswerer.AnswerMessageBoxAsync("yes", DialogTimeoutMs);
        var right = await DialogAnswerer.AnswerMessageBoxAsync("ok", DialogTimeoutMs);

        Assert.False(wrong.Success);
        Assert.Contains("has no yes button", wrong.Text);
        Assert.Contains("ok", wrong.Text);
        Assert.True(right.Success, right.Text);
        Assert.Equal(MessageBoxResult.OK, await shown.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>상자가 없으면 제한 시간 뒤 실패한다.</summary>
    [Fact]
    public async Task No_message_box_fails_after_the_timeout()
    {
        var answer = await DialogAnswerer.AnswerMessageBoxAsync("ok", 200);

        Assert.False(answer.Success);
        Assert.Contains("No message box appeared", answer.Text);
    }

    #endregion

    #region File Dialog

    /// <summary>열기 대화상자에 있는 파일 경로로 답하면 앱이 그 경로를 받는다.</summary>
    [Fact]
    public async Task Open_dialog_returns_the_given_path_to_the_app()
    {
        var file = TempFile(create: true);
        try
        {
            var shown = OnStaThread(() =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "XapperTest Open" };
                return dialog.ShowDialog() == true ? dialog.FileName : null;
            });

            var answer = await DialogAnswerer.AnswerFileDialogAsync(file, DialogTimeoutMs);

            Assert.True(answer.Success, answer.Text);
            Assert.Equal(file, await shown.WaitAsync(TimeSpan.FromSeconds(10)), ignoreCase: true);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// 저장 대화상자에 이미 있는 파일을 주면 덮어쓰기 확인이 뜬다. 그 사실을 실패로 알리고 확인 상자는 남겨 두므로,
    /// 이어서 메시지 상자로 답하면 저장 대화상자가 그 경로로 닫힌다.
    /// </summary>
    [Fact]
    public async Task Save_dialog_reports_the_overwrite_prompt_and_leaves_it_to_answer()
    {
        var file = TempFile(create: true);
        try
        {
            var shown = OnStaThread(() =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { Title = "XapperTest Save", OverwritePrompt = true };
                return dialog.ShowDialog() == true ? dialog.FileName : null;
            });

            var answer = await DialogAnswerer.AnswerFileDialogAsync(file, DialogTimeoutMs);
            var confirm = await DialogAnswerer.AnswerMessageBoxAsync("yes", DialogTimeoutMs);

            Assert.False(answer.Success);
            Assert.Contains("opened", answer.Text);
            Assert.True(confirm.Success, confirm.Text);
            Assert.Equal(file, await shown.WaitAsync(TimeSpan.FromSeconds(10)), ignoreCase: true);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>상대 경로는 대화상자를 찾기 전에 거절한다.</summary>
    [Fact]
    public async Task Relative_path_is_refused()
    {
        var answer = await DialogAnswerer.AnswerFileDialogAsync("orders.csv", 200);

        Assert.False(answer.Success);
        Assert.Contains("not an absolute path", answer.Text);
    }

    #endregion

    #region Window Watcher

    /// <summary>ShowDialog 로 뜬 WPF 창은 모달로, 그 소유 창은 모달이 아닌 것으로 기록되고, 모달 대기는 바로 끝난다.</summary>
    [Fact]
    public async Task Show_dialog_window_is_recorded_as_modal_and_wakes_the_wait()
    {
        using var watcher = new WindowWatcher();
        Assert.True(watcher.Start());
        var since = DateTime.UtcNow;

        var finished = OnStaThread(() =>
        {
            var owner = new Window { Title = "XapperTest Owner", Width = 300, Height = 200 };
            owner.Show();
            var dialog = new Window { Title = "XapperTest Dialog", Width = 200, Height = 100, Owner = owner };
            dialog.ShowDialog();
            owner.Close();
            return true;
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var modal = await watcher.WaitForModalAsync(since, cts.Token);

        Assert.NotNull(modal);
        Assert.Equal("XapperTest Dialog", modal.Window.Title);
        Assert.Equal("window", modal.Window.Kind);
        Assert.Contains(watcher.Since(since), entry => entry.Window.Title == "XapperTest Owner" && !entry.Window.Modal);

        NativeMethods.PostMessage(modal.Window.Hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
        Assert.True(await finished.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    #endregion

    #region Window Commands

    /// <summary>최대화·최소화·복원이 실제 창 상태를 바꾸고, 앞으로 가져오기는 성공하거나 거절을 정직하게 알린다.</summary>
    [Fact]
    public async Task Window_commands_change_the_real_window_state()
    {
        var (hwnd, closed) = await ShowPlainWindowAsync("XapperTest Commands");

        var maximize = await WindowCommands.RunAsync("Maximize", hwnd);
        Assert.True(maximize.Success, maximize.Text);
        Assert.True(NativeMethods.IsZoomed(hwnd));

        var minimize = await WindowCommands.RunAsync("minimize", hwnd);
        Assert.True(minimize.Success, minimize.Text);
        Assert.True(NativeMethods.IsIconic(hwnd));

        var restore = await WindowCommands.RunAsync("restore", hwnd);
        Assert.True(restore.Success, restore.Text);
        Assert.False(NativeMethods.IsIconic(hwnd) || NativeMethods.IsZoomed(hwnd));

        var front = await WindowCommands.RunAsync("front", hwnd);
        Assert.True(front.Success || front.Text.Contains("refused"), front.Text);

        NativeMethods.PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
        await closed.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>다른 프로세스의 창이나 없는 핸들에는 손대지 않는다.</summary>
    [Fact]
    public async Task Window_command_refuses_a_window_of_another_process()
    {
        var shell = NativeMethods.GetForegroundWindow();
        var target = ProcessWindows.IsOwn(shell) ? new IntPtr(0x7FFF0) : shell;

        var result = await WindowCommands.RunAsync("maximize", target);

        Assert.False(result.Success);
        Assert.Contains("is not a window of this app", result.Text);
    }

    /// <summary>핸들 문자열은 16진수와 10진수를 받고, 그 밖은 거절한다.</summary>
    [Theory]
    [InlineData("0x1A2B", 0x1A2B)]
    [InlineData("0X00ff", 0xFF)]
    [InlineData("6699", 6699)]
    public void Handle_text_is_parsed(string text, long expected)
    {
        Assert.True(ProcessWindows.TryParse(text, out var hwnd));
        Assert.Equal(expected, hwnd.ToInt64());
        Assert.Equal("0x" + expected.ToString("X"), ProcessWindows.Format(hwnd));
    }

    /// <summary>핸들이 아닌 글은 거절한다.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("0xZZ")]
    [InlineData("main")]
    public void Non_handle_text_is_refused(string text)
    {
        Assert.False(ProcessWindows.TryParse(text, out _));
    }

    #endregion

    #region Private Methods

    private const uint WmClose = 0x0010;

    /// <summary>STA 스레드에서 본문을 돌리고 결과를 기다릴 수 있게 돌려줍니다. 본문은 모달이라 끝날 때까지 스레드를 잡는다.</summary>
    private static Task<T> OnStaThread<T>(Func<T> body)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(body());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return done.Task;
    }

    /// <summary>디스패처가 도는 STA 스레드에 평범한 창을 띄우고, 그 핸들과 닫힐 때 끝나는 작업을 돌려줍니다.</summary>
    private static async Task<(IntPtr Hwnd, Task Closed)> ShowPlainWindowAsync(string title)
    {
        var handle = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = OnStaThread(() =>
        {
            var window = new Window { Title = title, Width = 300, Height = 200 };
            window.Closed += (_, _) => Dispatcher.CurrentDispatcher.InvokeShutdown();
            window.Show();
            handle.SetResult(new System.Windows.Interop.WindowInteropHelper(window).Handle);
            Dispatcher.Run();
            return true;
        });

        return (await handle.Task.WaitAsync(TimeSpan.FromSeconds(10)), closed);
    }

    /// <summary>값이 나올 때까지 잠깐씩 다시 봅니다. 감시기는 이벤트를 비동기로 받으므로 기록이 조금 늦을 수 있다.</summary>
    private static async Task<T> EventuallyAsync<T>(Func<T?> probe) where T : class
    {
        var deadline = Environment.TickCount64 + 5000;
        while (true)
        {
            if (probe() is { } value)
                return value;
            if (Environment.TickCount64 >= deadline)
                throw new Xunit.Sdk.XunitException("the expected record did not appear within 5 s");
            await Task.Delay(25);
        }
    }

    /// <summary>임시 폴더의 고유한 파일 경로. create 면 빈 파일을 만들어 둔다.</summary>
    private static string TempFile(bool create)
    {
        var path = Path.Combine(Path.GetTempPath(), $"xapper-dialog-{Guid.NewGuid():N}.txt");
        if (create)
            File.WriteAllText(path, "x");
        return path;
    }

    #endregion
}
