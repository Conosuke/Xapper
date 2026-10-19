namespace Xapper.Inspector.NativeWindows;

/// <summary>
/// 이 프로세스가 띄운 표준 대화상자(MessageBox, 공용 열기·저장 대화상자)에 답한다.
///
/// 버튼은 표시 글자가 아니라 표준 ID 로 찾는다. 글자는 Windows 언어마다 다르고("OK" 대 "확인") 바로 가기 표시("예(&amp;Y)")가
/// 붙기 때문이다. 누르는 것은 대화상자에 WM_COMMAND 를 올려(Post) 하는데, BM_CLICK 은 대화상자가 활성 창이 아니면
/// 무시될 수 있고, SendMessage 는 대화상자가 후속 확인 창을 띄우면 그 창이 닫힐 때까지 돌아오지 않기 때문이다.
/// 누른 뒤에는 대화상자가 정말 닫혔는지 확인한다. 닫히지 않았는데 성공이라고 하면 다음 조작이 엉뚱한 곳에서 실패한다.
/// </summary>
internal static class DialogAnswerer
{
    #region Constants

    /// <summary>파일 이름 칸의 콤보 상자(cmb13). 새 방식 대화상자도 호환을 위해 이 ID 를 유지한다.</summary>
    private const int FileNameComboId = 0x047C;

    /// <summary>예전 방식 대화상자의 파일 이름 편집 칸(edt1).</summary>
    private const int FileNameEditId = 0x0480;

    /// <summary>새 방식 저장 대화상자의 파일 이름 편집 칸. 이 대화상자에는 cmb13 이 없다(실측).</summary>
    private const int SaveFileNameEditId = 0x03E9;

    /// <summary>새 방식(Vista 이후) 열기·저장 대화상자가 탐색 영역을 담는 창 클래스.</summary>
    private const string ShellViewHostClass = "DUIViewWndClassName";

    /// <summary>MessageBox 본문을 담는 정적 컨트롤.</summary>
    private const int MessageTextId = 0xFFFF;

    private const int IdOk = 1;

    /// <summary>대화상자를 다시 찾는 간격.</summary>
    private const int PollMs = 50;

    /// <summary>버튼을 누른 뒤 닫히기를 기다리는 최소 시간. 남은 제한 시간이 이보다 짧아도 이만큼은 기다린다.</summary>
    private const int CloseGraceMs = 2000;

    /// <summary>MessageBox 버튼 이름과 표준 ID.</summary>
    private static readonly (string Name, int Id)[] Buttons =
    [
        ("ok", 1), ("cancel", 2), ("abort", 3), ("retry", 4), ("ignore", 5),
        ("yes", 6), ("no", 7), ("close", 8), ("tryagain", 10), ("continue", 11)
    ];

    #endregion

    #region Public Methods

    /// <summary>앱의 메시지 상자를 기다려 지정한 버튼으로 답합니다.</summary>
    /// <param name="button">버튼 이름(ok, cancel, yes, no …). 대소문자 무관.</param>
    /// <param name="timeoutMs">상자가 나타나기를 기다릴 시간.</param>
    public static async Task<NativeResult> AnswerMessageBoxAsync(string button, int timeoutMs)
    {
        var name = button.Trim().ToLowerInvariant();
        var id = Buttons.FirstOrDefault(entry => entry.Name == name).Id;
        if (id == 0)
            return NativeResult.Fail(
                $"Unknown button \"{button}\". Use one of: {string.Join(", ", Buttons.Select(entry => entry.Name))}.");

        var box = await WaitForDialogAsync(IsMessageBox, timeoutMs);
        if (box == IntPtr.Zero)
            return NativeResult.Fail($"No message box appeared within {timeoutMs} ms.");

        var title = NativeMethods.TitleOf(box);
        var children = NativeMethods.DescendantsOf(box);
        var bodyControl = children.FirstOrDefault(child => NativeMethods.GetDlgCtrlID(child) == MessageTextId);
        var body = bodyControl == IntPtr.Zero ? "" : NativeMethods.ControlTextOf(bodyControl);

        var buttons = children.Where(child => NativeMethods.ClassOf(child) == "Button").ToList();
        var available = buttons
            .Select(child => (Hwnd: child, Id: NativeMethods.GetDlgCtrlID(child)))
            .Select(entry => (entry.Hwnd, entry.Id, Name: ButtonName(entry.Id, buttons.Count)))
            .ToList();

        var target = available.FirstOrDefault(entry => entry.Name == name);
        if (target.Hwnd == IntPtr.Zero)
        {
            var names = available.Select(entry => entry.Name).Where(known => known is not null);
            return NativeResult.Fail($"\"{title}\" has no {name} button. It has: {string.Join(", ", names)}.");
        }

        NativeMethods.PostMessage(box, NativeMethods.WM_COMMAND, (IntPtr)target.Id, target.Hwnd);
        if (await WaitClosedAsync(box, followUpOf: IntPtr.Zero) is not { Closed: true })
            return NativeResult.Fail($"\"{title}\" is still open after pressing {name}.");

        return NativeResult.Ok($"Answered \"{title}\" with {name}. Message: \"{body}\"");
    }

