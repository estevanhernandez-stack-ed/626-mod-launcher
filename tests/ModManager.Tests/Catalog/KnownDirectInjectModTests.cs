using ModManager.Core.Catalog;

namespace ModManager.Tests.Catalog;

public class KnownDirectInjectModTests
{
    [Fact]
    public void Catalog_ships_Seamless_with_known_config_paths()
    {
        var seamless = KnownDirectInjectMod.Catalog.Single(m => m.ModId == "seamless-coop");

        Assert.Equal("Seamless Co-op", seamless.DisplayName);
        Assert.Equal("co-op", seamless.ChipKind);
        Assert.Equal("fromsoft", seamless.Engine);
        Assert.Equal("Yui", seamless.Author);
        Assert.Equal("PlayFolder", seamless.InstallRoot);
        Assert.Contains("SeamlessCoop/seamlesscoopsettings.ini", seamless.ConfigPaths);
        Assert.Contains("ersc.dll", seamless.InstallSignatureFiles);
        Assert.Contains("seamlesscoop", seamless.InstallSignatureDirs);
    }

    [Fact]
    public void Catalog_includes_six_directinject_mods()
    {
        var modIds = KnownDirectInjectMod.Catalog.Select(m => m.ModId).ToList();
        Assert.Contains("reshade", modIds);
        Assert.Contains("seamless-coop", modIds);
        Assert.Contains("erss2-frame-gen", modIds);
        Assert.Contains("ultrawide-fix", modIds);
        Assert.Contains("modded-regulation", modIds);
        Assert.Contains("dll-mod-loader", modIds);
    }

    [Fact]
    public void Every_entry_has_required_fields()
    {
        foreach (var m in KnownDirectInjectMod.Catalog)
        {
            Assert.Equal("directInjectMod", m.Kind);
            Assert.False(string.IsNullOrWhiteSpace(m.ModId));
            Assert.False(string.IsNullOrWhiteSpace(m.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(m.ChipKind));
            Assert.False(string.IsNullOrWhiteSpace(m.Engine));
            Assert.False(string.IsNullOrWhiteSpace(m.InstallRoot));
        }
    }
}
