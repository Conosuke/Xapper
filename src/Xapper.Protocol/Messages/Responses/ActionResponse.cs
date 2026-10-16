namespace Xapper.Protocol.Messages.Responses;

/// <summary>
/// click, type, select, toggle, expand, scroll 등 UI 액션의 공통 응답.
/// </summary>
public sealed class ActionResponse
{
    /// <summary>액션 성공 여부.</summary>
    public bool Success { get; set; }

    /// <summary>성공 시 부가 메시지.</summary>
    public string? Message { get; set; }

    /// <summary>실패 시 에러 메시지.</summary>
    public string? Error { get; set; }

    /// <summary>true 면 조작은 전달됐지만 앱이 제한 시간 안에 처리를 끝내지 못했다(대개 모달 대화상자). 결과는 아직 알 수 없다.</summary>
    public bool Pending { get; set; }

    /// <summary>
    /// 조작이 띄운 모달 대화상자(제목과 핸들). 없으면 null. 이 값이 있으면 Pending 도 true 다 — 앱은 그 대화상자가 닫힐
    /// 때까지 다른 일을 하지 않으므로 배치는 멈추지만, 스크립트는 다음 줄에서 그 대화상자에 답할 수 있다.
    /// </summary>
    public string? ModalDialog { get; set; }
}
