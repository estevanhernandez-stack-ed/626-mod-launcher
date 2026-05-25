using ModManager.Core;

namespace ModManager.Tests;

// saveModPath + saveModForbidden thread from the agent profile -> draft -> GameInput -> GameEntry so
// each game declares its own save-mod install layout + the folders the installer must never write.
public class SaveModThreadingTests
{
    [Fact]
    public void BuildGameEntry_carries_save_mod_path_and_forbidden()
    {
        var input = new GameInput
        {
            Name = "Windrose", Engine = "ue-pak",
            SaveModPath = "RocksDB/{version}/Worlds",
            SaveModForbidden = new[] { "RocksDB_v2", "RocksDB_v2_Backups" },
        };
        var entry = EnginePresets.BuildGameEntry(input, existingIds: null);
        Assert.Equal("RocksDB/{version}/Worlds", entry.SaveModPath);
        Assert.Contains("RocksDB_v2", entry.SaveModForbidden);
    }

    [Fact]
    public void BuildGameEntry_leaves_save_mod_fields_default_when_unset()
    {
        var entry = EnginePresets.BuildGameEntry(new GameInput { Name = "X", Engine = "ue-pak" }, null);
        Assert.Null(entry.SaveModPath);
        Assert.Empty(entry.SaveModForbidden);
    }

    [Fact]
    public void GameProfileImport_parses_save_mod_fields()
    {
        var json = """
        {
          "name": "Windrose", "engine": "ue-pak",
          "saveRoot": "AppData", "saveSubPath": "Windrose/Saved",
          "saveModPath": "RocksDB/{version}/Worlds",
          "saveModForbidden": ["RocksDB_v2", "RocksDB_v2_Backups"]
        }
        """;
        var result = GameProfileImport.Load(json);
        Assert.Empty(result.Errors);
        Assert.Equal("RocksDB/{version}/Worlds", result.Draft!.SaveModPath);
        Assert.Contains("RocksDB_v2_Backups", result.Draft!.SaveModForbidden!);
    }
}
