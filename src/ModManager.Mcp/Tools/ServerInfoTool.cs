using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;

namespace ModManager.Mcp.Tools;

/// <summary>Liveness + versioning. An agent calls this first to confirm the server is up and to
/// learn the tool-catalog version (so it can detect a launcher upgrade and re-discover tools).</summary>
[McpServerToolType]
public static class ServerInfoTool
{
    [McpServerTool(Name = "get_server_info")]
    [Description("Returns the agent-access server version, the read-tool catalog version, and when "
                 + "the binaries answering these calls were built. Check the build time before trusting "
                 + "a payload against a repo you have just changed.")]
    public static object GetServerInfo() => new
    {
        serverVersion = "0.1.0",
        // 2: list_themes, get_app_settings, list_save_mods (E1, third slice).
        // 3: dry_run_intake, intake (E1, fourth slice).
        // 4: register_game, uninstall_mod (E1, fifth slice).
        // 5: apply_theme; get_agent_log reads the launcher's own log when given no gameId (E1, sixth slice).
        // 6: install_save_mod, reset_save_mod, remove_save_mod (E1, seventh slice).
        catalogVersion = 6,
        // .mcp.json runs the server with --no-build, so it serves whatever was last compiled. That
        // sat on a nine-day-old binary through an entire session with nothing to surface it: no
        // payload carried a version, so the only way to notice was comparing file timestamps, which
        // nobody does. Stale reads got attributed to a feed gap instead. An agent can now check its
        // own freshness in one call.
        coreVersion = VersionOf(typeof(Core.GameEntry)),
        coreBuiltUtc = BuiltUtc(typeof(Core.GameEntry)),
        serverBuiltUtc = BuiltUtc(typeof(ServerInfoTool)),
        freshnessNote = "coreBuiltUtc predating your last edit to Core means this server is answering "
                        + "from stale binaries — rebuild before believing a payload that disagrees with the repo.",
    };

    private static string VersionOf(Type t)
        => t.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? t.Assembly.GetName().Version?.ToString()
           ?? "unknown";

    /// <summary>The assembly file's last-write time. Deliberately the file rather than a compiled-in
    /// constant: a constant would be baked at build time and is therefore right, but a file that was
    /// never rebuilt has an old timestamp for exactly the reason we care about, and it needs no build
    /// plumbing to stay honest.</summary>
    private static string? BuiltUtc(Type t)
    {
        try
        {
            var path = t.Assembly.Location;
            return string.IsNullOrEmpty(path)
                ? null
                : File.GetLastWriteTimeUtc(path).ToString("O");
        }
        catch { return null; }
    }
}