    /// <summary>앱의 열기·저장 대화상자를 기다려 경로를 넣고 확인을 누릅니다.</summary>
    /// <param name="path">넣을 절대 경로.</param>
    /// <param name="timeoutMs">대화상자가 나타나기를 기다릴 시간.</param>
    public static async Task<NativeResult> AnswerFileDialogAsync(string path, int timeoutMs)
    {
        if (!System.IO.Path.IsPathRooted(path))
            return NativeResult.Fail($"\"{path}\" is not an absolute path. Pass the full path, e.g. C:\\data\\file.txt.");

        var dialog = await WaitForDialogAsync(IsFileDialog, timeoutMs);
        if (dialog == IntPtr.Zero)
            return NativeResult.Fail($"No open or save dialog appeared within {timeoutMs} ms.");

        var title = NativeMethods.TitleOf(dialog);
        var edit = FileNameEditOf(dialog);
        if (edit == IntPtr.Zero)
            return NativeResult.Fail($"Could not find the file name box in \"{title}\".");

        var accepted = NativeMethods.SendMessageTimeout(
            edit, NativeMethods.WM_SETTEXT, IntPtr.Zero, path, NativeMethods.SMTO_ABORTIFHUNG, 2000, out _);
        if (accepted == IntPtr.Zero)
            return NativeResult.Fail($"\"{title}\" did not accept the path within 2000 ms; the app is not responding.");

        NativeMethods.PostMessage(dialog, NativeMethods.WM_COMMAND, (IntPtr)IdOk, IntPtr.Zero);

        var outcome = await WaitClosedAsync(dialog, followUpOf: dialog);
        if (outcome.FollowUp != IntPtr.Zero)
        {
            var followTitle = NativeMethods.TitleOf(outcome.FollowUp);
            var followText = string.Join(" ", NativeMethods.DescendantsOf(outcome.FollowUp)
                .Where(child => NativeMethods.GetDlgCtrlID(child) == MessageTextId)
                .Select(NativeMethods.ControlTextOf));
            return NativeResult.Fail(
                $"\"{title}\" opened \"{followTitle}\": {followText} It is still open; answer it with xapper_message_box.");
        }

        if (!outcome.Closed)
            return NativeResult.Fail($"\"{title}\" is still open after confirming {path}.");

        return NativeResult.Ok($"Answered \"{title}\" with {path}");
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 버튼 ID 의 이름. 확인 버튼 하나뿐인 MessageBox 는 그 버튼에 IDOK 가 아니라 IDCANCEL(2) 을 주고, 누르면 앱은 OK 를 받는다(실측).
    /// Esc 로도 닫히게 하려는 Windows 의 동작이다.
    /// </summary>
    private static string? ButtonName(int id, int buttonCount)
    {
        if (buttonCount == 1 && id == 2)
            return "ok";

        return Buttons.FirstOrDefault(known => known.Id == id).Name;
    }

    /// <summary>
    /// 이 프로세스의 활성 대화상자를 기다립니다. 모달이 겹치면 맨 위의 것만 활성이므로 활성 여부로 고른다.
    /// </summary>
    private static async Task<IntPtr> WaitForDialogAsync(Func<IntPtr, bool> isWanted, int timeoutMs)
    {
        var deadline = Bcl.TickCount64 + timeoutMs;
        while (true)
        {
            var found = ProcessWindows.Visible().FirstOrDefault(hwnd =>
                NativeMethods.ClassOf(hwnd) == ProcessWindows.DialogClass
                && NativeMethods.IsWindowEnabled(hwnd)
                && isWanted(hwnd));

            if (found != IntPtr.Zero)
                return found;
            if (Bcl.TickCount64 >= deadline)
                return IntPtr.Zero;

            await Task.Delay(PollMs);
        }
    }

    /// <summary>
    /// 대화상자가 닫히기를 기다립니다. <paramref name="followUpOf"/> 가 주어지면 그 대화상자가 띄운 후속 상자(덮어쓰기 확인,
    /// "파일 없음")가 나타나는지도 함께 본다. 후속 상자가 뜨면 원래 대화상자는 그것이 닫힐 때까지 닫히지 않기 때문이다.
    /// </summary>
    private static async Task<(bool Closed, IntPtr FollowUp)> WaitClosedAsync(IntPtr dialog, IntPtr followUpOf)
    {
        var deadline = Bcl.TickCount64 + CloseGraceMs;
        while (true)
        {
            if (!NativeMethods.IsWindow(dialog) || !NativeMethods.IsWindowVisible(dialog))
                return (true, IntPtr.Zero);

            if (followUpOf != IntPtr.Zero)
            {
                var followUp = ProcessWindows.Visible().FirstOrDefault(hwnd =>
                    hwnd != followUpOf
                    && NativeMethods.ClassOf(hwnd) == ProcessWindows.DialogClass
                    && NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) == followUpOf);
                if (followUp != IntPtr.Zero)
                    return (false, followUp);
            }

            if (Bcl.TickCount64 >= deadline)
                return (false, IntPtr.Zero);

            await Task.Delay(PollMs);
        }
    }

