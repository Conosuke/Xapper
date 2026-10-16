using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Xapper.McpServer.Ipc;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Requests;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 시각 트리 밖의 창을 다루는 MCP 도구. MessageBox 와 공용 열기·저장 대화상자는 Win32 창이라 스냅샷에 나오지 않고,
/// 떠 있는 동안 앱은 다른 조작을 처리하지 않는다. 그래서 그 창에 답하는 길과, 언제 무엇이 떴는지 기록을 읽는 길을 따로 낸다.
/// 모든 도구는 연결된 앱의 창만 다룬다.
/// </summary>
[McpServerToolType]
public sealed class WindowTools
{
    #region Fields

    private readonly SessionManager _sessionManager;

    #endregion

    #region Constructor

    /// <summary><see cref="WindowTools"/>의 새 인스턴스를 생성합니다.</summary>
    /// <param name="sessionManager">활성 Inspector 세션을 제공하는 세션 관리자.</param>
    public WindowTools(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    #endregion

    #region MCP Tools

    /// <summary>앱의 최상위 창과 뜬 창 기록을 JSON 으로 돌려줍니다.</summary>
    [McpServerTool(Name = "xapper_list_windows"), Description(
        "List the attached app's top-level windows as JSON: \"open\" holds what is showing now, \"shown\" holds every " +
        "window that appeared since `since`, with shownAt/hiddenAt times (UTC, ISO-8601) and whether it was modal. " +
        "Menus, tooltips and drop-downs are left out unless modal. To tell whether an action raised a modal dialog, " +
        "pass the \"now\" of a call made before the action as `since`; no entry with \"modal\": true means none appeared. " +
        "Message boxes and open/save dialogs are Win32 windows that snapshots do not show - this is how you see them.")]
    public async Task<string> ListWindows(
        [Description("Only windows that appeared at or after this time (the \"now\" of an earlier response). Optional")] string? since = null,
        CancellationToken ct = default)
    {
        var response = await SendAsync("windows", new WindowsRequest { Since = since }, ct);
        if (response.Error is not null)
            return response.Error;

        return response.Message.Payload?.GetRawText() ?? "Error: the inspector returned no windows.";
    }

    /// <summary>앱의 메시지 상자에 답합니다.</summary>
    [McpServerTool(Name = "xapper_message_box"), Description(
        "Answer the attached app's message box by pressing one of its buttons, then confirm it closed. Buttons are " +
        "pressed by their standard ID, so this works whatever language Windows shows them in. The response quotes the " +
        "box's title and message. Fails if no message box appears within timeout, if it has no such button (the error " +
        "lists the ones it has), or if it stays open.")]
    public async Task<string> MessageBox(
        [Description("Button to press: ok, cancel, yes, no, abort, retry, ignore, close, tryagain or continue")] string button,
        [Description("How long to wait for the message box to appear, in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var response = await SendAsync("messageBox", new MessageBoxRequest { Button = button, Timeout = timeout }, ct);
        return response.Error ?? ResponseFormat.Action(response.Message, "Answered");
    }

    /// <summary>앱의 열기·저장 대화상자에 경로로 답합니다.</summary>
    [McpServerTool(Name = "xapper_file_dialog"), Description(
        "Answer the attached app's open or save dialog: put the path in its file name box, confirm, and check it closed. " +
        "If the dialog raises a follow-up box instead (overwrite? file not found?), this fails quoting that box and leaves " +
        "it open - answer it with xapper_message_box.")]
    public async Task<string> FileDialog(
        [Description("Absolute path to enter, e.g. C:\\data\\orders.csv")] string path,
        [Description("How long to wait for the dialog to appear, in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var response = await SendAsync("fileDialog", new FileDialogRequest { Path = path, Timeout = timeout }, ct);
        return response.Error ?? ResponseFormat.Action(response.Message, "Answered");
    }

    /// <summary>앱의 창 하나를 최대화·복원·최소화하거나 앞으로 가져옵니다.</summary>
    [McpServerTool(Name = "xapper_window"), Description(
        "Maximize, restore, minimize or bring to the front one of the attached app's windows, then confirm the window " +
        "actually changed. Without hwnd it acts on the app's main window. Windows may refuse \"front\" when another app " +
        "holds the user's input; that is reported as an error rather than claimed as done.")]
    public async Task<string> Window(
        [Description("maximize, restore, minimize or front")] string action,
        [Description("Window handle from xapper_list_windows, e.g. 0x1A2B. Optional - defaults to the main window")] string? hwnd = null,
        CancellationToken ct = default)
    {
        var response = await SendAsync("window", new WindowRequest { Action = action, Hwnd = hwnd }, ct);
        return response.Error ?? ResponseFormat.Action(response.Message, "Done");
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 활성 세션에 요청을 보냅니다. 세션이 없거나 Inspector 가 오류로 답하면 "Error:" 로 시작하는 글을 Error 에 담는다.
    /// </summary>
    private async Task<(IpcMessage Message, string? Error)> SendAsync(string method, object payload, CancellationToken ct)
    {
        InspectorClient client;
        try
        {
            client = _sessionManager.GetActive();
        }
        catch (InvalidOperationException ex)
        {
            return (IpcSerializer.CreateError("", ex.Message), $"Error: {ex.Message}");
        }

        var response = await client.SendAsync(IpcSerializer.CreateRequest(method, payload), ct);
        return response.Type == "error" ? (response, $"Error: {ErrorText(response)}") : (response, null);
    }

    /// <summary>오류 응답의 문장만. Inspector 는 {"error": "..."} 로 싸서 보낸다.</summary>
    private static string ErrorText(IpcMessage response)
    {
        if (response.Payload is { ValueKind: JsonValueKind.Object } payload
            && payload.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.String)
            return error.GetString() ?? "";

        return response.Payload?.GetRawText() ?? "the inspector returned an error with no message.";
    }

    #endregion
}
