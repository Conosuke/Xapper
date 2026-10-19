using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Xapper.Inspector.Diagnostics;

/// <summary>
/// WPF 가 트레이스로만 알리는 바인딩 오류를 모아 둔다.
/// 경로를 찾지 못한 바인딩은 요소에 오류로 남지 않으므로(HasError 는 유효성 검사만 본다) 트레이스를 듣는 수밖에 없다.
/// 같은 템플릿의 같은 바인딩은 행마다 같은 오류를 내므로, 객체 해시만 다른 메시지는 한 건으로 합쳐 센다.
/// </summary>
public sealed class BindingErrorCollector : TraceListener
{
    #region Constants

    /// <summary>담아 두는 서로 다른 오류의 최대 수. 넘친 것은 건수만 센다.</summary>
    public const int Capacity = 100;

    #endregion

    #region Fields

    /// <summary>행마다 달라지는 객체 해시. 이것만 다른 메시지는 같은 바인딩에서 난 것이다.</summary>
    private static readonly Regex HashCode = new(@" ?\(HashCode=-?\d+\)", RegexOptions.Compiled);

    private readonly object _gate = new();
    private readonly Dictionary<string, BindingErrorRecord> _byMessage = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private int _overflow;

    #endregion

    #region Public Properties

    /// <summary>듣기 시작한 시각(<see cref="Bcl.TickCount64"/>). 시작하지 않았으면 null.</summary>
    public long? StartedAt { get; private set; }

    /// <inheritdoc />
    public override bool IsThreadSafe => true;

    #endregion

    #region Public Methods

    /// <summary>
    /// 바인딩 트레이스를 켜고 듣기 시작합니다.
    /// 디버거가 없으면 WPF 는 바인딩 트레이스를 내보내지 않는다. Refresh 가 그 스위치를 켜지만 설정 파일을 다시 읽으며
    /// 듣는 쪽과 수준을 되돌리므로, 등록은 반드시 그 뒤에 한다.
    /// </summary>
    public void Start()
    {
        PresentationTraceSources.Refresh();
        var source = PresentationTraceSources.DataBindingSource;
        source.Listeners.Add(this);
        source.Switch.Level = SourceLevels.Error;
        StartedAt = Bcl.TickCount64;
    }

    /// <summary>모아 둔 오류를 처음 본 순서대로 돌려줍니다.</summary>
    /// <param name="clear">돌려준 뒤 비울지. 비우면 다음 호출은 그 뒤에 난 것만 본다.</param>
    /// <param name="overflow">상한을 넘어 내용 없이 건수만 센 오류의 수.</param>
    public IReadOnlyList<BindingErrorRecord> Read(bool clear, out int overflow)
    {
        lock (_gate)
        {
            var records = _order.Select(key => _byMessage[key]).ToList();
            overflow = _overflow;

            if (clear)
            {
                _order.Clear();
                _byMessage.Clear();
                _overflow = 0;
            }

            return records;
        }
    }

    /// <inheritdoc />
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
    {
        Record(message);
    }

    /// <inheritdoc />
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id,
        string? format, params object?[]? args)
    {
        Record(Format(format, args));
    }

    /// <summary>머리말 조각. WPF 는 TraceEvent 로만 알리므로 쓸 것이 없다.</summary>
    public override void Write(string? message)
    {
    }

    /// <summary>머리말 뒤의 줄. WPF 는 TraceEvent 로만 알리므로 쓸 것이 없다.</summary>
    public override void WriteLine(string? message)
    {
    }

    #endregion

    #region Protected Methods

    /// <summary>트레이스 소스에서 빠집니다. 빠지지 않으면 소스가 이 객체를 계속 붙든다.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            PresentationTraceSources.DataBindingSource.Listeners.Remove(this);

        base.Dispose(disposing);
    }

    #endregion

    #region Private Methods

    /// <summary>오류 한 건을 담습니다. UI 스레드에서 불리는 자리이므로 예외를 내지 않는다.</summary>
    private void Record(string? message)
    {
        if (Bcl.IsBlank(message))
            return;

        var key = HashCode.Replace(message.Trim(), "");
        var now = Bcl.TickCount64;

        lock (_gate)
        {
            if (_byMessage.TryGetValue(key, out var seen))
            {
                _byMessage[key] = seen with { Count = seen.Count + 1, LastAt = now };
                return;
            }

            if (_byMessage.Count >= Capacity)
            {
                _overflow++;
                return;
            }

            _byMessage[key] = new BindingErrorRecord(key, 1, now, now);
            _order.Add(key);
        }
    }

    /// <summary>서식 문자열을 채웁니다. 서식이 깨졌으면 원문을 그대로 쓴다.</summary>
    private static string? Format(string? format, object?[]? args)
    {
        if (format is null || args is not { Length: > 0 })
            return format;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    #endregion
}

/// <summary>같은 바인딩에서 난 오류 묶음.</summary>
/// <param name="Message">객체 해시를 뺀 오류 문장.</param>
/// <param name="Count">난 횟수.</param>
/// <param name="FirstAt">처음 난 시각(<see cref="Bcl.TickCount64"/>).</param>
/// <param name="LastAt">마지막으로 난 시각.</param>
public sealed record BindingErrorRecord(string Message, int Count, long FirstAt, long LastAt);
