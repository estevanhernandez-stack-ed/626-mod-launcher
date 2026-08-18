using ModManager.Core;
using ModManager.Core.Manifest;
using ModManager.Mcp;

namespace ModManager.Tests.Manifest;

/// <summary>
/// A18. The MCP read the embedded snapshot while the app read embedded + the cached remote feed, so
/// the two answered the same question differently — the app resolved Monster Hunter Wilds's mod
/// folder as <c>reframework/autorun</c> and <c>get_game_shape</c> reported <c>mods</c>. Every
/// correction the manifest carries was invisible to an agent, and an agent that sees a DIFFERENT
/// answer states it with the same confidence as a right one.
/// </summary>
public class McpFeedParityTests
{
    [Fact]
    public void The_gate_is_checked_against_Core_s_version_so_both_hosts_agree()
    {
        // The gate asks whether the binary understands the schema, and Core parses it. Checking the
        // MCP host's own version instead would let an unstamped dev build fail a gate the app passes
        // and silently fall back to the embedded snapshot — this bug, returning quietly.
        Assert.Equal(typeof(EffectiveManifest).Assembly.GetName().Version, McpManifest.BinaryVersion);
    }

    [Fact]
    public void Apply_is_false_and_harmless_when_no_cache_exists()
    {
        // Running on the embedded snapshot is a normal state, not an error. A feed must never be able
        // to stop the MCP answering.
        var previous = McpManifest.CacheDir;
        try
        {
            McpManifest.CacheDir = Path.Combine(Path.GetTempPath(), "a18-no-cache-" + Guid.NewGuid().ToString("N"));
            Assert.False(McpManifest.Apply());
            Assert.NotEmpty(EffectiveManifest.Current.Games); // still answering, off the embedded snapshot
        }
        finally { McpManifest.CacheDir = previous; }
    }

    [Fact]
    public void Apply_survives_a_corrupt_cache()
    {
        var previous = McpManifest.CacheDir;
        var dir = Path.Combine(Path.GetTempPath(), "a18-bad-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "games-manifest.json"), "{ not json");
            File.WriteAllText(Path.Combine(dir, "games-manifest.json.sig"), "not a signature");
            McpManifest.CacheDir = dir;

            Assert.False(McpManifest.Apply());
            Assert.NotEmpty(EffectiveManifest.Current.Games);
        }
        finally
        {
            McpManifest.CacheDir = previous;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void The_cache_dir_matches_where_the_launcher_writes_it()
    {
        // Two spellings of one path is how the MCP would go on reading a cache nobody writes.
        //
        // This CANNOT reference RemoteManifestSource: it lives in the WinUI app, which a headless test
        // must not load. So the assertion pins the MCP to the literal the app also spells out, and the
        // comment carries the obligation. Say so plainly rather than write a comparison that looks
        // like verification and is really the same expression twice.
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModManagerBuilder"),
            McpManifest.CacheDir);
        Assert.EndsWith("ModManagerBuilder", McpManifest.CacheDir);
    }
}
