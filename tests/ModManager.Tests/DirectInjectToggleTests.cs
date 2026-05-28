using ModManager.Core;

namespace ModManager.Tests;

// Reversible enable/disable of direct-inject mods: disabling MOVES the mod's owned files/folders
// to a holding dir (never deletes), enabling moves them back. Game files are never touched.
public class DirectInjectToggleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mmb-di-" + Guid.NewGuid().ToString("N"));
    private string Play => Path.Combine(_root, "Game");
    private string Holding => Path.Combine(_root, "holding");

    public DirectInjectToggleTests()
    {
        Directory.CreateDirectory(Play);
        // A direct-inject mod (ReShade): a shaders folder + a preset, plus a vanilla game file.
        Directory.CreateDirectory(Path.Combine(Play, "reshade-shaders"));
        File.WriteAllText(Path.Combine(Play, "reshade-shaders", "shader.fx"), "fx");
        File.WriteAllText(Path.Combine(Play, "ReShadePreset.ini"), "preset");
        File.WriteAllText(Path.Combine(Play, "eldenring.exe"), "game"); // must never move
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static string[] Files(string dir) => Directory.GetFiles(dir).Select(Path.GetFileName).ToArray()!;
    private static string[] Dirs(string dir) => Directory.GetDirectories(dir).Select(Path.GetFileName).ToArray()!;

    [Fact]
    public void Detect_reports_all_owned_entries_present()
    {
        var reshade = DirectInject.Detect(Files(Play), Dirs(Play)).Single(m => m.Name == "ReShade");
        Assert.Contains("reshade-shaders", reshade.Entries);
        Assert.Contains("ReShadePreset.ini", reshade.Entries);
        Assert.DoesNotContain("eldenring.exe", reshade.Entries);
    }

    [Fact]
    public void Disable_moves_owned_entries_to_holding_and_leaves_game_files()
    {
        var reshade = DirectInject.Detect(Files(Play), Dirs(Play)).Single(m => m.Name == "ReShade");
        DirectInject.Disable(Play, Holding, reshade);

        Assert.False(Directory.Exists(Path.Combine(Play, "reshade-shaders")));
        Assert.False(File.Exists(Path.Combine(Play, "ReShadePreset.ini")));
        Assert.True(File.Exists(Path.Combine(Play, "eldenring.exe"))); // game file untouched
        // Detect no longer sees ReShade as enabled; it shows as disabled instead.
        Assert.DoesNotContain(DirectInject.Detect(Files(Play), Dirs(Play)), m => m.Name == "ReShade");
        Assert.Contains(DirectInject.ListDisabled(Holding), m => m.Name == "ReShade" && m.Kind == "graphics");
    }

    [Fact]
    public void Enable_restores_owned_entries_exactly()
    {
        var reshade = DirectInject.Detect(Files(Play), Dirs(Play)).Single(m => m.Name == "ReShade");
        DirectInject.Disable(Play, Holding, reshade);
        DirectInject.Enable(Play, Holding, "ReShade");

        Assert.True(File.Exists(Path.Combine(Play, "reshade-shaders", "shader.fx")));
        Assert.True(File.Exists(Path.Combine(Play, "ReShadePreset.ini")));
        Assert.Empty(DirectInject.ListDisabled(Holding)); // holding cleared
        Assert.Contains(DirectInject.Detect(Files(Play), Dirs(Play)), m => m.Name == "ReShade");
    }

    [Fact]
    public void Disable_surfaces_IOException_when_game_holds_the_file_open()
    {
        // MoveAny now routes through SafeMove, which verifies the move before deleting the source.
        // A file held open by the game (FileShare.None) must surface as IOException rather than
        // silently succeeding and leaving the source in an inconsistent state.
        var reshade = DirectInject.Detect(Files(Play), Dirs(Play)).Single(m => m.Name == "ReShade");

        var lockedPath = Path.Combine(Play, "ReShadePreset.ini");
        using var hold = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Disable wraps the IOException in an InvalidOperationException with user-facing context
        // ("is the game running?"). The inner exception is the raw IOException from SafeMove.
        var ex = Assert.Throws<InvalidOperationException>(() => DirectInject.Disable(Play, Holding, reshade));
        Assert.IsType<IOException>(ex.InnerException);

        // Game folder is unchanged — no partial disable.
        Assert.True(File.Exists(lockedPath));
    }
}
