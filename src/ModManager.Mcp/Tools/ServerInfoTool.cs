using System.ComponentModel;
using ModelContextProtocol.Server;

namespace ModManager.Mcp.Tools;

/// <summary>Liveness + versioning. An agent calls this first to confirm the server is up and to
/// learn the tool-catalog version (so it can detect a launcher upgrade and re-discover tools).</summary>
[McpServerToolType]
public static class ServerInfoTool
{
    [McpServerTool(Name = "get_server_info")]
    [Description("Returns the agent-access server version and the read-tool catalog version.")]
    public static object GetServerInfo() => new { serverVersion = "0.1.0", catalogVersion = 1 };
}
