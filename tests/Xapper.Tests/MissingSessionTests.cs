using ModelContextProtocol.Protocol;
using Xapper.McpServer;
using Xapper.McpServer.Infrastructure;
using Xapper.McpServer.Tools;

namespace Xapper.Tests;

/// <summary>No-session errors must survive the SDK as useful tool responses.</summary>
public sealed class MissingSessionTests
{
    [Fact]
    public async Task FindAndElementAt_ReturnTheSessionReason()
    {
        await using var sessions = new SessionManager();
        var tools = new FindTools(sessions);
        foreach (var result in new[] { await tools.Find(name: "Save"), await tools.ElementAt(0, 0) })
        {
            Assert.StartsWith("Error:", result);
            Assert.Contains("No active session", result);
            Assert.True(ToolFailure.IsFailure(result));
        }
    }

    [Fact]
    public async Task Screenshot_ReturnsTheSessionReasonInATextBlock()
    {
        await using var sessions = new SessionManager();
        var path = Path.Combine(Path.GetTempPath(), "xapper-no-session-" + Guid.NewGuid() + ".db");
        try
        {
            using var store = new ScreenStore(path);
            var result = await new CaptureTools(sessions, store, new ScreenTracker()).Screenshot();
            var block = Assert.IsType<TextContentBlock>(Assert.Single(result));
            Assert.StartsWith("Error:", block.Text);
            Assert.Contains("No active session", block.Text);
            Assert.True(ToolFailure.IsFailure(block.Text));
        }
        finally { File.Delete(path); }
    }
}
