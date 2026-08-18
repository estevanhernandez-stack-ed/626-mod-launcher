using System.Reflection;
using ModManager.Core.Manifest;

namespace ModManager.Mcp;

/// <summary>
/// Applies the launcher's cached remote game-definition feed before any tool answers a question.
///
/// <para>Without this the MCP reads the EMBEDDED snapshot only, while the app reads embedded merged
/// with the feed — so the two give different answers about the same game. Measured on Monster Hunter
/// Wilds: the app resolved its mod folder as <c>reframework/autorun</c> and <c>get_game_shape</c>
/// reported <c>mods</c>. Every correction the manifest carries — the extensions A1 taught
/// registrations to pick up, the mod path A15 added — was invisible to an agent.</para>
///
/// <para>That is worse than the agent seeing less. An agent that sees a DIFFERENT answer reports it
/// with the same confidence, and a probe run this way already produced <c>exts=pak</c> for a game
/// the launcher reads as <c>lua,pak,dll</c>.</para>
///
/// <para>Read-only and best-effort, exactly like the app's path: no cache, an unsigned cache, a
/// schema too new or a failed gate all fall back to the embedded snapshot silently. A feed can never
/// stop the MCP answering.</para>
/// </summary>
public static class McpManifest
{
    /// <summary>Where the launcher writes the cache — must match
    /// <c>ModManager.App.Services.RemoteManifestSource.CacheDir</c>. Two spellings of one path is how
    /// the MCP would go on reading a cache nobody writes.</summary>
    public static string CacheDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModManagerBuilder");

    /// <summary>The version the feed's <c>minBinaryVersion</c> gate is checked against.
    ///
    /// <para>Core's, not this host's. The gate asks whether the binary understands the schema, and it
    /// is Core that parses it — so Core's version is the honest answer AND the one that makes the app
    /// and the MCP agree by construction rather than by coincidence. Presenting the MCP host's own
    /// version would let an unstamped dev build fail a gate the app passes, and re-open this exact
    /// bug silently.</para></summary>
    public static Version BinaryVersion =>
        typeof(EffectiveManifest).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Apply the cached feed. True when it was applied; false when the MCP is running on the
    /// embedded snapshot, which is a normal state and not an error.</summary>
    public static bool Apply() => RemoteManifestCache.ApplyCached(CacheDir, BinaryVersion);
}
