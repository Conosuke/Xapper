namespace Xapper.Protocol.Messages.Requests;

/// <summary>모아 둔 바인딩 오류를 달라는 메시지.</summary>
public sealed class BindingErrorsRequest
{
    /// <summary>돌려준 뒤 비울지. 비우면 다음 호출은 그 뒤에 난 것만 본다.</summary>
    public bool Clear { get; set; }
}
