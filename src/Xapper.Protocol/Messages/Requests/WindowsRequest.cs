namespace Xapper.Protocol.Messages.Requests;

/// <summary>앱의 최상위 창 목록과 뜬 창 기록을 달라는 메시지.</summary>
public sealed class WindowsRequest
{
    /// <summary>이 시각(ISO-8601) 이후에 뜬 창만 기록에 담는다. 없으면 담아 둔 전부.</summary>
    public string? Since { get; set; }
}
