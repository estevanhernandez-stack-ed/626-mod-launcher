using System.IO.Compression;
using ModManager.Core;
using ModManager.Core.LooseMods;

namespace ModManager.Tests.LooseMods;

// Intake for loose-root (decima) games: a dropped loose mod installs into the GAME ROOT through the
// DirectInject plan/execute machinery (validate-then-extract, path-safe, no-clobber) behind a
// recognition gate — only drops that would produce a LooseRootListing row (catalog + by-nature)
// are placed; anything unrecognized is refused for the root, never silently dumped.
public class LooseRootIntakeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mmb-lri-" + Guid.NewGuid().ToString("N"));
    private string GameRoot => Path.Combine(_root, "game");
    private string Drops => Path.Combine(_root, "drops");

    public LooseRootIntakeTests()
    {
        Directory.CreateDirectory(GameRoot);
        Directory.CreateDirectory(Drops);
        // Game-owned files — intake must never disturb them.
        File.WriteAllText(Path.Combine(GameRoot, "DS2.exe"), "GAME");
        File.WriteAllText(Path.Combine(GameRoot, "DeathStranding2Core.dll"), "CORE");
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private GameEntry Game() => new()
    {
        Id = "death-stranding-2", GameName = "Death Stranding 2", Engine = "decima",
        GameRoot = GameRoot,
        ModLocations = new[] { new ModLocation("mods", "mods", ".") },
        DataDir = Path.Combine(_root, "data"),
    };

    private GameContext Ctx() => Scanner.GameContext(Game());

    private string MakeDrop(string name, string content = "x")
    {
        var p = Path.Combine(Drops, name);
        File.WriteAllText(p, content);
        return p;
    }

    private string MakeZip(string name, params (string entry, string content)[] entries)
    {
        var path = Path.Combine(Drops, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
            using (var w = new StreamWriter(zip.CreateEntry(entry).Open())) w.Write(content);
        return path;
    }

    private static readonly HashSet<string> NoReplacements = new();

    // ---- (1) loose .asi drop -> planned to the game root, listed after execute ------------------

    [Fact]
    public void Loose_asi_drop_plans_to_game_root_and_lists_after_execute()
    {
        var ctx = Ctx();
        var drop = MakeDrop("Zipliner.asi", "ZIP");

        var plan = Scanner.PlanIntake(new[] { drop }, ctx);
        var item = Assert.Single(plan.ToAdd);
        Assert.Equal("Zipliner.asi", item.RelPath);   // lands at the root, no subfolder indirection
        Assert.Empty(plan.Collisions);
        Assert.Empty(plan.Unsafe);

        var r = Scanner.ExecuteIntake(plan, NoReplacements, ctx);
        Assert.Contains("Zipliner.asi", r.Added);
        Assert.Equal("ZIP", File.ReadAllText(Path.Combine(GameRoot, "Zipliner.asi")));

        // The installed mod appears in the loose-root listing — the intake feeds the same view.
        var rows = LooseRootListing.List(Game());
        Assert.Contains(rows, m => m.Name == "Zipliner" && m.Enabled && m.Files.Contains("Zipliner.asi"));
    }

    // ---- (2) archive with a recognized set (Mod.asi + Mod.ini) -> both planned + placed ---------

    [Fact]
    public void Archive_with_asi_and_ini_plans_and_places_both()
    {
        var ctx = Ctx();
        var zip = MakeZip("DollmanMute.zip", ("DollmanMute.asi", "DOLL"), ("DollmanMute.ini", "doll-cfg"));

        var plan = Scanner.PlanIntake(new[] { zip }, ctx);
        Assert.Equal(2, plan.ToAdd.Count);
        Assert.Contains(plan.ToAdd, i => i.RelPath == "DollmanMute.asi");
        Assert.Contains(plan.ToAdd, i => i.RelPath == "DollmanMute.ini");
        Assert.Empty(plan.Unsafe);

        var r = Scanner.ExecuteIntake(plan, NoReplacements, ctx);
        Assert.Equal(2, r.Added.Count);
        Assert.Equal("DOLL", File.ReadAllText(Path.Combine(GameRoot, "DollmanMute.asi")));
        Assert.Equal("doll-cfg", File.ReadAllText(Path.Combine(GameRoot, "DollmanMute.ini")));

        var row = LooseRootListing.List(Game()).Single(m => m.Name == "DollmanMute");
        Assert.Contains("DollmanMute.asi", row.Files);
        Assert.Contains("DollmanMute.ini", row.Files);
    }

    // A wrapped archive flattens its single top folder — contents land at the root, not nested.
    [Fact]
    public void Wrapped_archive_flattens_into_the_game_root()
    {
        var ctx = Ctx();
        var zip = MakeZip("Zipliner_v1.1.zip", ("Zipliner_v1.1/Zipliner.asi", "ZIP"));

        var plan = Scanner.PlanIntake(new[] { zip }, ctx);
        var item = Assert.Single(plan.ToAdd);
        Assert.Equal("Zipliner.asi", item.RelPath);

        Scanner.ExecuteIntake(plan, NoReplacements, ctx);
        Assert.True(File.Exists(Path.Combine(GameRoot, "Zipliner.asi")));
        Assert.False(Directory.Exists(Path.Combine(GameRoot, "Zipliner_v1.1")));
    }

    // ---- (3) unrecognized drops are refused for the root — never silently placed ----------------

    [Fact]
    public void Unrecognized_loose_files_are_refused_and_root_untouched()
    {
        var ctx = Ctx();
        var readme = MakeDrop("readme.txt", "hello");
        var randomDll = MakeDrop("Random.dll", "not a proxy name");
        var before = Directory.GetFileSystemEntries(GameRoot, "*", SearchOption.AllDirectories);

        var plan = Scanner.PlanIntake(new[] { readme, randomDll }, ctx);
        Assert.Empty(plan.ToAdd);
        Assert.Empty(plan.Collisions);
        Assert.Equal(2, plan.Unsafe.Count);
        Assert.All(plan.Unsafe, s => Assert.Contains("not a recognized loose mod", s.Reason));

        var r = Scanner.ExecuteIntake(plan, NoReplacements, ctx);
        Assert.Empty(r.Added);
        Assert.Equal(2, r.Skipped.Count);   // refusal surfaces in the result, not silence
        Assert.Equal(before, Directory.GetFileSystemEntries(GameRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Archive_without_a_recognized_set_is_refused_and_root_untouched()
    {
        var ctx = Ctx();
        var zip = MakeZip("docs.zip", ("readme.txt", "hello"), ("notes/changelog.txt", "v1"));
        var before = Directory.GetFileSystemEntries(GameRoot, "*", SearchOption.AllDirectories);

        var plan = Scanner.PlanIntake(new[] { zip }, ctx);
        Assert.Empty(plan.ToAdd);
        var skip = Assert.Single(plan.Unsafe);
        Assert.Contains("not a recognized loose mod", skip.Reason);

        Scanner.ExecuteIntake(plan, NoReplacements, ctx);
        Assert.Equal(before, Directory.GetFileSystemEntries(GameRoot, "*", SearchOption.AllDirectories));
    }

    // ---- (4) unsafe ..-path archive entries are refused before any write ------------------------

    [Fact]
    public void Traversal_entry_is_refused_before_any_write_but_safe_siblings_install()
    {
        var ctx = Ctx();
        var zip = MakeZip("evil.zip", ("Mod.asi", "ok"), ("../evil.asi", "pwn"));

        var plan = Scanner.PlanIntake(new[] { zip }, ctx);
        var item = Assert.Single(plan.ToAdd);
        Assert.Equal("Mod.asi", item.RelPath);
        Assert.Contains(plan.Unsafe, s => s.Reason.Contains("unsafe", StringComparison.OrdinalIgnoreCase));

        var r = Scanner.ExecuteIntake(plan, NoReplacements, ctx);
        Assert.True(File.Exists(Path.Combine(GameRoot, "Mod.asi")));
        Assert.False(File.Exists(Path.Combine(_root, "evil.asi")));   // never escaped the game root
        Assert.Contains(r.Skipped, s => s.Reason.Contains("unsafe", StringComparison.OrdinalIgnoreCase));
    }

    // ---- no-clobber: an existing root file needs an explicit replace decision -------------------

    [Fact]
    public void Existing_file_collides_and_is_never_silently_overwritten()
    {
        var ctx = Ctx();
        File.WriteAllText(Path.Combine(GameRoot, "Zipliner.asi"), "OLD");
        var drop = MakeDrop("Zipliner.asi", "NEW");

        var plan = Scanner.PlanIntake(new[] { drop }, ctx);
        Assert.Empty(plan.ToAdd);
        var col = Assert.Single(plan.Collisions);
        Assert.Equal("Zipliner.asi", col.RelPath);

        // No replace decision -> kept existing, byte-for-byte.
        var r = Scanner.ExecuteIntake(plan, NoReplacements, ctx);
        Assert.Empty(r.Updated);
        Assert.Equal("OLD", File.ReadAllText(Path.Combine(GameRoot, "Zipliner.asi")));
        Assert.Contains(r.Skipped, s => s.Reason.Contains("kept existing"));
    }
}