    /// <summary>
    /// 열기·저장 대화상자인지. 새 방식은 탐색 영역 창으로, 예전 방식은 파일 이름 칸의 ID 로 알아본다.
    /// 새 방식 저장 대화상자에는 cmb13 이 없으므로 ID 만으로는 놓친다(실측).
    /// </summary>
    private static bool IsFileDialog(IntPtr dialog)
    {
        return NativeMethods.DescendantsOf(dialog).Any(child =>
            NativeMethods.GetDlgCtrlID(child) is FileNameComboId or FileNameEditId
            || NativeMethods.ClassOf(child) == ShellViewHostClass);
    }

    /// <summary>
    /// MessageBox 인지. 본문을 담는 정적 컨트롤(0xFFFF)이 있고 파일 대화상자가 아니어야 한다. 이 조건이 없으면 버튼 ID 가 같은
    /// 다른 대화상자(인쇄, 저장)를 메시지 상자로 알고 누르게 된다.
    /// </summary>
    private static bool IsMessageBox(IntPtr dialog)
    {
        return !IsFileDialog(dialog)
               && NativeMethods.DescendantsOf(dialog).Any(child => NativeMethods.GetDlgCtrlID(child) == MessageTextId);
    }

    /// <summary>
    /// 파일 이름 편집 칸. 대화상자 종류마다 자리가 다르다(실측):
    /// 열기는 cmb13 콤보 상자 안 깊숙이(ComboBoxEx32 → ComboBox → Edit), 저장은 탐색 영역 안 콤보 상자의 Edit(0x3E9),
    /// 예전 방식은 edt1 이다. 어느 쪽이든 직계 자식만 보아서는 찾지 못한다.
    /// </summary>
    private static IntPtr FileNameEditOf(IntPtr dialog)
    {
        IntPtr saveEdit = IntPtr.Zero;
        foreach (var child in NativeMethods.DescendantsOf(dialog))
        {
            var id = NativeMethods.GetDlgCtrlID(child);
            var isEdit = NativeMethods.ClassOf(child) == "Edit";

            if (isEdit && id == FileNameEditId)
                return child;

            if (isEdit && id == SaveFileNameEditId && saveEdit == IntPtr.Zero && NativeMethods.IsWindowVisible(child))
                saveEdit = child;

            if (id != FileNameComboId)
                continue;

            if (isEdit)
                return child;

            var inner = NativeMethods.DescendantsOf(child).FirstOrDefault(grand => NativeMethods.ClassOf(grand) == "Edit");
            if (inner != IntPtr.Zero)
                return inner;
        }

        return saveEdit;
    }

    #endregion
}

/// <summary>창·대화상자 조작의 결과. 실패면 Text 가 이유다.</summary>
/// <param name="Success">성공했는지.</param>
/// <param name="Text">성공 설명 또는 실패 이유.</param>
internal sealed record NativeResult(bool Success, string Text)
{
    /// <summary>성공 결과.</summary>
    public static NativeResult Ok(string text) => new(true, text);

    /// <summary>실패 결과.</summary>
    public static NativeResult Fail(string text) => new(false, text);
}
