// src/ModManager.Core/Plugins/PluginIndex.cs
using System.Text.Json;

namespace ModManager.Core.Plugins;

/// <summary>One plugin row in the signed feed (mirrors the producer's plugins.json). All strings —
/// versions are compared via <see cref="PluginGate"/>, the sha is hex.</summary>
public sealed record PluginIndexEntry(
    string Id, string DisplayName, string Version, string MinBinaryVersion,
    string DownloadUrl, string SigUrl, string Sha256);

/// <summary>The signed plugin feed index. <see cref="TryParse"/> is tolerant — any malformed input
/// yields false + null, never throws (the App treats that as "no feed", fail-silent).</summary>
public sealed record PluginIndex(int SchemaVersion, IReadOnlyList<PluginIndexEntry> Plugins)
{
    /// <summary>The newest schema this binary understands. A feed declaring a higher version is
    /// rejected wholesale by <see cref="PluginGate"/> (forward-compat: a newer feed never breaks us).</summary>
    public const int KnownSchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static bool TryParse(ReadOnlySpan<byte> utf8Json, out PluginIndex? index)
    {
        index = null;
        if (utf8Json.IsEmpty) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<PluginIndex>(utf8Json, Json);
            if (parsed is null) return false;
            // Normalize a null plugins array to empty so callers never null-check it.
            index = parsed.Plugins is null ? parsed with { Plugins = Array.Empty<PluginIndexEntry>() } : parsed;
            return true;
        }
        catch (JsonException) { return false; }
    }
}
