using ModManager.Core;

namespace ModManager.Tests;

// techiew's Elden Ring Mod Loader (dinput8.dll) runs each DLL in a `mods\` folder as its own mod.
// We surface those individually (name, kind, owned entries) instead of one opaque "mod loader" row.
public class DirectInjectLoaderTests
{
    private static string M(string name) => Path.Combine("mods", name);

    [Fact]
    public void Each_dll_in_mods_becomes_its_own_mod()
    {
        var mods = DirectInject.DetectLoaderMods(
            new[] { "UltrawideFix.dll", "AdjustTheFov.dll", "IncreaseAnimationDistance.dll", "readme.txt" },
            new[] { "UltrawideFix", "AdjustTheFov" });

        Assert.Equal(3, mods.Count); // readme.txt ignored — only DLLs are mods

        var uw = mods.Single(m => m.Name == "Ultrawide Fix"); // camelCase prettified
        Assert.Equal("display", uw.Kind);
        Assert.Contains(M("UltrawideFix.dll"), uw.Entries);
        Assert.Contains(M("UltrawideFix"), uw.Entries); // its config folder travels with it
    }

    [Fact]
    public void A_mod_without_a_config_folder_owns_only_its_dll()
    {
        var iad = DirectInject.DetectLoaderMods(new[] { "IncreaseAnimationDistance.dll" }, Array.Empty<string>())
            .Single();
        Assert.Equal(new[] { M("IncreaseAnimationDistance.dll") }, iad.Entries);
    }

    [Theory]
    [InlineData("AdjustTheFov.dll", "display")]
    [InlineData("RemoveVignette.dll", "graphics")]
    [InlineData("RemoveChromaticAberration.dll", "graphics")]
    [InlineData("SkipTheIntro.dll", "tweak")]
    public void Kind_is_inferred_from_the_name(string dll, string kind)
        => Assert.Equal(kind, DirectInject.DetectLoaderMods(new[] { dll }, Array.Empty<string>()).Single().Kind);
}
