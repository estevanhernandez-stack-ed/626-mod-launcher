using ModManager.Core;

namespace ModManager.Tests;

// BepInEx: a plugin is enabled when its file ends in .dll, disabled when renamed to .dll.disabled.
// Scan surfaces both; SetEnabled flips by rename (reversible, no move/delete).
public class BepInExPluginsTests
{
    private static string Dir()
    {
        var d = TestSupport.TempDir("bep-");
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Scan_lists_enabled_and_disabled_plugins_keyed_by_base_name()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "CoolMod.dll"), "x");
        File.WriteAllText(Path.Combine(d, "OffMod.dll.disabled"), "x");
        var scan = BepInExPlugins.Scan(d).OrderBy(p => p.Name).ToList();
        Assert.Equal(2, scan.Count);
        Assert.Equal(("CoolMod", "CoolMod.dll", true), (scan[0].Name, scan[0].File, scan[0].Enabled));
        Assert.Equal(("OffMod", "OffMod.dll.disabled", false), (scan[1].Name, scan[1].File, scan[1].Enabled));
    }

    [Fact]
    public void Scan_prefers_enabled_when_both_forms_exist()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "Dup.dll"), "x");
        File.WriteAllText(Path.Combine(d, "Dup.dll.disabled"), "x");
        var p = Assert.Single(BepInExPlugins.Scan(d));
        Assert.True(p.Enabled);
    }

    [Fact]
    public void Scan_ignores_non_plugin_files() // e.g. .json/.txt sidecars
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "Note.txt"), "x");
        Assert.Empty(BepInExPlugins.Scan(d));
    }

    [Fact]
    public void IsEnabled_reflects_the_present_form()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "A.dll"), "x");
        File.WriteAllText(Path.Combine(d, "B.dll.disabled"), "x");
        Assert.True(BepInExPlugins.IsEnabled(d, "A"));
        Assert.False(BepInExPlugins.IsEnabled(d, "B"));
        Assert.False(BepInExPlugins.IsEnabled(d, "Ghost"));
    }

    [Fact]
    public void SetEnabled_false_renames_dll_to_disabled_no_data_lost()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "A.dll"), "PAYLOAD");
        BepInExPlugins.SetEnabled(d, "A", false);
        Assert.False(File.Exists(Path.Combine(d, "A.dll")));
        Assert.True(File.Exists(Path.Combine(d, "A.dll.disabled")));
        Assert.Equal("PAYLOAD", File.ReadAllText(Path.Combine(d, "A.dll.disabled"))); // content preserved
    }

    [Fact]
    public void SetEnabled_true_renames_disabled_back_to_dll()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "A.dll.disabled"), "x");
        BepInExPlugins.SetEnabled(d, "A", true);
        Assert.True(File.Exists(Path.Combine(d, "A.dll")));
        Assert.False(File.Exists(Path.Combine(d, "A.dll.disabled")));
    }

    [Fact]
    public void SetEnabled_is_idempotent_when_already_in_target_state()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "A.dll"), "x");
        BepInExPlugins.SetEnabled(d, "A", true); // already enabled — no throw, no change
        Assert.True(File.Exists(Path.Combine(d, "A.dll")));
    }
}
