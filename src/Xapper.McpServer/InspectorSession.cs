using Xapper.McpServer.Ipc;

namespace Xapper.McpServer;

/// <summary>An immutable session snapshot prevents policy and IPC from referring to different active apps.</summary>
/// <param name="Client">The connection captured with this identity.</param>
/// <param name="ProcessId">The process served by that connection.</param>
/// <param name="Isolated">Whether the same session owns an isolated desktop application.</param>
public sealed record InspectorSession(InspectorClient Client, int ProcessId, bool Isolated);
