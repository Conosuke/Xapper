using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Xapper.McpServer.Ipc;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Requests;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 프로퍼티 조회, 바인딩 검사, 값 검증(assert) 등 진단 MCP 도구를 제공하는 클래스.
/// </summary>
[McpServerToolType]
public sealed class DiagnosticTools
{
    private readonly SessionManager _sessionManager;

    public DiagnosticTools(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    [McpServerTool(Name = "xapper_get_property"), Description(
        "Read one property of an element: a dependency property or public property of that control type; " +
        "paths like ItemsSource.Count are not resolved.")]
    public async Task<string> GetProperty(
        [Description("Property name (e.g., 'Text', 'IsEnabled', 'Visibility', 'Content')")] string propertyName,
        [Description(ToolDescriptions.Ref)] int? @ref = null,
        [Description(ToolDescriptions.Target)] string? target = null,
        CancellationToken ct = default)
    {
        if (@ref is null && target is null)
            return "Error: pass ref or target.";

        var client = _sessionManager.GetActive();
        var response = await client.GetPropertyAsync(@ref, target: target, propertyName: propertyName, ct: ct);

        return ResponseFormat.Property(response);
    }

    [McpServerTool(Name = "xapper_get_bindings"), Description("Get data binding info and errors for an element")]
    public async Task<string> GetBindings(
        [Description("Element ref from snapshot/find")] int @ref,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.GetBindingsAsync(@ref, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<BindingsResponse>(response.Payload!.Value);

        if (result.Bindings.Count == 0)
            return $"ref={@ref}: No bindings found.";

        var sb = new StringBuilder();
        sb.AppendLine($"ref={@ref} Bindings ({result.Bindings.Count}):");
        foreach (var b in result.Bindings)
        {
            var status = b.HasError ? $" [ERROR: {b.ErrorMessage}]" : "";
            sb.AppendLine($"  {b.PropertyName} ← {b.Path} (Source: {b.Source}, Mode: {b.Mode}){status}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 주입 시점부터 앱이 낸 XAML 바인딩 오류를 읽습니다.
    /// 경로 오류는 요소에 남지 않아 xapper_get_bindings 로는 보이지 않고, WPF 의 바인딩 트레이스에만 나온다.
    /// </summary>
    [McpServerTool(Name = "xapper_binding_errors"), Description(
        "Read the XAML binding errors the app has raised since Xapper got into it - a path that names a " +
        "property the data does not have, a missing source, a failed conversion. These never show on the " +
        "element, so xapper_get_bindings cannot see them; WPF only writes them to its debug trace. The same " +
        "binding repeated across list rows is one line with a count. xapper_launch catches errors from the " +
        "very first screen; after xapper_attach only errors raised from then on are seen, so reopen the screen " +
        "to raise them again. Pass clear=true to start over and see only what the next action raises. " +
        "Call it every time a screen is new or its XAML was changed, after opening that screen: a broken binding " +
        "leaves a blank value with no other sign.")]
    public async Task<string> BindingErrors(
        [Description("Forget the errors read so far, so the next call shows only new ones (default false)")] bool clear = false,
        CancellationToken ct = default)
    {
        InspectorClient client;
        try
        {
            client = _sessionManager.GetActive();
        }
        catch (InvalidOperationException ex)
        {
            return $"Error: {ex.Message}";
        }

        var request = IpcSerializer.CreateRequest("bindingErrors", new BindingErrorsRequest { Clear = clear });
        var response = await client.SendAsync(request, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        if (response.Payload is not { } payload)
            return "Error: the inspector returned no binding errors.";

        return DescribeBindingErrors(IpcSerializer.DeserializePayload<BindingErrorsResponse>(payload), clear);
    }

    /// <summary>
    /// 바인딩 오류 목록을 읽을 문장으로 옮깁니다.
    /// 비어 있을 때 언제부터 들었는지를 함께 적는다. 그 전에 난 오류가 없다는 뜻으로 읽히면 안 되기 때문이다.
    /// </summary>
    private static string DescribeBindingErrors(BindingErrorsResponse result, bool cleared)
    {
        var since = $"{result.CollectingForMs / 1000} s ago";
        var tail = cleared ? " Cleared - the next call shows only new ones." : "";

        if (result.Errors.Count == 0 && result.Overflow == 0)
            return $"No binding errors since collection started {since}. Errors raised before that were not seen." + tail;

        var sb = new StringBuilder();
        sb.AppendLine($"{result.Errors.Count} distinct binding errors since collection started {since}, first seen first:");

        foreach (var entry in result.Errors)
            sb.AppendLine($"  x{entry.Count}  last {entry.LastAgoMs} ms ago  {entry.Message}");

        if (result.Overflow > 0)
            sb.AppendLine($"  ... and {result.Overflow} more beyond the first {result.Errors.Count}. Fix these and clear to see the rest.");

        return sb.ToString().TrimEnd() + tail;
    }

    [McpServerTool(Name = "xapper_assert"), Description("Assert a property value on an element (returns PASS/FAIL)")]
    public async Task<string> Assert(
        [Description("Property name to check")] string property,
        [Description("Expected value (string comparison, case-insensitive)")] string expected,
        [Description(ToolDescriptions.Ref)] int? @ref = null,
        [Description(ToolDescriptions.Target)] string? target = null,
        CancellationToken ct = default)
    {
        if (@ref is null && target is null)
            return "Error: pass ref or target.";

        var client = _sessionManager.GetActive();
        var response = await client.AssertAsync(@ref, target: target, property: property, expected: expected, ct: ct);

        return ResponseFormat.Assert(response);
    }
}
