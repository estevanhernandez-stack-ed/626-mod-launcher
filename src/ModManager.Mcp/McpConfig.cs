namespace ModManager.Mcp;

/// <summary>Where the MCP reads the launcher's on-disk state. Defaults to the launcher's data root
/// (<c>%APPDATA%/ModManagerBuilder</c>); settable so tests and alternate installs can point elsewhere.</summary>
public static class McpConfig
{
    public static string DataRoot { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModManagerBuilder");
}
