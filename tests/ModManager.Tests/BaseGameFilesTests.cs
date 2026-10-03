using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Persistence;
using ModManager.Mcp;
using ModManager.Mcp.Tools;

namespace ModManager.Tests;

/// <summary>The rule itself (<see cref="BaseGameFiles"/>) and the flag the scan sets from it
/// (<see cref="Mod.IsBase"/>). The toggle paths that read the flag are in <see cref="BaseGameFilesToggleTests"/>.</summary>
[Collection("McpDataRoot")]
public class BaseGameFilesTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-baserule-");
    private readonly string _savedDataRoot = McpConfig.DataRoot;

    public void Dispose()
    {
        McpConfig.DataRoot = _savedDataRoot;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static readonly IReadOnlySet<string> NoCc = new HashSet<string>();

    [Theory]
    [InlineData("Skyrim.esm")]
    [InlineData("skyrim.ESM")]
    [InlineData("Update.esm")]
    [InlineData("Dragonborn.esm")]
    [InlineData("Fallout4.esm")]
    [InlineData("DLCworkshop03.esm")]
    [InlineData("DLCUltraHighResolution.esm")]
    [InlineData("Starfield.esm")]
    [InlineData("BlueprintShips-Starfield.esm")]
    [InlineData("ShatteredSpace.esm")]
    [InlineData("SFBGS003.esm")]
    [InlineData("SFBGS008.esm")]
    [InlineData("FalloutNV.esm")]
    [InlineData("GunRunnersArsenal.esm")]
    [InlineData("BrokenSteel.esm")]
    [InlineData("Oblivion.esm")]
    [InlineData("SkyrimVR.esm")]
    [InlineData("Fallout4_VR.esm")]
    [InlineData("_ResourcePack.esl")]
    [InlineData("_ResourcePack.bsa")]
    public void Base_masters_are_the_games(string name)
        => Assert.True(BaseGameFiles.IsBethesdaBaseFile(name, NoCc));

    [Theory]
    [InlineData("MyMod.esp")]
    [InlineData("SkyUI_SE.esp")]
    [InlineData("Skyrim.esp")]                  // a master's NAME with a plugin extension it never ships as
    [InlineData("Unofficial Skyrim Special Edition Patch.esp")]
    [InlineData("SFBGS003.esp")]
    [InlineData("ccBGSSSE001-Fish.esm")]       // Creation Club only counts when the game's list names it
    public void Mods_are_not(string name)
        => Assert.False(BaseGameFiles.IsBethesdaBaseFile(name, NoCc));

    [Theory]
    [InlineData("Skyrim - Textures0.bsa", true)]
    [InlineData("Skyrim - Animations.bsa", true)]
    [InlineData("Update.bsa", true)]
    [InlineData("Fallout4 - Meshes.ba2", true)]
    [InlineData("DLCRobot - Main.ba2", true)]
    [InlineData("SFBGS003 - Main.ba2", true)]
    [InlineData("Fallout - Meshes.bsa", false)] // only with a Fallout 3 / New Vegas master in the folder
    [InlineData("ccBGSSSE001-Fish.bsa", true)]
    [InlineData("MyMod.bsa", false)]
    [InlineData("SkyrimTextures.bsa", false)]  // no " - ": not Skyrim's
    [InlineData("MyMod - Textures.ba2", false)]
    public void Archives_belong_to_their_plugin(string name, bool isBase)
    {
        var cc = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ccBGSSSE001-Fish.esm" };
        Assert.Equal(isBase, BaseGameFiles.IsBethesdaBaseFile(name, cc));
    }

    [Fact]
    public void Creation_club_list_is_read_from_the_game_root_and_tolerates_blank_lines()
    {
        File.WriteAllText(Path.Combine(_root, "Skyrim.ccc"), "ccBGSSSE001-Fish.esm\r\n\r\n  ccQDRSSE001-SurvivalMode.esl  \r\n");
        File.WriteAllText(Path.Combine(_root, "Starfield.ccc"), "SFBGS00A_FakeContent.esm\n");
        var cc = BaseGameFiles.ReadCreationClub(_root);
        Assert.Contains("ccbgssse001-fish.esm", cc);
        Assert.Contains("ccQDRSSE001-SurvivalMode.esl", cc);
        Assert.Contains("SFBGS00A_FakeContent.esm", cc);
        Assert.Equal(3, cc.Count);
    }

    [Fact]
    public void A_missing_or_unreadable_creation_club_list_is_empty()
    {
        Assert.Empty(BaseGameFiles.ReadCreationClub(_root));
        Assert.Empty(BaseGameFiles.ReadCreationClub(null));
        Directory.CreateDirectory(Path.Combine(_root, "Fallout4.ccc"));   // reading a folder throws
        Assert.Empty(BaseGameFiles.ReadCreationClub(_root));
    }

    [Theory]
    [InlineData("pakchunk0-Windows.pak", true)]
    [InlineData("pakchunk0-Windows.utoc", true)]
    [InlineData("pakchunk0-WindowsNoEditor.ucas", true)]
    [InlineData("global.utoc", true)]
    [InlineData("global.ucas", true)]
    [InlineData("BetterHUD-WindowsNoEditor.pak", false)]   // Safe Clear round 6: a real mod, never hidden
    [InlineData("zz_Funner_P.pak", false)]
    [InlineData("pakchunk0-Windows.sig", false)]
    public void Unreal_base_archive_is_the_narrow_shipping_rule(string name, bool isBase)
        => Assert.Equal(isBase, BaseGameFiles.IsBaseGameArchive(name, 1024));

    [Fact]
    public void Unreal_size_ceiling_still_counts_as_base()
        => Assert.True(BaseGameFiles.IsBaseGameArchive("Whatever_P.pak", PakClassifier.ModSizeCeilingBytes));

    [Fact]
    public void Refusal_sentences()
    {
        Assert.Equal("Skyrim.esm is part of the game, so 626 won't turn it off.", BaseGameFiles.TurnOffRefusal("Skyrim.esm"));
        Assert.Equal("Skyrim.esm is part of the game, so 626 won't remove it.", BaseGameFiles.RemoveRefusal("Skyrim.esm"));
    }

    // ---------- the flag on rows ----------

    private GameEntry Skyrim()
    {
        var root = Path.Combine(_root, "Skyrim");
        var data = Path.Combine(root, "Data");
        Directory.CreateDirectory(data);
        foreach (var f in new[] { "Skyrim.esm", "Skyrim - Textures0.bsa", "ccBGSSSE001-Fish.esm", "MyMod.esp", "MyMod.bsa" })
            File.WriteAllText(Path.Combine(data, f), f);
        File.WriteAllText(Path.Combine(root, "Skyrim.ccc"), "ccBGSSSE001-Fish.esm\n");
        return new GameEntry
        {
            Id = "sky-rule", GameName = "Sky", Engine = "bethesda", GameRoot = root,
            FileExtensions = new[] { "esp", "esl", "esm", "bsa" }, GroupingRule = "filename_no_ext",
            DataDir = Path.Combine(_root, "data"),
            ModLocations = new[] { new ModLocation("mods", "Data", "Data") },
        };
    }

    [Fact]
    public void The_scan_marks_game_rows_and_leaves_mod_rows_alone()
    {
        var rows = ModListing.Resolve(Skyrim()).ToDictionary(m => m.Name);
        Assert.True(rows["Skyrim"].IsBase);
        Assert.True(rows["Skyrim - Textures0"].IsBase);
        Assert.True(rows["ccBGSSSE001-Fish"].IsBase);
        Assert.False(rows["MyMod"].IsBase);
    }

    [Fact]
    public void The_same_file_names_on_a_non_bethesda_game_are_not_marked()
    {
        var game = Skyrim();
        game.Engine = "custom";
        Assert.DoesNotContain(ModListing.Resolve(game), m => m.IsBase);
    }

    [Fact]
    public void A_held_game_file_lists_marked()
    {
        var game = Skyrim();
        var c = Scanner.GameContext(game);
        var held = Path.Combine(c.DisabledRoot, "Skyrim");
        Directory.CreateDirectory(held);
        File.Move(Path.Combine(game.GameRoot!, "Data", "Skyrim.esm"), Path.Combine(held, "Skyrim.esm"));
        File.WriteAllText(Path.Combine(held, "meta.json"), "{\"location\":\"mods\",\"hadOnServer\":{},\"isFolder\":false}");
        var row = ModListing.Resolve(game).Single(m => m.Name == "Skyrim");
        Assert.False(row.Enabled);
        Assert.True(row.IsBase);
    }

    [Fact]
    public void Uninstall_refusal_names_the_game_file()
    {
        var c = Scanner.GameContext(Skyrim());
        var row = ModListing.Resolve(c.Game).Single(m => m.Name == "Skyrim");
        var why = ModUninstall.Refusal(c, row);
        Assert.NotNull(why);
        Assert.Equal(UninstallBlock.GameFile, why!.Kind);
        Assert.Equal("Skyrim.esm is part of the game, so 626 won't remove it.", why.Message);
        Assert.Null(ModUninstall.Refusal(c, ModListing.Resolve(c.Game).Single(m => m.Name == "MyMod")));
    }

    [Fact]
    public void Safe_clear_names_a_bethesda_game_row_as_the_games_own_file()
    {
        var c = Scanner.GameContext(Skyrim());
        var plan = ModManager.Core.RestorePoints.RestorePointEngine.PlanVanillaRemainder(c,
            Array.Empty<ModManager.Core.RestorePoints.ClearSkip>());
        Assert.Contains(plan.LeftInPlace, n => n.Path == "Skyrim"
            && n.Reason == ModManager.Core.RestorePoints.RestorePointEngine.BaseFileRowNote);
        Assert.DoesNotContain(plan.LeftInPlace, n => n.Path == "MyMod"
            && n.Reason == ModManager.Core.RestorePoints.RestorePointEngine.BaseFileRowNote);
    }

    [Fact]
    public void Error_remedy_shows_the_refusal_as_it_is()
        => Assert.Equal("Skyrim.esm is part of the game, so 626 won't turn it off.",
            ErrorRemedy.Describe(new BaseGameFileException("Skyrim.esm", remove: false)));

    [Fact]
    public async Task Mcp_list_mods_exposes_the_flag()
    {
        McpConfig.DataRoot = Path.Combine(_root, "appdata");
        var game = Skyrim();
        RegistryStore.Save(McpConfig.DataRoot, Registry.UpsertGame(Registry.EmptyRegistry(), game));
        var json = JsonSerializer.SerializeToElement(await ModTools.ListMods(game.Id!));
        var mods = json.GetProperty("mods").EnumerateArray().ToDictionary(m => m.GetProperty("name").GetString()!);
        Assert.True(mods["Skyrim"].GetProperty("isBase").GetBoolean());
        Assert.False(mods["MyMod"].GetProperty("isBase").GetBoolean());
    }

    [Fact]
    public async Task Mcp_refusal_is_coded_and_logged()
    {
        McpConfig.DataRoot = Path.Combine(_root, "appdata");
        var game = Skyrim();
        RegistryStore.Save(McpConfig.DataRoot, Registry.UpsertGame(Registry.EmptyRegistry(), game));
        var json = JsonSerializer.SerializeToElement(await WriteTools.SetModEnabled(game.Id!, "Skyrim", enabled: false));
        Assert.Equal("game_file", json.GetProperty("refusal").GetString());
        var log = ModManager.Core.Agent.AgentAudit.Read(Scanner.DataDirForGame(game));
        Assert.Contains(log, e => e.Result == "game_file");
    }

    [Fact]
    public void Fallout_base_archives_count_only_beside_a_fallout_3_or_new_vegas_master()
    {
        Assert.True(BaseGameFiles.IsBethesdaBaseFile("Fallout - Meshes.bsa", NoCc, falloutArchives: true));
        Assert.False(BaseGameFiles.IsBethesdaBaseFile("Fallout - Meshes.bsa", NoCc, falloutArchives: false));
    }

    [Fact]
    public void Mcp_uninstall_refusal_is_coded_game_file()
    {
        McpConfig.DataRoot = Path.Combine(_root, "appdata");
        var game = Skyrim();
        RegistryStore.Save(McpConfig.DataRoot, Registry.UpsertGame(Registry.EmptyRegistry(), game));
        var json = JsonSerializer.SerializeToElement(GameWriteTools.UninstallMod(game.Id!, "Skyrim", confirm: true));
        Assert.Equal("game_file", json.GetProperty("refusal").GetString());
        Assert.Equal("Skyrim.esm is part of the game, so 626 won't remove it.", json.GetProperty("detail").GetString());
    }

    [Fact]
    public void List_mods_description_mentions_the_flag()
    {
        var attr = typeof(ModTools).GetMethod(nameof(ModTools.ListMods))!
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
            .Cast<System.ComponentModel.DescriptionAttribute>().Single();
        Assert.Contains("isBase", attr.Description);
    }

    [Fact]
    public async Task Load_order_leaves_the_games_own_files_out()
    {
        var c = Scanner.GameContext(Skyrim());
        var order = await Scanner.GetLoadOrderAsync(c);
        Assert.Contains("MyMod", order);
        Assert.DoesNotContain("Skyrim", order);
        Assert.DoesNotContain("ccBGSSSE001-Fish", order);
    }
}
