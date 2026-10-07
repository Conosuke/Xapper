namespace Xapper.Protocol.Messages.Responses;

/// <summary>
/// 핑 요청에 대한 응답. Inspector가 주입된 프로세스의 정보를 반환.
/// </summary>
public sealed class PongResponse
{
    /// <summary>Inspector가 주입된 대상 프로세스의 PID.</summary>
    public int ProcessId { get; set; }

    /// <summary>대상 프로세스의 이름.</summary>
    public string? ProcessName { get; set; }

    /// <summary>
    /// 응답한 Inspector 어셈블리의 빌드 식별자(MVID, "N" 형식). 서버는 이것으로 앱 안의 Inspector 가 자기가 싣고
    /// 있는 것과 같은 빌드인지 확인한다. 이 필드가 생기기 전의 Inspector 는 보내지 않으므로 null 이다.
    /// </summary>
    public string? InspectorBuild { get; set; }
}
