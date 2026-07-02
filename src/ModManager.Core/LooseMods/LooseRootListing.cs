using System.IO;
using System.Text.Json;

namespace ModManager.Core.LooseMods;

/// <summary>
/// Read-only listing for loose-root (decima) games: mods drop as loose files into the game root.
/// Structural twin of <see cref="DirectInjectListing"/> — enabled mods come from the catalog
/// (<see cref="DirectInject.Detect"/>, so ReShade et al. resolve via existing signatures) PLUS the
/// by-nature detector (<see cref="LooseModScan.Detect"/>, fed the catalog's entries as
/// <c>alreadyOwned</c> so nothing is double-claimed). Disabled mods are read from the
/// <c>&lt;dataDir&gt;/loose-disabled/*/__626mod.json</c> sidecars the shared holding machinery writes.
/// Toggling is NOT forked: it reuses <see cref="DirectInject.Disable"/> / <see cref="DirectInject.Enable"/>
/// with this holding root — byte-for-byte reversible, no-clobber, already proven.
///
/// A corrupt/missing sidecar is surfaced as disabled-but-unrestorable (a clear state, never guessed
/// entries) rather than silently dropped — the user can see the mod is held but the manifest is gone.
/// </summary>
public static class LooseRootListing
{
    /// <summary>The chip/location tag for the sentinel unrestorable row (corrupt/missing sidecar).</summary>
    public const string UnrestorableLocation = "loose-root-unrestorable";

    /// <summary>The location tag for a normal loose-root mod row.</summary>
    private const string LooseRootLocation = "loose-root";

    private const string MetaFile = "__626mod.json";

    /// <summary>The sidecar carries camelCase keys on disk (written by AtomicJson); read case-tolerant.</summary>
    private static readonly JsonSerializerOptions MetaJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>True for loose-root games (mods drop as loose files into the game root).</summary>
    public static bool Applies(GameEntry game) => game.Engine == "decima";

    /// <summary>True when a row is the disabled-but-unrestorable sentinel (corrupt/missing sidecar).</summary>
    public static bool IsUnrestorable(Mod row) => row.Location == UnrestorableLocation;

    public static IReadOnlyList<Mod> List(GameEntry game)
    {
        var folder = PlayFolder(game.GameRoot);
        return Enabled(folder).Select(d => Row(d, enabled: true))
            .Concat(ListDisabled(Holding(game)))
            .ToList();
    }

    /// <summary>Currently-enabled loose-root mods: catalog hits first (ReShade et al. via the shared
    /// signature catalog), then by-nature hits (ASI/addon/proxy) fed the catalog's owned entries as
    /// alreadyOwned so a file recognized by the catalog is never re-claimed by nature.</summary>
    public static IReadOnlyList<DirectInjectMod> Enabled(string? folder)
    {
        if (folder is null) return Array.Empty<DirectInjectMod>();
        var files = Names(folder, Directory.GetFiles);
        var dirs = Names(folder, Directory.GetDirectories);

        var catalog = DirectInject.Detect(files, dirs);
        var owned = new HashSet<string>(catalog.SelectMany(m => m.Entries), StringComparer.OrdinalIgnoreCase);
        var nature = LooseModScan.Detect(files, dirs, owned);

        return catalog.Concat(nature).ToList();
    }

    /// <summary>Loose-root games keep mods at the game root — no "Game" subfolder indirection.</summary>
    public static string? PlayFolder(string? gameRoot)
        => string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot) ? null : gameRoot;

    /// <summary>The holding root for disabled loose-root mods (shared move machinery lives here).</summary>
    public static string Holding(GameEntry game) => Path.Combine(Scanner.DataDirForGame(game), "loose-disabled");

    // Disabled rows read from the holding folder's sidecars. Unlike DirectInject.ListDisabled — which
    // silently drops an unreadable sidecar — we surface it as an unrestorable row so a held-but-orphaned
    // mod is visible, never guessing what entries it owned.
    private static IReadOnlyList<Mod> ListDisabled(string holdingRoot)
    {
        var rows = new List<Mod>();
        if (!Directory.Exists(holdingRoot)) return rows;
        foreach (var dir in Directory.GetDirectories(holdingRoot))
        {
            var meta = ReadMeta(dir);
            if (meta is not null)
            {
                rows.Add(Row(
                    new DirectInjectMod(meta.Name, meta.Kind, meta.Entries.FirstOrDefault() ?? meta.Name, meta.Entries),
                    enabled: false));
            }
            else
            {
                // Corrupt/missing sidecar: name from the holding folder, NO guessed entries.
                rows.Add(new Mod
                {
                    Name = Path.GetFileName(dir),
                    Base = Path.GetFileName(dir),
                    Class = "unrestorable",
                    Location = UnrestorableLocation,
                    Enabled = false,
                    Description = "Disabled, but its manifest (__626mod.json) is missing or unreadable — "
                        + "the launcher won't guess which files to restore. Move them back by hand from "
                        + dir + ", or re-disable a fresh copy.",
                    Files = new List<string>(),
                });
            }
        }
        return rows;
    }

    private static Mod Row(DirectInjectMod d, bool enabled) => new()
    {
        Name = d.Name,
        Base = d.Name,
        Class = d.Kind,                 // chip: plugin / shaders / loader / graphics / co-op / ...
        Location = LooseRootLocation,     // chip: loose-file mod in the game root
        Enabled = enabled,
        Description = "Detected: " + d.Evidence,
        Files = d.Entries.ToList(),
        // ASI-loader proxies (dinput8 et al.) are flagged loader by the by-nature detector; render distinguished.
        IsLoader = d.Kind == "loader",
    };

    private sealed class DisabledMeta
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public List<string> Entries { get; set; } = new();
    }

    private static DisabledMeta? ReadMeta(string dir)
    {
        try
        {
            var path = Path.Combine(dir, MetaFile);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<DisabledMeta>(File.ReadAllText(path), MetaJson);
        }
        catch { return null; }
    }

    private static IReadOnlyList<string> Names(string folder, Func<string, string[]> list)
    {
        try { return list(folder).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList(); }
        catch { return Array.Empty<string>(); }
    }
}
