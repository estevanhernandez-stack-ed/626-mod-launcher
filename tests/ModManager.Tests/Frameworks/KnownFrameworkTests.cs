using ModManager.Core.Frameworks;

namespace ModManager.Tests.Frameworks;

public class KnownFrameworkTests
{
    [Fact]
    public void Catalog_ships_Elden_Mod_Loader_for_Elden_Ring()
    {
        var elm = KnownFramework.Catalog.Single(f => f.FrameworkId == "elden-mod-loader");

        Assert.Equal("Elden Mod Loader", elm.DisplayName);
        Assert.Equal("fromsoft", elm.Engine);
        Assert.Equal("1245620", elm.SteamAppId);
        Assert.Equal("https://www.nexusmods.com/eldenring/mods/117", elm.GetUrl);
        Assert.Equal("TechieW", elm.Author);
        // PlayFolder, not GameRoot — FromSoft games (ER) put the exe under <gameRoot>/Game/
        // and ELM's dinput8.dll proxy must sit next to the exe to load.
        Assert.Equal("PlayFolder", elm.InstallRoot);
        Assert.Contains("dinput8.dll", elm.ZipSignatureFiles);
        Assert.Contains("mod_loader_config.ini", elm.ZipSignatureFiles);
        Assert.Contains("eldenring.exe", elm.ForbiddenPaths);
    }

    [Fact]
    public void Catalog_entries_have_nonempty_required_fields()
    {
        foreach (var f in KnownFramework.Catalog)
        {
            Assert.False(string.IsNullOrWhiteSpace(f.FrameworkId), $"FrameworkId empty");
            Assert.False(string.IsNullOrWhiteSpace(f.DisplayName), $"DisplayName empty for {f.FrameworkId}");
            Assert.False(string.IsNullOrWhiteSpace(f.Engine), $"Engine empty for {f.FrameworkId}");
            Assert.False(string.IsNullOrWhiteSpace(f.GetUrl), $"GetUrl empty for {f.FrameworkId}");
            Assert.False(string.IsNullOrWhiteSpace(f.Author), $"Author empty for {f.FrameworkId}");
            Assert.NotEmpty(f.ZipSignatureFiles);
        }
    }
}

public class KnownFrameworkClassifyTests
{
    [Fact]
    public void Classify_matches_ELM_when_signature_files_present_in_zip()
    {
        var zipEntries = new[]
        {
            "dinput8.dll",
            "mod_loader_config.ini",
            "ModLoader/some.dll",
        };

        var result = KnownFramework.Classify(zipEntries, engine: "fromsoft", steamAppId: "1245620");

        Assert.NotNull(result.Match);
        Assert.Equal("elden-mod-loader", result.Match.FrameworkId);
        Assert.False(result.LooksLikeFramework, "Recognized hit should NOT also flag looks-like.");
    }

    [Fact]
    public void Classify_no_match_when_wrong_engine()
    {
        var zipEntries = new[] { "dinput8.dll", "mod_loader_config.ini" };

        var result = KnownFramework.Classify(zipEntries, engine: "ue-pak", steamAppId: null);

        Assert.Null(result.Match);
    }

    [Fact]
    public void Classify_no_match_when_signature_files_missing()
    {
        // Only dinput8.dll — not enough to be ELM (which requires mod_loader_config.ini too).
        var zipEntries = new[] { "dinput8.dll", "somethingelse.dll" };

        var result = KnownFramework.Classify(zipEntries, engine: "fromsoft", steamAppId: "1245620");

        Assert.Null(result.Match);
    }

    [Fact]
    public void Classify_flags_looks_like_framework_for_unrecognized_proxy_dll_at_zip_root()
    {
        // Has winhttp.dll at the zip root but ISN'T a catalog match.
        // (e.g. user has a homebrew DLL proxy or a framework we don't know about.)
        var zipEntries = new[] { "winhttp.dll", "some_other_thing.txt" };

        var result = KnownFramework.Classify(zipEntries, engine: "fromsoft", steamAppId: "1245620");

        Assert.Null(result.Match);
        Assert.True(result.LooksLikeFramework, "Bare DLL-proxy at zip root should look-like.");
    }

    [Fact]
    public void Classify_does_not_flag_looks_like_for_non_fromsoft_engines()
    {
        var zipEntries = new[] { "winhttp.dll" };

        var result = KnownFramework.Classify(zipEntries, engine: "ue-pak", steamAppId: null);

        Assert.Null(result.Match);
        Assert.False(result.LooksLikeFramework, "Looks-like is FromSoft-specific for v1.");
    }

    [Fact]
    public void Classify_does_not_flag_looks_like_when_proxy_dll_is_nested()
    {
        // winhttp.dll under a subfolder is a regular mod's bundled lib, not a framework signal.
        var zipEntries = new[] { "subfolder/winhttp.dll", "subfolder/data.bin" };

        var result = KnownFramework.Classify(zipEntries, engine: "fromsoft", steamAppId: "1245620");

        Assert.Null(result.Match);
        Assert.False(result.LooksLikeFramework, "Nested DLL-proxy shouldn't trigger the nudge.");
    }

    [Fact]
    public void Classify_returns_no_match_when_zip_is_empty()
    {
        var result = KnownFramework.Classify(Array.Empty<string>(), engine: "fromsoft", steamAppId: "1245620");

        Assert.Null(result.Match);
        Assert.False(result.LooksLikeFramework);
    }
}
