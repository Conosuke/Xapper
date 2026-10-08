using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace E2E.Common;

/// <summary>
/// Calls the tools of one MCP server and ends the case at the first result marked as an error,
/// so a case never has to read a response's text to tell success from failure.
/// </summary>
public sealed class Connection
{
    private readonly McpClient _client;
    private readonly string _caseId;
    private readonly string _name;

    internal Connection(McpClient client, string caseId, string name)
    {
        _client = client;
        _caseId = caseId;
        _name = name;
    }

    /// <summary>Calls a tool and returns its text; throws when the result is marked as an error.</summary>
    /// <param name="tool">Tool name, for example "xapper_click".</param>
    /// <param name="args">An anonymous object whose property names are the tool's argument names.</param>
    public async Task<string> Call(string tool, object? args = null)
    {
        var (text, isError) = await CallQuietly(tool, args);
        if (isError)
            throw new InvalidOperationException($"{_name} {tool} failed: {text}");
        return text;
    }

    /// <summary>Calls a tool and returns its text and whether it is marked as an error, without throwing on that.</summary>
    internal async Task<(string Text, bool IsError)> CallQuietly(string tool, object? args = null)
    {
        var given = args?.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(args))
                    ?? new Dictionary<string, object?>();
        var result = await _client.CallToolAsync(tool, given);
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        Console.WriteLine($"[{_caseId}] {_name} {tool}: {text.Split('\n')[0]}");
        return (text, result.IsError == true);
    }
}
