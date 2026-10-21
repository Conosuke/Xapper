namespace Xapper.Inspector.NativeWindows;

/// <summary>
/// 이 프로세스의 창을 최대화·복원·최소화하거나 앞으로 가져온다.
/// 명령을 보낸 것으로 끝내지 않고 실제 창 상태로 확인한다. ShowWindow 의 반환값은 "전에 보이던 창이었는가" 라서 성공 여부가
/// 아니고, SetForegroundWindow 는 Windows 가 전경 전환을 막으면 작업 표시줄만 깜빡이고 끝나기 때문이다.
/// </summary>
internal static class WindowCommands
{
    #region Constants

    /// <summary>상태가 바뀌기를 기다리는 최대 시간.</summary>
    private const int SettleMs = 2000;

    private const int PollMs = 25;

    #endregion

    #region Public Methods

    /// <summary>창 하나에 동작을 적용하고 결과 상태를 확인합니다.</summary>
    /// <param name="action">maximize, restore, minimize, front 중 하나. 대소문자 무관.</param>
    /// <param name="hwnd">대상 창. 이 프로세스의 창이어야 한다.</param>
    public static async Task<NativeResult> RunAsync(string action, IntPtr hwnd)
    {
        if (!ProcessWindows.IsOwn(hwnd))
            return NativeResult.Fail($"{ProcessWindows.Format(hwnd)} is not a window of this app.");

        var title = NativeMethods.TitleOf(hwnd);
        var name = action.Trim().ToLowerInvariant();
        if (name == "front" && IsolatedMode.RefuseForeground() is { } refusal)
            return NativeResult.Fail(refusal);

        switch (name)
        {
            case "maximize":
                NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_MAXIMIZE);
                return Verdict(await UntilAsync(() => NativeMethods.IsZoomed(hwnd)), "Maximized", title, hwnd);

            case "minimize":
                NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_MINIMIZE);
                return Verdict(await UntilAsync(() => NativeMethods.IsIconic(hwnd)), "Minimized", title, hwnd);

            case "restore":
                // 최대화했다가 최소화한 창은 한 번 복원하면 최대화로 돌아온다(실측). 일반 크기가 될 때까지 한 번 더 복원한다.
                if (NativeMethods.IsIconic(hwnd))
                {
                    NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_RESTORE);
                    await UntilAsync(() => !NativeMethods.IsIconic(hwnd));
                }

                if (NativeMethods.IsZoomed(hwnd))
                    NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_RESTORE);

                return Verdict(await UntilAsync(() => !NativeMethods.IsZoomed(hwnd) && !NativeMethods.IsIconic(hwnd)),
                    "Restored", title, hwnd);

            case "front":
                // 최소화된 창은 전경이 되어도 보이지 않으므로 먼저 되살린다.
                if (NativeMethods.IsIconic(hwnd))
                {
                    NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_RESTORE);
                    await UntilAsync(() => !NativeMethods.IsIconic(hwnd));
                }

                NativeMethods.SetForegroundWindow(hwnd);
                if (await UntilAsync(() => NativeMethods.GetForegroundWindow() == hwnd))
                    return NativeResult.Ok($"Brought \"{title}\" ({ProcessWindows.Format(hwnd)}) to the front");

                return NativeResult.Fail(
                    $"Windows refused to bring \"{title}\" to the front. It only lets the app that has the user's last " +
                    "input take the foreground; the taskbar button flashes instead.");

            default:
                return NativeResult.Fail($"Unknown action \"{action}\". Use maximize, restore, minimize or front.");
        }
    }

    #endregion

    #region Private Methods

    /// <summary>확인 결과를 응답으로.</summary>
    private static NativeResult Verdict(bool reached, string verb, string title, IntPtr hwnd)
    {
        return reached
            ? NativeResult.Ok($"{verb} \"{title}\" ({ProcessWindows.Format(hwnd)})")
            : NativeResult.Fail($"\"{title}\" ({ProcessWindows.Format(hwnd)}) did not change within {SettleMs} ms.");
    }

    /// <summary>조건이 참이 될 때까지 기다립니다.</summary>
    private static async Task<bool> UntilAsync(Func<bool> condition)
    {
        var deadline = Bcl.TickCount64 + SettleMs;
        while (!condition())
        {
            if (Bcl.TickCount64 >= deadline)
                return false;
            await Task.Delay(PollMs);
        }

        return true;
    }

    #endregion
}
