using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModManager.Core.Tools;

/// <summary>
/// On-disk shape of <c>tools.json</c>. A single root object with a <c>tools</c> array,
/// camelCase keys, to align with the shared-JSON convention the Electron app also reads.
/// </summary>
public sealed record ToolRegistryFile(IReadOnlyList<ToolEntry> Tools);

/// <summary>
/// Per-game tool registry persistence. Reads/writes <c>tools.json</c> under the supplied
/// game data directory. Writes are atomic (temp + <see cref="File.Move(string, string, bool)"/>),
/// reads return an empty registry when the file is missing, and throw
/// <see cref="InvalidDataException"/> when the file is unreadable or malformed.
/// </summary>
public static class ToolRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ToolRegistryFile Load(string gameDataDir)
    {
        var path = Path.Combine(gameDataDir, "tools.json");
        if (!File.Exists(path)) return new ToolRegistryFile(Array.Empty<ToolEntry>());

        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception e) { throw new InvalidDataException($"Couldn't read tools.json: {e.Message}", e); }

        try
        {
            return JsonSerializer.Deserialize<ToolRegistryFile>(text, JsonOptions)
                ?? new ToolRegistryFile(Array.Empty<ToolEntry>());
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"tools.json is malformed: {e.Message}", e);
        }
    }

    public static void Save(string gameDataDir, IReadOnlyList<ToolEntry> tools)
    {
        Directory.CreateDirectory(gameDataDir);
        var path = Path.Combine(gameDataDir, "tools.json");
        var tmp = path + ".tmp";

        var json = JsonSerializer.Serialize(new ToolRegistryFile(tools), JsonOptions);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
