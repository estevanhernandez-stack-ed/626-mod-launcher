using System.Text.Json;

namespace ModManager.Core.Manifest;

/// <summary>
/// The game manifest baked into the binary — always present, always offline-safe. The single
/// source of truth the KnownEngines / NexusDomains / PopularGames facades read from. Loaded once
/// (cached), validated on load. In Phase 1 a remote source merges over this; the embedded copy is
/// the fallback the remote path can never break.
/// </summary>
public static class EmbeddedGameManifest
{
    private static readonly Lazy<GameManifest> Cached = new(Load);

    /// <summary>The validated embedded manifest (cached after first access).</summary>
    public static GameManifest Current => Cached.Value;

    private static GameManifest Load()
    {
        var asm = typeof(EmbeddedGameManifest).Assembly;
        // Match by suffix so a RootNamespace / folder change can't silently break resource lookup.
        var resourceName = asm.GetManifestResourceNames()
            .Single(n => n.EndsWith("games-manifest.json", StringComparison.Ordinal));

        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("embedded games-manifest.json stream was null");

        var raw = JsonSerializer.Deserialize<GameManifest>(stream, ManifestJson.Options)
            ?? throw new InvalidOperationException("games-manifest.json failed to deserialize");

        var knownEngines = EnginePresets.Presets.Keys.ToHashSet();
        return ManifestValidator.Validate(raw, knownEngines).Manifest;
    }
}
