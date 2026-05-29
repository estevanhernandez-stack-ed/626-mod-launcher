using System.Text.Json;

namespace ModManager.Core.Persistence;

/// <summary>
/// Reads/writes the games registry (games.json) under a data root. Extracted from the App's
/// LauncherService so the App and the headless agent-access MCP share ONE reader — no second
/// source of truth, no drift. On-disk shape is camelCase (the launcher's historical Electron-shared
/// convention, written via <see cref="AtomicJson"/>); reads are case-insensitive for tolerance.
/// A missing or unreadable file yields an empty registry — <see cref="Load"/> never throws.
/// </summary>
public static class RegistryStore
{
    public const string FileName = "games.json";

    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    public static string PathFor(string dataRoot) => Path.Combine(dataRoot, FileName);

    public static GameRegistry Load(string dataRoot)
    {
        try { return JsonSerializer.Deserialize<GameRegistry>(File.ReadAllText(PathFor(dataRoot)), ReadOpts) ?? Registry.EmptyRegistry(); }
        catch { return Registry.EmptyRegistry(); }
    }

    public static void Save(string dataRoot, GameRegistry reg)
    {
        Directory.CreateDirectory(dataRoot);
        AtomicJson.WriteJsonAtomic(PathFor(dataRoot), reg);
    }
}
