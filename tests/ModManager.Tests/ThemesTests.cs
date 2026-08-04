using ModManager.Core;

namespace ModManager.Tests;

// Ports themes.test.js — built-in theme set + normalize (required-field validation, optional
// defaults, tag fallbacks), CSS-var mapping, and the builtin+user merge (user wins by id).
public class ThemesTests
{
    private static RawTheme Clone(RawTheme t) => new() { Tokens = new(t.Tokens), AccentBloom = t.AccentBloom };

    private static RawTheme Minimal(string name = "Min")
    {
        var t = new RawTheme();
        foreach (var f in Themes.RequiredFields) t.Tokens[f] = "#101010";
        t.Tokens["name"] = name;
        return t;
    }

    private static RawTheme Synthwave()
    {
        var t = new RawTheme();
        foreach (var f in Themes.RequiredFields) t.Tokens[f] = "#ff00ff";
        t.Tokens["name"] = "Synthwave";
        return t;
    }

    [Fact]
    public void Exactly_8_builtin_themes() => Assert.Equal(8, Themes.BuiltinThemes.Count);

    [Fact]
    public void Builtin_ids_match_expected_set()
    {
        Assert.Equal(
            new[] { "626-labs", "aurora", "blueprint", "ember", "forge", "matrix", "mint", "obsidian" },
            Themes.BuiltinThemes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Every_builtin_has_required_and_status_tag_tokens()
    {
        var extra = new[] { "success", "warning", "danger", "info", "tag_secondary", "tag_client_only", "tag_vortex", "tag_folder" };
        foreach (var (id, theme) in Themes.BuiltinThemes)
        {
            foreach (var f in Themes.RequiredFields) Assert.True(theme.Tokens.ContainsKey(f), $"{id} missing {f}");
            foreach (var f in extra) Assert.True(theme.Tokens.ContainsKey(f), $"{id} missing {f}");
        }
    }

    [Fact]
    public void NormalizeTheme_valid_passes_and_gets_an_id()
    {
        var t = Themes.NormalizeTheme("mine", Clone(Themes.BuiltinThemes["obsidian"]))!;
        Assert.Equal("mine", t.Id);
        Assert.Equal("Obsidian", t.Name);
    }

    [Fact]
    public void NormalizeTheme_missing_required_field_returns_null()
    {
        var bad = Clone(Themes.BuiltinThemes["obsidian"]);
        bad.Tokens.Remove("accent");
        Assert.Null(Themes.NormalizeTheme("bad", bad));
    }

    [Fact]
    public void NormalizeTheme_optional_status_defaults_tags_fall_back()
    {
        var t = Themes.NormalizeTheme("min", Minimal())!;
        Assert.Equal("#3a8b4a", t["success"]);       // OPTIONAL_DEFAULTS.success
        Assert.Equal("#101010", t["tag_secondary"]); // -> accent
        Assert.Equal("#3a8b4a", t["tag_folder"]);    // -> success default
    }

    [Fact]
    public void ThemeToCssVars_maps_core_status_and_tag_tokens()
    {
        var t = Themes.NormalizeTheme("obsidian", Clone(Themes.BuiltinThemes["obsidian"]))!;
        var v = Themes.ThemeToCssVars(t);
        Assert.Equal("#0d0d0d", v["--bg"]);
        Assert.Equal("#1c1c1c", v["--panel"]);
        Assert.Equal("#6c63ff", v["--accent"]);
        Assert.Equal("#5fb87a", v["--success"]);
        Assert.Equal("#e09080", v["--tag-vortex"]);
    }

    [Fact]
    public void ThemeToCssVars_accent_bloom_none_when_alpha_zero_shadow_when_positive()
    {
        Assert.Equal("none", Themes.ThemeToCssVars(Themes.NormalizeTheme("min", Minimal())!)["--accent-bloom"]);
        Assert.Matches("px", Themes.ThemeToCssVars(Themes.NormalizeTheme("obsidian", Clone(Themes.BuiltinThemes["obsidian"]))!)["--accent-bloom"]);
    }

    [Fact]
    public void BuildThemeList_builtins_only()
    {
        var list = Themes.BuildThemeList(Themes.BuiltinThemes, Array.Empty<(string, RawTheme)>());
        Assert.Equal(8, list.Count);
        Assert.All(list, t => Assert.False(string.IsNullOrEmpty(t.Id) || string.IsNullOrEmpty(t.Name)));
    }

    [Fact]
    public void BuildThemeList_new_user_theme_appended()
    {
        var list = Themes.BuildThemeList(Themes.BuiltinThemes, new[] { ("synthwave", Synthwave()) });
        Assert.Equal(9, list.Count);
        Assert.Contains(list, t => t.Id == "synthwave");
    }

    [Fact]
    public void BuildThemeList_user_overrides_builtin_by_id()
    {
        var tweaked = Clone(Themes.BuiltinThemes["obsidian"]);
        tweaked.Tokens["name"] = "My Obsidian";
        var list = Themes.BuildThemeList(Themes.BuiltinThemes, new[] { ("obsidian", tweaked) });
        Assert.Equal(8, list.Count);
        Assert.Equal("My Obsidian", list.First(t => t.Id == "obsidian").Name);
    }

    [Fact]
    public void BuildThemeList_invalid_user_theme_skipped()
    {
        var bad = new RawTheme();
        bad.Tokens["name"] = "Bad";
        var list = Themes.BuildThemeList(Themes.BuiltinThemes, new[] { ("bad", bad) });
        Assert.Equal(8, list.Count);
    }

    // The flagship (vibe-glow reveal): gunmetal + amber, bloom on. Values are the design
    // language's Forge table verbatim — a drifted token here means the shipped theme no
    // longer matches the measuring stick.
    [Fact]
    public void Forge_flagship_matches_the_design_language()
    {
        var raw = Themes.BuiltinThemes["forge"];
        Assert.Equal("Forge", raw.Tokens["name"]);
        Assert.Equal("#14161a", raw.Tokens["bg"]);
        Assert.Equal("#1d2126", raw.Tokens["glass"]);
        Assert.Equal("#101216", raw.Tokens["title_bg"]);
        Assert.Equal("#33383f", raw.Tokens["border"]);
        Assert.Equal("#e6e8ea", raw.Tokens["text"]);
        Assert.Equal("#ffb454", raw.Tokens["accent"]);
        Assert.Equal("#ff5c33", raw.Tokens["danger"]);
        Assert.Equal("#ffb454", raw.Tokens["warning"]);
        Assert.Equal("#7ec46f", raw.Tokens["success"]);
        Assert.Equal("#6fb3c4", raw.Tokens["info"]);
        Assert.Equal("#0f1114", raw.Tokens["footer_bg"]);
        Assert.NotNull(raw.AccentBloom);
        Assert.Equal(6, raw.AccentBloom!.Blur);
        Assert.Equal(0.45, raw.AccentBloom.Alpha);
    }
}
