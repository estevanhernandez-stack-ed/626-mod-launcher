using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// One write path for turning a single mod on or off, shared by the app and the agent-access MCP.
///
/// <para>The read path was already shared (<see cref="ModListing.Resolve"/>, "the parity rule"), but
/// the write path was not: the app's view-model chose a lane per game, and the MCP tool sent every mod
/// through the scanner's folder move. On a direct-inject game that enable found no holding record where
/// it looked, returned an outcome nobody read, and the tool said "ok". Four real Elden Ring mods stayed
/// off while an agent reported them on. Each test here drives one lane through the shared router against
/// a real folder, and asserts on what the LISTING says afterwards, because that is what both callers
/// show the user.</para>
/// </summary>
public class ModToggleTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-toggle-");
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static Mod Row(GameEntry game, string name)
        => Assert.Single(ModListing.Resolve(game), m => m.Name == name);

    private static async Task Toggle(GameEntry game, string name, bool enabled)
        => await ModToggle.SetEnabledAsync(Scanner.GameContext(game), Row(game, name), enabled);

    [Fact]
    public async Task A_direct_inject_mod_turns_off_and_back_on()
    {
        var gameRoot = Path.Combine(_root, "ELDEN RING");
        var play = Path.Combine(gameRoot, "Game");
        Directory.CreateDirectory(Path.Combine(play, "reshade-shaders"));
        File.WriteAllText(Path.Combine(play, "reshade-shaders", "shader.fx"), "fx");
        File.WriteAllText(Path.Combine(play, "ReShadePreset.ini"), "preset");
        File.WriteAllText(Path.Combine(play, "eldenring.exe"), "game");
        var game = new GameEntry
        {
            Id = "er-toggle", GameName = "ER", Engine = "fromsoft", GameRoot = gameRoot,
            DataDir = Path.Combine(_root, "data"),
        };

        await Toggle(game, "ReShade", false);
        Assert.False(Row(game, "ReShade").Enabled);
        Assert.False(File.Exists(Path.Combine(play, "ReShadePreset.ini")));
        Assert.True(File.Exists(Path.Combine(play, "eldenring.exe")));

        await Toggle(game, "ReShade", true);
        Assert.True(Row(game, "ReShade").Enabled);
        Assert.Equal("preset", File.ReadAllText(Path.Combine(play, "ReShadePreset.ini")));
        Assert.Equal("fx", File.ReadAllText(Path.Combine(play, "reshade-shaders", "shader.fx")));
    }

    [Fact]
    public async Task A_loose_root_mod_turns_off_and_back_on()
    {
        var gameRoot = Path.Combine(_root, "game");
        Directory.CreateDirectory(Path.Combine(gameRoot, "reshade-shaders"));
        File.WriteAllText(Path.Combine(gameRoot, "reshade-shaders", "shader.fx"), "fx");
        File.WriteAllText(Path.Combine(gameRoot, "ReShade.ini"), "reshade");
        File.WriteAllText(Path.Combine(gameRoot, "ReShadePreset.ini"), "preset");
        File.WriteAllText(Path.Combine(gameRoot, "DS2.exe"), "GAME");
        var game = new GameEntry
        {
            Id = "death-stranding-2", GameName = "Death Stranding 2", Engine = "decima", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", ".") },
            DataDir = Path.Combine(_root, "data"),
        };

        await Toggle(game, "ReShade", false);
        Assert.False(Row(game, "ReShade").Enabled);
        Assert.False(File.Exists(Path.Combine(gameRoot, "ReShadePreset.ini")));
        Assert.True(File.Exists(Path.Combine(gameRoot, "DS2.exe")));

        await Toggle(game, "ReShade", true);
        Assert.True(Row(game, "ReShade").Enabled);
        Assert.Equal("preset", File.ReadAllText(Path.Combine(gameRoot, "ReShadePreset.ini")));
    }

    [Fact]
    public async Task A_mod_engine_2_mod_flips_in_its_config_and_the_config_is_backed_up_once()
    {
        var gameRoot = Path.Combine(_root, "ER-ME2");
        var me2 = Path.Combine(gameRoot, "ModEngine2");
        Directory.CreateDirectory(me2);
        var config = Path.Combine(me2, "config_eldenring.toml");
        const string original = """
[extension.mod_loader]
enabled = true

# Example only:
# mods = [
#    { enabled = true, name = "coolmod", path = "mod1" }
# ]
mods = [
    { enabled = true, name = "default", path = "mod" }
]
""";
        File.WriteAllText(config, original);
        var game = new GameEntry
        {
            Id = "er-me2", GameName = "ER", Engine = "fromsoft", GameRoot = gameRoot,
            ModEngineConfig = config, DataDir = Path.Combine(_root, "data"),
        };

        await Toggle(game, "default", false);
        Assert.False(Row(game, "default").Enabled);
        Assert.Equal(original, File.ReadAllText(config + ".626bak"));

        await Toggle(game, "default", true);
        Assert.True(Row(game, "default").Enabled);
        Assert.Equal(original, File.ReadAllText(config + ".626bak")); // taken once, never overwritten
        Assert.Contains("# mods = [", File.ReadAllText(config));      // the commented example is left alone
    }

    [Fact]
    public async Task A_proxy_loader_steps_aside_and_comes_back()
    {
        var gameRoot = Path.Combine(_root, "MonsterHunterWilds");
        Directory.CreateDirectory(Path.Combine(gameRoot, "scripts"));
        File.WriteAllText(Path.Combine(gameRoot, "MonsterHunterWilds.exe"), "game");
        File.WriteAllText(Path.Combine(gameRoot, "dinput8.dll"), "loader");
        var game = new GameEntry
        {
            Id = "mhw-toggle", Engine = "custom", GameRoot = gameRoot, DataDir = Path.Combine(_root, "data"),
            FileExtensions = new[] { "lua" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "scripts") },
        };
        Assert.Equal(ProxyLoaderRows.LocationTag, Row(game, "dinput8.dll").Location);

        await Toggle(game, "dinput8.dll", false);
        Assert.False(Row(game, "dinput8.dll").Enabled);
        Assert.False(File.Exists(Path.Combine(gameRoot, "dinput8.dll")));

        await Toggle(game, "dinput8.dll", true);
        Assert.True(Row(game, "dinput8.dll").Enabled);
        Assert.Equal("loader", File.ReadAllText(Path.Combine(gameRoot, "dinput8.dll")));
    }

    [Fact]
    public async Task A_folder_lane_mod_still_moves_to_holding_and_back()
    {
        var gameRoot = Path.Combine(_root, "custom");
        var scripts = Path.Combine(gameRoot, "scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "fastcraft.lua"), "-- mod");
        var game = new GameEntry
        {
            Id = "custom-toggle", Engine = "custom", GameRoot = gameRoot, DataDir = Path.Combine(_root, "data"),
            FileExtensions = new[] { "lua" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "scripts") },
        };
        var name = Assert.Single(ModListing.Resolve(game)).Name;

        await Toggle(game, name, false);
        Assert.False(Row(game, name).Enabled);
        Assert.False(File.Exists(Path.Combine(scripts, "fastcraft.lua")));

        await Toggle(game, name, true);
        Assert.True(Row(game, name).Enabled);
        Assert.Equal("-- mod", File.ReadAllText(Path.Combine(scripts, "fastcraft.lua")));
    }

    [Fact]
    public async Task Applied_is_false_when_the_listing_did_not_change()
    {
        // The check the MCP tool makes after every write, so a lane nobody routed can never again
        // report success. A name the listing does not have cannot have changed.
        var gameRoot = Path.Combine(_root, "ELDEN RING");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Game"));
        File.WriteAllText(Path.Combine(gameRoot, "Game", "ReShadePreset.ini"), "preset");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Game", "reshade-shaders"));
        var game = new GameEntry { Id = "er-applied", Engine = "fromsoft", GameRoot = gameRoot, DataDir = Path.Combine(_root, "data") };

        Assert.True(ModToggle.IsApplied(game, "ReShade", enabled: true));
        Assert.False(ModToggle.IsApplied(game, "ReShade", enabled: false));
        Assert.False(ModToggle.IsApplied(game, "no-such-mod", enabled: true));

        await Toggle(game, "ReShade", false);
        Assert.True(ModToggle.IsApplied(game, "ReShade", enabled: false));
    }
}
