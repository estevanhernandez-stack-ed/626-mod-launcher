// src/ModManager.Core/Plugins/InstalledPluginsStore.cs
using System.IO;
using System.Text.Json;

namespace ModManager.Core.Plugins;

/// <summary>The on-disk record of which plugin is installed at which version (lives next to the
/// plugin dlls). Drives the "already current" gate and the "is anything installed" decision. camelCase
/// + atomic (via <see cref="AtomicJson"/>); a missing or corrupt file reads as "nothing installed".</summary>
public static class InstalledPluginsStore
{
    private sealed class File_ { public Dictionary<string, string> Versions { get; set; } = new(); }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Dictionary<string, string>();
            var file = JsonSerializer.Deserialize<File_>(File.ReadAllText(path), Json);
            return file?.Versions ?? new Dictionary<string, string>();
        }
        catch { return new Dictionary<string, string>(); }
    }

    public static void Write(string path, IReadOnlyDictionary<string, string> versions)
        => AtomicJson.WriteJsonAtomic(path, new File_ { Versions = new Dictionary<string, string>(versions) });
}
