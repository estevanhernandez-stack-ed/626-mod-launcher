using ModManager.Core;

namespace ModManager.Tests;

public class VortexTakeoverScanTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "vtx-scan-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    // A ue-pak game whose single folders-location is a UE4SS Mods dir holding a UE4SS manifest, one mod
    // folder, and a Vortex marker. Returns (game, modsFolderAbs).
    private (GameEntry game, string modsFolderAbs) Setup()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var mods = Path.Combine(gameRoot, "R5", "Binaries", "Win64", "ue4ss", "Mods");
        var modFolder = Path.Combine(mods, "ShantiesMod");
        Directory.CreateDirectory(Path.Combine(modFolder, "Scripts"));
        File.WriteAllText(Path.Combine(modFolder, "Scripts", "main.lua"), "x"); // makes ShantiesMod a UE4SS mod folder
        File.WriteAllText(Path.Combine(mods, "mods.txt"), "ShantiesMod : 1");    // UE4SS manifest -> IsUe4ssFolder true
        File.WriteAllText(Path.Combine(mods, "vortex.deployment.x.json"), "{}"); // Vortex marker -> owned

        var game = new GameEntry
        {
            Id = "windrose", GameName = "Windrose", Engine = "ue-pak",
            GameRoot = gameRoot, GroupingRule = "by_folder",
            FileExtensions = new[] { "pak" },
            DataDir = Path.Combine(_tmp, "data"),
            ModLocations = new[] { new ModLocation("mods", "Mods", "R5/Binaries/Win64/ue4ss/Mods") }, // forward slashes ON PURPOSE
        };
        Directory.CreateDirectory(game.DataDir!);
        return (game, mods);
    }

    [Fact]
    public async Task Owned_until_taken_over_then_managed()
    {
        var (game, _) = Setup();
        var ctx = Scanner.GameContext(game);
        var before = (await Scanner.BuildModListAsync(ctx)).First(m => m.Name == "ShantiesMod");
        Assert.True(before.ReadOnly);   // Vortex-owned -> read-only

        VortexTakeover.TakeOver(game.DataDir!, ctx.GameRoot, ctx.Locations[0].Abs); // take over the location's abs path
        var after = (await Scanner.BuildModListAsync(Scanner.GameContext(game))).First(m => m.Name == "ShantiesMod");
        Assert.False(after.ReadOnly);   // taken over (marker archived) -> ours to manage
    }

    [Fact]
    public async Task Stays_managed_after_a_vortex_redeploy_because_the_folder_is_taken_over()
    {
        var (game, modsFolder) = Setup();
        var ctx = Scanner.GameContext(game);
        VortexTakeover.TakeOver(game.DataDir!, ctx.GameRoot, ctx.Locations[0].Abs); // marker archived, folder recorded

        // Simulate Vortex re-deploying: a fresh marker reappears in the live folder.
        File.WriteAllText(Path.Combine(modsFolder, "vortex.deployment.x.json"), "{}");

        var after = (await Scanner.BuildModListAsync(Scanner.GameContext(game))).First(m => m.Name == "ShantiesMod");
        // WITHOUT set-threading the reappeared marker would make this read-only again; WITH it, the
        // folder is in the taken-over set -> ReDeployed -> Conductor (loader) -> still manageable.
        Assert.False(after.ReadOnly);
    }
}
