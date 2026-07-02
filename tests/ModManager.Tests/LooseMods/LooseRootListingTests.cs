using ModManager.Core;
using ModManager.Core.LooseMods;

namespace ModManager.Tests.LooseMods;

// Loose-root mods (decima / Death Stranding 2 shape): mods drop as loose files into the game root.
// The listing merges catalog hits (ReShade et al. via DirectInject.Detect) with by-nature hits
// (LooseModScan.Detect) so nothing is double-claimed, and never lists game files. Toggling reuses
// DirectInject.Disable/Enable with the loose-disabled holding root — byte-for-byte reversible.
public class LooseRootListingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mmb-loose-" + Guid.NewGuid().ToString("N"));
    private string GameRoot => Path.Combine(_root, "game");

    public LooseRootListingTests()
    {
        Directory.CreateDirectory(GameRoot);
        // The DS2 fixture — a real-shaped loose-root install.
        // Nature "plugin": a bare ASI, and an ASI grouping its same-stem .ini.
        File.WriteAllText(Path.Combine(GameRoot, "Zipliner_v1.1.asi"), "ZIP");
        File.WriteAllText(Path.Combine(GameRoot, "DollmanMute.asi"), "DOLL");
        File.WriteAllText(Path.Combine(GameRoot, "DollmanMute.ini"), "doll-cfg");
        // Catalog "ReShade": preset + ini + shaders folder.
        File.WriteAllText(Path.Combine(GameRoot, "ReShade.ini"), "reshade");
        File.WriteAllText(Path.Combine(GameRoot, "ReShadePreset.ini"), "preset");
        Directory.CreateDirectory(Path.Combine(GameRoot, "reshade-shaders"));
        File.WriteAllText(Path.Combine(GameRoot, "reshade-shaders", "shader.fx"), "fx");
        // Nature "shaders": a ReShade addon grouping its same-stem .ini.
        File.WriteAllText(Path.Combine(GameRoot, "ShaderToggler.addon64"), "TOGGLE");
        File.WriteAllText(Path.Combine(GameRoot, "ShaderToggler.ini"), "toggle-cfg");
        // Game files — must NEVER be listed or moved.
        File.WriteAllText(Path.Combine(GameRoot, "DS2.exe"), "GAME");
        File.WriteAllText(Path.Combine(GameRoot, "DeathStranding2Core.dll"), "CORE");
        // A standalone INI (generic) — never claimed by nature, not a catalog hit.
        File.WriteAllText(Path.Combine(GameRoot, "OptiScaler.ini"), "opti");
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private GameEntry Game() => new()
    {
        Id = "death-stranding-2", GameName = "Death Stranding 2", Engine = "decima",
        GameRoot = GameRoot,
        ModLocations = new[] { new ModLocation("mods", "mods", ".") },
        DataDir = Path.Combine(_root, "data"),
    };

    // ---- (1) decima preset ----------------------------------------------------------------------

    [Fact]
    public void Decima_preset_exists_with_root_modpath_and_no_extensions()
    {
        Assert.True(EnginePresets.Presets.ContainsKey("decima"));
        var p = EnginePresets.Presets["decima"];
        Assert.Equal(".", p.ModPath);
        Assert.Empty(p.FileExtensions);
        Assert.False(string.IsNullOrEmpty(p.GroupingRule)); // Every_preset_has_the_required_shape guard
    }

    // ---- (2) GameContext resolves the loose-root form -------------------------------------------

    [Fact]
    public void GameContext_for_decima_game_yields_loose_root_form_at_game_root()
    {
        var ctx = Scanner.GameContext(Game());
        var loc = ctx.Locations[0];
        Assert.Equal("loose-root", loc.Form);
        // modPath "." resolves to the game root (Path.Combine leaves a trailing "\." — normalize to compare).
        Assert.Equal(Path.GetFullPath(GameRoot), Path.GetFullPath(loc.Abs));
    }

    [Fact]
    public void ModListing_routes_loose_root_games_to_loose_root_listing()
    {
        // The dispatch: a loose-root-form game lists through LooseRootListing (catalog + nature),
        // not the scanner's pak-file path.
        var rows = ModListing.Resolve(Game());
        Assert.Contains(rows, r => r.Name == "ReShade");
        Assert.Contains(rows, r => r.Name == "Zipliner_v1.1"); // a nature ASI plugin
    }

    // ---- (3) listing: catalog + nature, no double-claim, game files never listed ----------------

    [Fact]
    public void List_returns_catalog_and_nature_mods_without_double_claiming()
    {
        var rows = LooseRootListing.List(Game());

        // Catalog hit.
        var reshade = rows.Single(r => r.Name == "ReShade");
        Assert.True(reshade.Enabled);
        Assert.Contains("reshade-shaders", reshade.Files);
        Assert.Contains("ReShade.ini", reshade.Files);
        Assert.Contains("ReShadePreset.ini", reshade.Files);

        // Nature hits.
        Assert.Contains(rows, r => r.Name == "Zipliner_v1.1");
        var dollman = rows.Single(r => r.Name == "DollmanMute");
        Assert.Contains("DollmanMute.asi", dollman.Files);
        Assert.Contains("DollmanMute.ini", dollman.Files);
        var toggler = rows.Single(r => r.Name == "ShaderToggler");
        Assert.Contains("ShaderToggler.addon64", toggler.Files);
        Assert.Contains("ShaderToggler.ini", toggler.Files);

        // Game files are never listed.
        Assert.DoesNotContain(rows, r => r.Files.Contains("DS2.exe"));
        Assert.DoesNotContain(rows, r => r.Files.Contains("DeathStranding2Core.dll"));
        // A standalone generic INI is never claimed.
        Assert.DoesNotContain(rows, r => r.Files.Contains("OptiScaler.ini"));

        // No entry is claimed by two rows.
        var allFiles = rows.SelectMany(r => r.Files).ToList();
        Assert.Equal(allFiles.Count, allFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ---- (4) disable -> enable round-trip through loose-disabled restores byte-identical --------

    [Fact]
    public void Disable_then_enable_restores_byte_identical_content()
    {
        var game = Game();
        var holding = LooseRootListing.Holding(game);
        var play = LooseRootListing.PlayFolder(game.GameRoot)!;

        var toggler = LooseRootListing.Enabled(play).Single(m => m.Name == "ShaderToggler");
        var addonBytes = File.ReadAllBytes(Path.Combine(play, "ShaderToggler.addon64"));
        var iniBytes = File.ReadAllBytes(Path.Combine(play, "ShaderToggler.ini"));

        DirectInject.Disable(play, holding, toggler);
        Assert.False(File.Exists(Path.Combine(play, "ShaderToggler.addon64")));
        Assert.False(File.Exists(Path.Combine(play, "ShaderToggler.ini")));
        Assert.True(File.Exists(Path.Combine(play, "DS2.exe"))); // game file untouched
        Assert.Contains(LooseRootListing.List(game), r => r.Name == "ShaderToggler" && !r.Enabled);

        DirectInject.Enable(play, holding, "ShaderToggler");
        Assert.Equal(addonBytes, File.ReadAllBytes(Path.Combine(play, "ShaderToggler.addon64")));
        Assert.Equal(iniBytes, File.ReadAllBytes(Path.Combine(play, "ShaderToggler.ini")));
        Assert.Empty(DirectInject.ListDisabled(holding)); // holding cleared
        Assert.Contains(LooseRootListing.List(game), r => r.Name == "ShaderToggler" && r.Enabled);
    }

    // ---- (5) corrupt sidecar -> disabled-unrestorable, never guessed ----------------------------

    [Fact]
    public void Corrupt_sidecar_lists_mod_as_disabled_unrestorable_without_guessing()
    {
        var game = Game();
        var holding = LooseRootListing.Holding(game);

        // Seed a holding folder with a corrupt __626mod.json (as if a disable wrote garbage / was truncated).
        var corruptDir = Path.Combine(holding, "brokenmod");
        Directory.CreateDirectory(corruptDir);
        File.WriteAllText(Path.Combine(corruptDir, "brokenmod.asi"), "held");
        File.WriteAllText(Path.Combine(corruptDir, "__626mod.json"), "{ this is not valid json");

        var rows = LooseRootListing.List(game);
        var broken = rows.Single(r => LooseRootListing.IsUnrestorable(r));
        Assert.False(broken.Enabled);
        Assert.Empty(broken.Files); // never guessed
    }

    // The sentinel's description is load-bearing (it tells the user where to move files back from
    // by hand) — it must survive the metadata merge in the shared listing path, not just the raw
    // LooseRootListing read.
    [Fact]
    public void Sentinel_description_survives_metadata_merge_through_ModListing_Resolve()
    {
        var game = Game();
        var corruptDir = Path.Combine(LooseRootListing.Holding(game), "brokenmod");
        Directory.CreateDirectory(corruptDir);
        File.WriteAllText(Path.Combine(corruptDir, "brokenmod.asi"), "held");
        File.WriteAllText(Path.Combine(corruptDir, "__626mod.json"), "{ this is not valid json");

        var rows = ModListing.Resolve(game); // the merged path the App actually renders
        var broken = rows.Single(r => LooseRootListing.IsUnrestorable(r));
        Assert.False(string.IsNullOrEmpty(broken.Description));
        Assert.Contains("__626mod.json", broken.Description);
        Assert.Contains(corruptDir, broken.Description); // the move-back-by-hand dir survives
    }

    // ---- (6) ownership: a Vortex/MO2-owned root is read-only until takeover ---------------------

    [Fact]
    public void Vortex_marker_at_game_root_stamps_rows_read_only_until_takeover()
    {
        var game = Game();

        // Without a marker: rows are ours to manage.
        var before = LooseRootListing.List(game);
        Assert.NotEmpty(before);
        Assert.All(before, r => { Assert.False(r.ReadOnly); Assert.Null(r.Managed); });

        // Vortex marker at the root -> every row (enabled AND disabled) reads ReadOnly + Managed,
        // same stamp the scanner world puts on rows from an owned location.
        var holdingDir = Path.Combine(LooseRootListing.Holding(game), "HeldMod");
        Directory.CreateDirectory(holdingDir);
        File.WriteAllText(Path.Combine(holdingDir, "HeldMod.asi"), "held");
        File.WriteAllText(Path.Combine(holdingDir, "__626mod.json"),
            """{ "name": "HeldMod", "kind": "plugin", "entries": ["HeldMod.asi"] }""");
        File.WriteAllText(Path.Combine(GameRoot, "__folder_managed_by_vortex"), "");

        var rows = LooseRootListing.List(game);
        Assert.Contains(rows, r => r.Enabled);
        Assert.Contains(rows, r => !r.Enabled); // the held row carries the stamp too
        Assert.All(rows, r => { Assert.True(r.ReadOnly); Assert.Equal("vortex", r.Managed); });
    }

    // ---- (7) ONE loose-root predicate: form-derived, no second engine-string check --------------

    [Fact]
    public void Explicit_loose_root_form_routes_to_loose_root_listing_without_engine_tag()
    {
        var game = new GameEntry
        {
            Id = "mystery-loose", GameName = "Mystery Loose Game",   // Engine deliberately null
            GameRoot = GameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", ".") { Form = "loose-root" } },
            DataDir = Path.Combine(_root, "data-mystery"),
        };

        // The single predicate is form-derived — engine tag not required.
        Assert.True(LooseRootListing.Applies(game));
        // And the shared listing dispatch consults the same predicate: loose-root rows, not scanner.
        var rows = ModListing.Resolve(game);
        Assert.Contains(rows, r => r.Name == "ReShade" && r.Location == LooseRootListing.LooseRootLocation);
    }
}
