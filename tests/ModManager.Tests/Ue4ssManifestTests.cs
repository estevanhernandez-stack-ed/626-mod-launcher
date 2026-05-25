using ModManager.Core;

namespace ModManager.Tests;

// UE4SS effective-enabled: (enabled.txt present) OR (manifest entry == true). enabled.txt overrides
// a mods.txt/json ":0". Absent from manifest + no enabled.txt = disabled.
public class Ue4ssManifestTests
{
    private static string ModsDir()
    {
        var d = TestSupport.TempDir("ue4ss-");
        Directory.CreateDirectory(d);
        return d;
    }
    private static void Folder(string modsDir, string name) => Directory.CreateDirectory(Path.Combine(modsDir, name));
    private static void EnabledTxt(string modsDir, string name) => File.WriteAllText(Path.Combine(modsDir, name, "enabled.txt"), "");

    [Fact]
    public void IsUe4ssFolder_true_when_a_manifest_exists()
    {
        var d = ModsDir();
        Assert.False(Ue4ssManifest.IsUe4ssFolder(d));
        File.WriteAllText(Path.Combine(d, "mods.txt"), "");
        Assert.True(Ue4ssManifest.IsUe4ssFolder(d));
    }

    [Fact]
    public void IsEnabled_reads_mods_txt_flag()
    {
        var d = ModsDir(); Folder(d, "Foo"); Folder(d, "Bar");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "Foo : 1\nBar : 0\n");
        Assert.True(Ue4ssManifest.IsEnabled(d, "Foo"));
        Assert.False(Ue4ssManifest.IsEnabled(d, "Bar"));
    }

    [Fact]
    public void IsEnabled_reads_mods_json_flag()
    {
        var d = ModsDir(); Folder(d, "Foo");
        File.WriteAllText(Path.Combine(d, "mods.json"),
            "[{\"mod_name\":\"Foo\",\"mod_enabled\":true}]");
        Assert.True(Ue4ssManifest.IsEnabled(d, "Foo"));
    }

    [Fact]
    public void IsEnabled_enabled_txt_overrides_a_disabled_manifest_entry()
    {
        var d = ModsDir(); Folder(d, "Foo");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "Foo : 0\n");
        EnabledTxt(d, "Foo");
        Assert.True(Ue4ssManifest.IsEnabled(d, "Foo")); // enabled.txt wins
    }

    [Fact]
    public void IsEnabled_enabled_txt_enables_a_mod_absent_from_the_manifest()
    {
        var d = ModsDir(); Folder(d, "PetBoarPlus");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "Other : 1\n");
        EnabledTxt(d, "PetBoarPlus");
        Assert.True(Ue4ssManifest.IsEnabled(d, "PetBoarPlus"));
    }

    [Fact]
    public void IsEnabled_absent_from_manifest_and_no_enabled_txt_is_disabled()
    {
        var d = ModsDir(); Folder(d, "Ghost");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "Other : 1\n");
        Assert.False(Ue4ssManifest.IsEnabled(d, "Ghost"));
    }

    [Fact]
    public void IsEnabled_ignores_comments_and_blank_lines()
    {
        var d = ModsDir(); Folder(d, "Keybinds");
        File.WriteAllText(Path.Combine(d, "mods.txt"),
            "; a comment\n\n; Built-in keybinds, do not move up!\nKeybinds : 1\n");
        Assert.True(Ue4ssManifest.IsEnabled(d, "Keybinds"));
    }

    [Fact]
    public void SetEnabled_false_flips_mods_txt_and_removes_enabled_txt()
    {
        var d = ModsDir(); Folder(d, "Foo");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "Foo : 1\nKeybinds : 1\n");
        File.WriteAllText(Path.Combine(d, "Foo", "enabled.txt"), "");

        Ue4ssManifest.SetEnabled(d, "Foo", false);

        Assert.False(Ue4ssManifest.IsEnabled(d, "Foo"));
        Assert.False(File.Exists(Path.Combine(d, "Foo", "enabled.txt"))); // removed, else it overrides
        Assert.Contains("Foo : 0", File.ReadAllText(Path.Combine(d, "mods.txt")));
        Assert.Contains("Keybinds : 1", File.ReadAllText(Path.Combine(d, "mods.txt"))); // untouched
    }

    [Fact]
    public void SetEnabled_true_flips_mods_txt()
    {
        var d = ModsDir(); Folder(d, "Foo");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "Foo : 0\n");
        Ue4ssManifest.SetEnabled(d, "Foo", true);
        Assert.True(Ue4ssManifest.IsEnabled(d, "Foo"));
        Assert.Contains("Foo : 1", File.ReadAllText(Path.Combine(d, "mods.txt")));
    }

    [Fact]
    public void SetEnabled_updates_mods_json_when_present()
    {
        var d = ModsDir(); Folder(d, "Foo");
        File.WriteAllText(Path.Combine(d, "mods.json"),
            "[{\"mod_name\":\"Foo\",\"mod_enabled\":true}]");
        Ue4ssManifest.SetEnabled(d, "Foo", false);
        Assert.False(Ue4ssManifest.IsEnabled(d, "Foo"));
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(d, "mods.json")));
        var foo = doc.RootElement.EnumerateArray().First(e => e.GetProperty("mod_name").GetString() == "Foo");
        Assert.False(foo.GetProperty("mod_enabled").GetBoolean());
    }

    [Fact]
    public void SetEnabled_keeps_mods_txt_and_json_in_lockstep()
    {
        var d = ModsDir(); Folder(d, "Foo");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "Foo : 1\n");
        File.WriteAllText(Path.Combine(d, "mods.json"), "[{\"mod_name\":\"Foo\",\"mod_enabled\":true}]");
        Ue4ssManifest.SetEnabled(d, "Foo", false);
        Assert.Contains("Foo : 0", File.ReadAllText(Path.Combine(d, "mods.txt")));
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(d, "mods.json")));
        Assert.False(doc.RootElement.EnumerateArray().First().GetProperty("mod_enabled").GetBoolean());
    }

    [Fact]
    public void SetEnabled_true_adds_a_missing_manifest_entry()
    {
        var d = ModsDir(); Folder(d, "New");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "; Built-in keybinds, do not move up!\nKeybinds : 1\n");
        Ue4ssManifest.SetEnabled(d, "New", true);
        Assert.True(Ue4ssManifest.IsEnabled(d, "New"));
        var txt = File.ReadAllText(Path.Combine(d, "mods.txt"));
        Assert.Contains("New : 1", txt);
        Assert.Contains("Keybinds : 1", txt); // keybinds section preserved
    }
}
