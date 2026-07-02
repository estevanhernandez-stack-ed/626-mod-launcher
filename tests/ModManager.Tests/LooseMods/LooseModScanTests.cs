using ModManager.Core;
using ModManager.Core.LooseMods;

namespace ModManager.Tests.LooseMods;

public class LooseModScanTests
{
    // The actual Death Stranding 2 game root observed live (2026-07-01).
    private static readonly string[] Ds2Files =
    {
        "Zipliner_v1.1.asi", "DollmanMute.asi", "DollmanMute.ini",
        "DeathStranding2Fix.asi", "DeathStranding2Fix.ini",
        "ReShade.ini", "ReShadePreset.ini", "ReShade.log",
        "ShaderToggler.addon64", "ShaderToggler.ini", "DeathStranding2UI.addon64",
        "OptiScaler.ini", "Chiral Clarity.ini", "NaturalDS2.ini", "SDR+.ini",
        "DS2.exe", "DeathStranding2Core.dll", "HashDB.bin",
        "PsPcSdkRuntimeInstaller.msi", "DS2nexusfullgame.CT", "CLAUDE.md",
        "dinput8.dll",
    };
    private static readonly string[] Ds2Dirs = { "LocalCacheWinGame", "reshade-shaders" };

    private static IReadOnlyList<DirectInjectMod> Scan(ISet<string>? owned = null)
        => LooseModScan.Detect(Ds2Files, Ds2Dirs, owned);

    [Fact]
    public void Asi_plugins_detect_one_mod_each_with_same_stem_config_grouped()
    {
        var mods = Scan();
        var dollman = Assert.Single(mods, m => m.Name.Contains("DollmanMute", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("plugin", dollman.Kind);
        Assert.Contains(dollman.Entries, e => e.Equals("DollmanMute.asi", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dollman.Entries, e => e.Equals("DollmanMute.ini", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(mods, m => m.Entries.Contains("Zipliner_v1.1.asi"));          // no config — still a mod
        Assert.Contains(mods, m => m.Entries.Contains("DeathStranding2Fix.asi"));
    }

    [Fact]
    public void Each_addon_is_its_own_shaders_mod_with_its_config()
    {
        var mods = Scan();
        var st = Assert.Single(mods, m => m.Entries.Contains("ShaderToggler.addon64"));
        Assert.Equal("shaders", st.Kind);
        Assert.Contains("ShaderToggler.ini", st.Entries);
        var ui = Assert.Single(mods, m => m.Entries.Contains("DeathStranding2UI.addon64"));
        Assert.Equal("shaders", ui.Kind);
    }

    [Fact]
    public void Exact_proxy_names_detect_as_loader()
    {
        var proxy = Assert.Single(Scan(), m => m.Kind == "loader");
        Assert.Contains("dinput8.dll", proxy.Entries);
    }

    [Fact]
    public void Safety_lines_hold_untouchables_are_never_claimed()
    {
        var claimed = Scan().SelectMany(m => m.Entries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // standalone INIs (incl. ReShade presets the user collected) — left alone
        Assert.DoesNotContain("OptiScaler.ini", claimed);
        Assert.DoesNotContain("Chiral Clarity.ini", claimed);
        Assert.DoesNotContain("NaturalDS2.ini", claimed);
        Assert.DoesNotContain("SDR+.ini", claimed);
        // game files + ambiguous DLL + stray files — invisible
        Assert.DoesNotContain("DS2.exe", claimed);
        Assert.DoesNotContain("DeathStranding2Core.dll", claimed);
        Assert.DoesNotContain("HashDB.bin", claimed);
        Assert.DoesNotContain("DS2nexusfullgame.CT", claimed);
        Assert.DoesNotContain("CLAUDE.md", claimed);
        Assert.DoesNotContain("LocalCacheWinGame", claimed);
        // ReShade's own set belongs to the CATALOG detector, not this one (see next test)
    }

    [Fact]
    public void Already_owned_entries_are_excluded()
    {
        // Simulate the catalog detector having claimed ReShade's set — nature scan must not re-claim.
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "ReShade.ini", "ReShadePreset.ini", "reshade-shaders", "ReShade.log" };
        var claimed = Scan(owned).SelectMany(m => m.Entries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReShade.ini", claimed);
        Assert.DoesNotContain("reshade-shaders", claimed);
    }

    [Fact]
    public void Same_stem_directory_groups_into_the_mod()
    {
        var mods = LooseModScan.Detect(new[] { "CoolMod.asi" }, new[] { "CoolMod" }, null);
        var m = Assert.Single(mods);
        Assert.Contains("CoolMod.asi", m.Entries);
        Assert.Contains("CoolMod", m.Entries);
    }

    [Fact]
    public void Empty_and_unmatched_inputs_detect_nothing()
    {
        Assert.Empty(LooseModScan.Detect(Array.Empty<string>(), Array.Empty<string>(), null));
        Assert.Empty(LooseModScan.Detect(new[] { "readme.txt", "game.exe", "data.core" }, new[] { "data" }, null));
    }
}
