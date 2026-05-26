using ModManager.Core;

namespace ModManager.Tests;

public class BuiltinMetadataTests
{
    [Fact]
    public async Task BuildModList_tags_ue4ss_builtin_folders()
    {
        var root = TestSupport.TempDir("bi-");
        var mods = Path.Combine(root, "ue4ss", "Mods");
        Directory.CreateDirectory(Path.Combine(mods, "ConsoleEnablerMod"));
        Directory.CreateDirectory(Path.Combine(mods, "PetBoarPlus"));
        File.WriteAllText(Path.Combine(mods, "mods.txt"), "ConsoleEnablerMod : 1\n");
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            ModLocations = new[] { new ModLocation("ue4ss", "UE4SS", "ue4ss/Mods") { Form = "folders" } },
        });
        var list = await Scanner.BuildModListAsync(c);
        Assert.True(list.First(m => m.Name == "ConsoleEnablerMod").Builtin);
        Assert.False(list.First(m => m.Name == "PetBoarPlus").Builtin);
    }

    [Fact]
    public void MergeMetadata_fills_builtin_description_when_no_real_metadata()
    {
        var mod = new Mod { Name = "ConsoleEnablerMod", Base = "ConsoleEnablerMod", Builtin = true };
        var merged = Metadata.MergeMetadata(new[] { mod }, null).First();
        Assert.Equal("Console Enabler", merged.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(merged.Description));
        Assert.StartsWith("https://", merged.Source);
    }

    [Fact]
    public void MergeMetadata_real_metadata_wins_over_builtin_catalog()
    {
        var mod = new Mod { Name = "ConsoleEnablerMod", Base = "ConsoleEnablerMod", Builtin = true };
        var map = new Dictionary<string, ModMeta> { ["ConsoleEnablerMod"] = new ModMeta { Title = "Real Title", Description = "real" } };
        var merged = Metadata.MergeMetadata(new[] { mod }, map).First();
        Assert.Equal("Real Title", merged.DisplayName);   // CF/Nexus wins
        Assert.Equal("real", merged.Description);
    }
}
