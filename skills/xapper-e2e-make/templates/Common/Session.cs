using ModelContextProtocol.Client;

namespace E2E.Common;

/// <summary>
/// What a case's steps work with: the app under test, the case's output folder, the Xapper server,
/// and any other MCP server the case connects to for checks beyond the screen.
/// </summary>
public sealed class Session : IAsyncDisposable
{
    private readonly List<McpClient> _clients = [];
    private Connection? _xapper;

    internal Session(string caseId, string app, string output)
    {
        CaseId = caseId;
        App = app;
        Output = output;
    }

    /// <summary>The case's id, for example "C01".</summary>
    public string CaseId { get; }

    /// <summary>Full path of the app under test, from APP_EXE.</summary>
    public string App { get; }

    /// <summary>The case's output folder (results/&lt;id&gt; next to the case files).</summary>
    public string Output { get; }

    /// <summary>Calls a Xapper tool; a result marked as an error ends the case.</summary>
    public Task<string> Call(string tool, object? args = null)
    {
        if (_xapper is null)
            throw new InvalidOperationException("Xapper is not connected.");
        return _xapper.Call(tool, args);
    }

    /// <summary>
    /// Starts the MCP server whose executable path is in the given environment variable - for example
    /// the app's own server - so the case can check the outcome by more than the screen.
    /// </summary>
    public async Task<Connection> Connect(string environmentVariable)
    {
        var command = Environment.GetEnvironmentVariable(environmentVariable)
                      ?? throw new InvalidOperationException($"Set {environmentVariable}.");
        return new Connection(await StartAsync(environmentVariable, command), CaseId, environmentVariable);
    }

    /// <summary>Starts the Xapper server.</summary>
    internal async Task ConnectXapperAsync(string command)
    {
        _xapper = new Connection(await StartAsync("xapper", command), CaseId, "xapper");
    }

    /// <summary>Calls a Xapper tool during clean-up: never throws, and does nothing if Xapper never connected.</summary>
    internal async Task TryAsync(string tool, object? args = null)
    {
        if (_xapper is null)
            return;
        try
        {
            await _xapper.CallQuietly(tool, args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{CaseId}] clean-up {tool} did not run: {ex.Message}");
        }
    }

    /// <summary>Closes every server the case started.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
            await client.DisposeAsync();
    }

    private async Task<McpClient> StartAsync(string name, string command)
    {
        var client = await McpClient.CreateAsync(new StdioClientTransport(new() { Name = name, Command = command }));
        _clients.Add(client);
        return client;
    }
}
