namespace Xapper.Protocol.Messages.Responses;

/// <summary>
/// 앱의 최상위 창. 지금 열린 창과, 기준 시각 이후에 뜬 창의 기록을 함께 담는다.
/// 시각은 모두 앱 프로세스의 UTC, ISO-8601 밀리초 문자열이다.
/// </summary>
public sealed class WindowsResponse
{
    /// <summary>응답을 만든 시각. 다음 조회의 since 로 넘기면 그 뒤에 뜬 창만 받는다.</summary>
    public required string Now { get; set; }

    /// <summary>지금 보이는 창. Z 순서의 앞(위)부터.</summary>
    public List<WindowEntry> Open { get; set; } = [];

    /// <summary>since 이후에 뜬 창. 뜬 순서대로이며 닫힌 창도 담는다.</summary>
    public List<WindowEntry> Shown { get; set; } = [];
}

/// <summary>창 하나.</summary>
public sealed class WindowEntry
{
    /// <summary>창 핸들. 예: 0x1A2B.</summary>
    public required string Hwnd { get; set; }

    /// <summary>제목.</summary>
    public required string Title { get; set; }

    /// <summary>창 클래스 이름. 표준 대화상자는 #32770.</summary>
    public required string ClassName { get; set; }

    /// <summary>"dialog"(#32770) 또는 "window".</summary>
    public required string Kind { get; set; }

    /// <summary>뜰 때 다른 창을 막았는지.</summary>
    public bool Modal { get; set; }

    /// <summary>소유 창 핸들. 없으면 null.</summary>
    public string? Owner { get; set; }

    /// <summary>보인 시각. 감시를 시작하기 전부터 열려 있던 창이면 null.</summary>
    public string? ShownAt { get; set; }

    /// <summary>사라진 시각. 아직 열려 있으면 null.</summary>
    public string? HiddenAt { get; set; }
}
