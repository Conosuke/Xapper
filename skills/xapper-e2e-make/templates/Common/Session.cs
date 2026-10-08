using ModelContextProtocol.Client;

namespace E2E.Common;

/// <summary>
/// What setups and cases work with: the app under test, the output folder of whatever is running,
/// the Xapper server, and any other MCP server a case connects to for checks beyond the screen.
/// One session lasts the whole run, so the app a setup prepared stays up for the cases after it.
/// </summary>
public sealed class Session : IAsyncDisposable
{
    private readonly List<McpClient> _stepClients = [];
    private McpClient? _xapperClient;
    private Connection? _xapper;

    internal Session(string app)
    {
        App = app;
    }

    /// <summary>The case id or setup being run, for example "C01" or "setup app-started"; it labels output lines.</summary>
    public string Label { get; internal set; } = "";

    /// <summary>Full path of the app under test, from APP_EXE.</summary>
    public string App { get; }

    /// <summary>The output folder of the case or setup being run (results/&lt;id&gt; next to the cases).</summary>
    public string Output { get; internal set; } = "";

    /// <summary>Calls a Xapper tool; a result marked as an error ends the case or setup.</summary>
    public Task<string> Call(string tool, object? args = null)
    {
        if (_xapper is null)
            throw new InvalidOperationException("Xapper is not connected.");
        return _xapper.Call(tool, args);
    }

    /// <summary>
    /// Starts the MCP server whose executable path is in the given environment variable - for example
    /// the app's own server - so the case can check the outcome by more than the screen. The server is
    /// closed when the case or setup that started it ends.
    /// </summary>
    public async Task<Connection> Connect(string environmentVariable)
    {
        var command = Environment.GetEnvironmentVariable(environmentVariable)
                      ?? throw new InvalidOperationException($"Set {environmentVariable}.");
        var client = await StartAsync(environmentVariable, command);
        _stepClients.Add(client);
        return new Connection(client, this, environmentVariable);
    }

    /// <summary>Starts the Xapper server, once for the whole run.</summary>
    internal async Task ConnectXapperAsync(string command)
    {
        _xapperClient = await StartAsync("xapper", command);
        _xapper = new Connection(_xapperClient, this, "xapper");
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
            Console.Error.WriteLine($"[{Label}] clean-up {tool} did not run: {ex.Message}");
        }
    }

    /// <summary>Closes the servers the case or setup that just ended connected to.</summary>
    internal async Task EndStepAsync()
    {
        foreach (var client in _stepClients)
            await client.DisposeAsync();
        _stepClients.Clear();
    }

    /// <summary>Closes every server the run started.</summary>
    public async ValueTask DisposeAsync()
    {
        await EndStepAsync();
        if (_xapperClient is not null)
            await _xapperClient.DisposeAsync();
    }

    private static Task<McpClient> StartAsync(string name, string command)
    {
        return McpClient.CreateAsync(new StdioClientTransport(new() { Name = name, Command = command }));
    }
}
