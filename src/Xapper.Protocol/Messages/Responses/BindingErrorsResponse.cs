namespace Xapper.Protocol.Messages.Responses;

/// <summary>모아 둔 바인딩 오류. 처음 본 순서대로다.</summary>
public sealed class BindingErrorsResponse
{
    /// <summary>서로 다른 오류 목록.</summary>
    public List<BindingErrorEntry> Errors { get; set; } = [];

    /// <summary>상한을 넘어 내용 없이 건수만 센 오류의 수.</summary>
    public int Overflow { get; set; }

    /// <summary>듣기 시작한 지 몇 밀리초 지났는지. 그 전에 난 오류는 볼 수 없다.</summary>
    public long CollectingForMs { get; set; }
}

/// <summary>같은 바인딩에서 난 오류 묶음 하나.</summary>
public sealed class BindingErrorEntry
{
    /// <summary>객체 해시를 뺀 오류 문장.</summary>
    public required string Message { get; set; }

    /// <summary>난 횟수.</summary>
    public int Count { get; set; }

    /// <summary>처음 난 것이 지금으로부터 몇 밀리초 전인지.</summary>
    public long FirstAgoMs { get; set; }

    /// <summary>마지막으로 난 것이 지금으로부터 몇 밀리초 전인지.</summary>
    public long LastAgoMs { get; set; }
}
