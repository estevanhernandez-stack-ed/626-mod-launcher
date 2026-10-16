using System.Diagnostics;
using ModManager.Core;
using Xunit.Abstractions;

namespace ModManager.Tests;

/// <summary>
/// A real Fallout 4 load order is hundreds of plugins and a thousand archives, and the mod list is rebuilt on
/// every toggle. Archive ownership once re-contested every archive for every plugin (plugins^2 x archives:
/// 259 ms became 8.4 s at 600 plugins / 1,200 archives). It is settled once per folder now.
/// </summary>
public class BethesdaArchiveScalingTests : IDisposable
{
    private const int Plugins = 600;
    private readonly ITestOutputHelper _out;
    private readonly string _root = TestSupport.TempDir("mmb-ba2-scale-");

    public BethesdaArchiveScalingTests(ITestOutputHelper output) { _out = output; }
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private GameContext Fixture()
    {
        var gameRoot = Path.Combine(_root, "Fallout 4");
        var data = Path.Combine(gameRoot, "Data");
        Directory.CreateDirectory(data);
        for (var i = 0; i < Plugins; i++)
        {
            var name = $"Plugin Number {i:D4}";
            File.WriteAllText(Path.Combine(data, name + ".esp"), "p");
            File.WriteAllText(Path.Combine(data, name + " - Main.ba2"), "a");
            File.WriteAllText(Path.Combine(data, name + " - Textures.ba2"), "a");
        }
        var game = new GameEntry
        {
            Id = "fo4-scale", GameName = "FO4 scale", Engine = "bethesda", GameRoot = gameRoot,
            FileExtensions = new[] { "esp", "esl", "esm", "bsa" }, GroupingRule = "filename_no_ext",
            DataDir = Path.Combine(_root, "data"),
            ModLocations = new[] { new ModLocation("mods", "Data", "Data") },
        };
        Directory.CreateDirectory(game.DataDir!);
        return Scanner.GameContext(game);
    }

    [Fact]
    public async Task A_warm_scan_of_600_plugins_and_1200_archives_stays_fast_and_correct()
    {
        var c = Fixture();
        await Scanner.BuildModListAsync(c); // warm

        var sw = Stopwatch.StartNew();
        var mods = await Scanner.BuildModListAsync(c);
        sw.Stop();
        _out.WriteLine($"warm BuildModList, {Plugins} plugins / {Plugins * 2} archives: {sw.ElapsedMilliseconds} ms");

        Assert.Equal(Plugins, mods.Count);
        Assert.All(mods, m => Assert.Equal(3, m.Files.Count));
        // Generous: the quadratic version took 8 s here; the settled-once version takes a fraction of a second.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"warm scan took {sw.ElapsedMilliseconds} ms");
    }
}
