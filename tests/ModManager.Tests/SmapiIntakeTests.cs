using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Wave 5 / A9. A SMAPI mod is a FOLDER holding a <c>manifest.json</c>, never a loose file — and
/// intake did not know that. <c>smapi</c> has no <c>loose-root</c> form and is not
/// direct-inject-backed, so a dropped archive reached the planner with the empty→<c>["pak"]</c>
/// substitution applied, and any <c>.pak</c> inside it classified as a mod and landed flat in
/// <c>Mods/</c>.
///
/// <para>Taking the substitution away was not the fix: an extension-less registration IS a pak game to
/// <c>FileRe</c> and <c>ModKey</c> (A7), so intake would start refusing mods the listing lane shows.
/// Recognise the shape instead.</para>
/// </summary>
public class SmapiIntakeTests
{
    [Fact]
    public void A_mod_folder_is_placed_whole()
    {
        var plan = SmapiIntake.Plan(new[]
        {
            "MyMod/manifest.json",
            "MyMod/MyMod.dll",
            "MyMod/i18n/default.json",
        });

        Assert.Equal("MyMod/manifest.json", plan["MyMod/manifest.json"]);
        Assert.Equal("MyMod/MyMod.dll", plan["MyMod/MyMod.dll"]);
        Assert.Equal("MyMod/i18n/default.json", plan["MyMod/i18n/default.json"]);
    }

    [Fact]
    public void A_version_wrapper_is_packaging_and_does_not_become_the_mod_folder()
    {
        // Nexus archives routinely wrap the mod in a folder named after the download. The folder BESIDE
        // the marker is the mod; the wrapper is not, and placing it would put Mods/MyMod-1.2.3/MyMod/…
        // on disk where SMAPI would never look.
        var plan = SmapiIntake.Plan(new[]
        {
            "MyMod-1.2.3/MyMod/manifest.json",
            "MyMod-1.2.3/MyMod/MyMod.dll",
        });

        Assert.Equal("MyMod/manifest.json", plan["MyMod-1.2.3/MyMod/manifest.json"]);
        Assert.Equal("MyMod/MyMod.dll", plan["MyMod-1.2.3/MyMod/MyMod.dll"]);
    }

    [Fact]
    public void A_stray_pak_beside_the_folder_is_not_placed_at_all()
    {
        // The entry. Before this, that pak classified as a mod and landed flat in Mods/.
        var plan = SmapiIntake.Plan(new[]
        {
            "MyMod/manifest.json",
            "MyMod/MyMod.dll",
            "ReadMe.txt",
            "SomethingElse.pak",
        });

        Assert.False(plan.ContainsKey("SomethingElse.pak"));
        Assert.False(plan.ContainsKey("ReadMe.txt"));
        Assert.Equal(2, plan.Count);
    }

    [Fact]
    public void Several_mods_in_one_archive_are_each_placed()
    {
        // Modpack archives are normal on Stardew.
        var plan = SmapiIntake.Plan(new[]
        {
            "Alpha/manifest.json", "Alpha/a.dll",
            "Beta/manifest.json", "Beta/b.dll",
        });

        Assert.Equal("Alpha/a.dll", plan["Alpha/a.dll"]);
        Assert.Equal("Beta/b.dll", plan["Beta/b.dll"]);
    }

    [Fact]
    public void A_marker_at_the_archive_root_names_the_folder_after_the_archive()
    {
        // The archive IS the mod folder. Its own name is the only name available, and SMAPI needs the
        // files inside a folder rather than loose in Mods/.
        var plan = SmapiIntake.Plan(new[] { "manifest.json", "MyMod.dll" }, fallbackName: "Cool Mod 1.4");

        Assert.Equal("Cool Mod 1.4/manifest.json", plan["manifest.json"]);
        Assert.Equal("Cool Mod 1.4/MyMod.dll", plan["MyMod.dll"]);
    }

    [Fact]
    public void A_root_marker_with_no_name_to_use_places_nothing_rather_than_guessing()
    {
        // Refusing beats inventing a folder name. Nothing placed is recoverable; a mod in a folder
        // SMAPI does not read looks installed and silently is not.
        Assert.Empty(SmapiIntake.Plan(new[] { "manifest.json", "MyMod.dll" }, fallbackName: null));
    }

    [Fact]
    public void A_nested_mod_belongs_to_the_nearest_marker()
    {
        var plan = SmapiIntake.Plan(new[]
        {
            "Outer/manifest.json",
            "Outer/Inner/manifest.json",
            "Outer/Inner/inner.dll",
        });

        Assert.Equal("Inner/inner.dll", plan["Outer/Inner/inner.dll"]);
        Assert.Equal("Outer/manifest.json", plan["Outer/manifest.json"]);
    }

    [Fact]
    public void Backslash_entries_are_understood()
    {
        var plan = SmapiIntake.Plan(new[] { @"MyMod\manifest.json", @"MyMod\MyMod.dll" });

        Assert.Equal("MyMod/MyMod.dll", plan[@"MyMod\MyMod.dll"]);
    }

    [Fact]
    public void An_archive_with_no_marker_is_left_entirely_alone()
    {
        // The guard that keeps this branch off every other engine's archives.
        Assert.False(SmapiIntake.LooksLikeSmapi(new[] { "mod.pak", "readme.txt" }));
        Assert.Empty(SmapiIntake.Plan(new[] { "mod.pak", "readme.txt" }, "whatever"));
    }

    [Fact]
    public void The_marker_is_recognised_wherever_it_sits()
    {
        Assert.True(SmapiIntake.LooksLikeSmapi(new[] { "MyMod/manifest.json" }));
        Assert.True(SmapiIntake.LooksLikeSmapi(new[] { @"a\b\MANIFEST.JSON" }));
        Assert.False(SmapiIntake.LooksLikeSmapi(new[] { "MyMod/manifest.json.bak" }));
        Assert.False(SmapiIntake.LooksLikeSmapi(null));
    }
}
