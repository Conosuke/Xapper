namespace Xapper.Protocol.Messages.Requests;

/// <summary>앱의 열기·저장 대화상자에 경로로 답하라는 메시지.</summary>
public sealed class FileDialogRequest
{
    /// <summary>파일 이름 칸에 넣을 절대 경로.</summary>
    public string Path { get; set; } = "";

    /// <summary>대화상자가 나타나기를 기다릴 시간(ms).</summary>
    public int Timeout { get; set; } = 5000;
}
