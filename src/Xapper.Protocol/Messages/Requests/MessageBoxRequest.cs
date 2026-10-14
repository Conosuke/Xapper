namespace Xapper.Protocol.Messages.Requests;

/// <summary>앱의 메시지 상자에 답하라는 메시지.</summary>
public sealed class MessageBoxRequest
{
    /// <summary>누를 버튼 이름. ok·cancel·yes·no·abort·retry·ignore·close·tryagain·continue.</summary>
    public string Button { get; set; } = "";

    /// <summary>상자가 나타나기를 기다릴 시간(ms).</summary>
    public int Timeout { get; set; } = 5000;
}
