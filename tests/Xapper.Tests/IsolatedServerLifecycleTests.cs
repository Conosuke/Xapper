using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Xapper.Tests;

/// <summary>Verifies the OS job safety net even when the server cannot run its cleanup code.</summary>
[Collection(DesktopWindowCollection.Name)]
public sealed class IsolatedServerLifecycleTests
{
    #region Tests

    [Fact]
    public async Task AbruptServerExit_ClosesTheIsolatedAppAndDesktop()
    {
        var serverExe = WindowsTestApps.Find("Xapper.McpServer");
        using var server = Process.Start(new ProcessStartInfo(serverExe)
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start the test MCP server.");
        var stderr = server.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try
        {
            await server.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0", id = 1, method = "initialize",
                @params = new { protocolVersion = "2024-11-05", capabilities = new { }, clientInfo = new { name = "Xapper lifecycle test", version = "1" } }
            }));
            await ReadResponse(server, 1, deadline.Token);
            await server.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            await server.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0", id = 2, method = "tools/call",
                @params = new { name = "xapper_launch", arguments = new { exePath = WindowsTestApps.Find("Xapper.TestApp"), isolated = true } }
            }));
            var response = await ReadResponse(server, 2, deadline.Token);
            var text = string.Join("\n", response.GetProperty("result").GetProperty("content").EnumerateArray()
                .Where(c => c.GetProperty("type").GetString() == "text").Select(c => c.GetProperty("text").GetString()));
            var match = Regex.Match(text, @"Launched process (\d+)");
            Assert.True(match.Success, text);
            using var app = Process.GetProcessById(int.Parse(match.Groups[1].Value));
            _ = app.Handle;
            var desktopName = $"xapper-{server.Id}-1";
            Assert.True(WindowsTestApps.DesktopExists(desktopName));
            server.Kill();
            await server.WaitForExitAsync(deadline.Token);
            Assert.True(app.WaitForExit(5000));
            Assert.False(WindowsTestApps.DesktopExists(desktopName));
        }
        finally
        {
            if (!server.HasExited) { server.Kill(); await server.WaitForExitAsync(); }
            await stderr;
        }
    }

    #endregion

    #region Helpers

    private static async Task<JsonElement> ReadResponse(Process server, int id, CancellationToken ct)
    {
        while (await server.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            using var json = JsonDocument.Parse(line);
            if (json.RootElement.TryGetProperty("id", out var responseId) && responseId.TryGetInt32(out var actual) && actual == id)
                return json.RootElement.Clone();
        }
        throw new InvalidOperationException("The test server closed stdout before its response.");
    }

    #endregion
}
