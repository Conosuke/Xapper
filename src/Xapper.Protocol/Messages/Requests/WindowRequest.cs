namespace Xapper.Protocol.Messages.Requests;

/// <summary>앱의 창 하나를 최대화·복원·최소화하거나 앞으로 가져오라는 메시지.</summary>
public sealed class WindowRequest
{
    /// <summary>maximize, restore, minimize, front 중 하나.</summary>
    public string Action { get; set; } = "";

    /// <summary>대상 창 핸들("0x1A2B"). 없으면 앱의 주 창.</summary>
    public string? Hwnd { get; set; }
}
