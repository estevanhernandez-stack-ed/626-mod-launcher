using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// A held copy of a mod is the user's files. Turning a mod on or off must never delete one.
///
/// <para>Found 2026-09-14 while fixing the agent toggle: turn ReShade off, install a fresh ReShade,
/// turn the old one back on, and <see cref="DirectInject.Enable"/> skipped every entry because the
/// names were taken, then cleared the holding folder with <c>Directory.Delete(recursive: true)</c> —
/// the old copy gone for good. <see cref="DirectInject.Disable"/> had the mirror image: disabling the
/// fresh copy while the old one was held hit the name collision, and its rollback deleted the holding
/// folder, old copy included. Both break the reversibility law in the one place it is most explicit:
/// no delete in a toggle path.</para>
/// </summary>
public class DirectInjectHeldFilesTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-held-");
    private string Play => Path.Combine(_root, "Game");
    private string Holding => Path.Combine(_root, "holding");
    private string HeldDir => Path.Combine(Holding, EnginePresets.Slugify("ReShade"));

    public DirectInjectHeldFilesTests()
    {
        Directory.CreateDirectory(Path.Combine(Play, "reshade-shaders"));
        File.WriteAllText(Path.Combine(Play, "reshade-shaders", "shader.fx"), "OLD-FX");
        File.WriteAllText(Path.Combine(Play, "ReShadePreset.ini"), "OLD-PRESET");
        File.WriteAllText(Path.Combine(Play, "eldenring.exe"), "game");
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private DirectInjectMod LiveReShade()
        => DirectInject.Detect(
                Directory.GetFiles(Play).Select(Path.GetFileName).ToArray()!,
                Directory.GetDirectories(Play).Select(Path.GetFileName).ToArray()!)
            .Single(m => m.Name == "ReShade");

    private void HoldTheOldCopyAndInstallAFreshOne()
    {
        DirectInject.Disable(Play, Holding, LiveReShade());
        Directory.CreateDirectory(Path.Combine(Play, "reshade-shaders"));
        File.WriteAllText(Path.Combine(Play, "reshade-shaders", "shader.fx"), "NEW-FX");
        File.WriteAllText(Path.Combine(Play, "ReShadePreset.ini"), "NEW-PRESET");
    }

    /// <summary>Every byte of both copies, wherever it now sits. The invariant the law states.</summary>
    private void AssertBothCopiesSurvive()
    {
        var everything = Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .Select(File.ReadAllText).ToList();
        Assert.Contains("OLD-PRESET", everything);
        Assert.Contains("OLD-FX", everything);
        Assert.Contains("NEW-PRESET", everything);
        Assert.Contains("NEW-FX", everything);
    }

    [Fact]
    public void Turning_the_held_copy_on_over_a_fresh_install_deletes_nothing()
    {
        HoldTheOldCopyAndInstallAFreshOne();

        try { DirectInject.Enable(Play, Holding, "ReShade"); } catch (InvalidOperationException) { }

        AssertBothCopiesSurvive();
        Assert.Equal("NEW-PRESET", File.ReadAllText(Path.Combine(Play, "ReShadePreset.ini"))); // live copy never clobbered
    }

    [Fact]
    public void Turning_a_fresh_install_off_while_an_old_copy_is_held_deletes_nothing()
    {
        HoldTheOldCopyAndInstallAFreshOne();

        try { DirectInject.Disable(Play, Holding, LiveReShade()); } catch (InvalidOperationException) { }

        AssertBothCopiesSurvive();
    }

    // ---- the owner's call, 2026-09-14: refuse and change nothing ----------------------------------

    [Fact]
    public void Turning_the_held_copy_on_over_a_fresh_install_refuses_and_moves_nothing()
    {
        HoldTheOldCopyAndInstallAFreshOne();

        var e = Assert.Throws<HeldCopyCollisionException>(() => DirectInject.Enable(Play, Holding, "ReShade"));

        Assert.Contains("ReShadePreset.ini", e.Message);
        Assert.Contains("Nothing was moved", e.Message);
        // The old copy is still held exactly as it was, and still listed as a turned-off mod.
        Assert.Equal("OLD-PRESET", File.ReadAllText(Path.Combine(HeldDir, "ReShadePreset.ini")));
        Assert.Equal("OLD-FX", File.ReadAllText(Path.Combine(HeldDir, "reshade-shaders", "shader.fx")));
        Assert.Contains(DirectInject.ListDisabled(Holding), m => m.Name == "ReShade");
        // The fresh copy is untouched.
        Assert.Equal("NEW-PRESET", File.ReadAllText(Path.Combine(Play, "ReShadePreset.ini")));
        Assert.Equal("NEW-FX", File.ReadAllText(Path.Combine(Play, "reshade-shaders", "shader.fx")));
    }

    [Fact]
    public void Turning_a_fresh_install_off_while_an_old_copy_is_held_refuses_and_moves_nothing()
    {
        HoldTheOldCopyAndInstallAFreshOne();

        var e = Assert.Throws<HeldCopyCollisionException>(() => DirectInject.Disable(Play, Holding, LiveReShade()));

        Assert.Contains("Nothing was moved", e.Message);
        Assert.Equal("NEW-PRESET", File.ReadAllText(Path.Combine(Play, "ReShadePreset.ini")));
        Assert.Equal("OLD-PRESET", File.ReadAllText(Path.Combine(HeldDir, "ReShadePreset.ini")));
        Assert.Contains(DirectInject.ListDisabled(Holding), m => m.Name == "ReShade");
    }

    [Fact]
    public void A_restore_that_fails_partway_puts_back_what_it_moved()
    {
        DirectInject.Disable(Play, Holding, LiveReShade());
        var meta = DirectInject.ListDisabled(Holding).Single(m => m.Name == "ReShade");
        // Lock the LAST entry the restore will move, so everything before it has already moved.
        var last = meta.Entries.Last();
        var lockPath = Path.Combine(HeldDir, last);
        var lockFile = File.Exists(lockPath) ? lockPath : Directory.GetFiles(lockPath, "*", SearchOption.AllDirectories).First();

        using (new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<InvalidOperationException>(() => DirectInject.Enable(Play, Holding, "ReShade"));
        }

        // Still turned off, with every entry back in holding and nothing half-restored in the game folder.
        Assert.Contains(DirectInject.ListDisabled(Holding), m => m.Name == "ReShade");
        foreach (var entry in meta.Entries)
        {
            Assert.True(File.Exists(Path.Combine(HeldDir, entry)) || Directory.Exists(Path.Combine(HeldDir, entry)), $"{entry} left holding");
            Assert.False(File.Exists(Path.Combine(Play, entry)) || Directory.Exists(Path.Combine(Play, entry)), $"{entry} half-restored");
        }
        Assert.True(File.Exists(Path.Combine(Play, "eldenring.exe")));
    }

    [Fact]
    public void A_clean_round_trip_still_leaves_no_holding_folder_behind()
    {
        DirectInject.Disable(Play, Holding, LiveReShade());
        DirectInject.Enable(Play, Holding, "ReShade");

        Assert.Equal("OLD-PRESET", File.ReadAllText(Path.Combine(Play, "ReShadePreset.ini")));
        Assert.False(Directory.Exists(HeldDir));
        Assert.Empty(DirectInject.ListDisabled(Holding));
    }

    [Fact]
    public void Enabling_never_deletes_a_file_it_did_not_put_in_holding()
    {
        DirectInject.Disable(Play, Holding, LiveReShade());
        File.WriteAllText(Path.Combine(HeldDir, "notes-the-user-left.txt"), "mine");

        DirectInject.Enable(Play, Holding, "ReShade");

        Assert.Equal("OLD-PRESET", File.ReadAllText(Path.Combine(Play, "ReShadePreset.ini")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(HeldDir, "notes-the-user-left.txt")));
    }

    [Fact]
    public void A_disable_that_fails_partway_leaves_the_mod_live_with_no_stray_record()
    {
        var mod = LiveReShade();
        // Lock the LAST entry, so the ones before it have already moved when the move fails.
        var last = Path.Combine(Play, mod.Entries.Last());
        var lockFile = File.Exists(last) ? last : Directory.GetFiles(last, "*", SearchOption.AllDirectories).First();

        using (new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<InvalidOperationException>(() => DirectInject.Disable(Play, Holding, mod));
        }

        // Everything is back in the game folder, no record claims otherwise, and the holding folder this
        // call created is gone.
        Assert.Equal("OLD-PRESET", File.ReadAllText(Path.Combine(Play, "ReShadePreset.ini")));
        Assert.Equal("OLD-FX", File.ReadAllText(Path.Combine(Play, "reshade-shaders", "shader.fx")));
        Assert.Empty(DirectInject.ListDisabled(Holding));
        Assert.False(Directory.Exists(HeldDir));
    }

    [Fact]
    public void A_stale_record_with_nothing_behind_it_does_not_block_turning_the_mod_off()
    {
        // Enable deletes its record best-effort. If that delete failed, the record outlives the files it
        // described, and must not make the next Disable report a held copy that does not exist.
        Directory.CreateDirectory(HeldDir);
        File.WriteAllText(Path.Combine(HeldDir, "__626mod.json"), """{"name":"ReShade","kind":"graphics","entries":["ReShadePreset.ini"]}""");

        DirectInject.Disable(Play, Holding, LiveReShade());

        Assert.False(File.Exists(Path.Combine(Play, "ReShadePreset.ini")));
        Assert.Equal("OLD-PRESET", File.ReadAllText(Path.Combine(HeldDir, "ReShadePreset.ini")));
        Assert.Contains(DirectInject.ListDisabled(Holding), m => m.Name == "ReShade" && m.Entries.Count >= 2);
    }

    [Fact]
    public void The_refusal_reaches_the_user_as_written()
    {
        // The generic remedy appends "try again after a Refresh", which would send someone in a circle:
        // refreshing changes nothing when the fix is choosing which copy to keep.
        var e = new HeldCopyCollisionException("Couldn't turn \"ReShade\" back on: \"ReShadePreset.ini\" is already in the game folder. Nothing was moved.");

        var said = ErrorRemedy.Describe(e);

        Assert.Equal(e.Message, said);
        Assert.DoesNotContain("Refresh", said);
    }
}
