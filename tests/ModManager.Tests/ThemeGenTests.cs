using ModManager.Core;

namespace ModManager.Tests;

// The theme generator: a prompt we hand to any LLM, and a parser for the JSON it returns.
public class ThemeGenTests
{
    [Fact]
    public void Prompt_includes_the_vibe_and_the_contract_and_asks_for_json()
    {
        var p = ThemePrompt.Build("deep ocean bioluminescence");
        Assert.Contains("deep ocean bioluminescence", p);
        Assert.Contains("bg", p);
        Assert.Contains("accent", p);
        Assert.Contains("accent_bloom", p);
        Assert.Contains("JSON", p, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prompt_handles_empty_vibe()
        => Assert.False(string.IsNullOrWhiteSpace(ThemePrompt.Build("")));

    [Fact]
    public void ParseRawTheme_reads_string_tokens_and_accent_bloom()
    {
        var json = """{"name":"Ocean","bg":"#001020","accent":"#22aaff","accent_bloom":{"blur":6,"alpha":0.5}}""";
        var raw = Themes.ParseRawTheme(json);
        Assert.Equal("Ocean", raw.Tokens["name"]);
        Assert.Equal("#001020", raw.Tokens["bg"]);
        Assert.Equal("#22aaff", raw.Tokens["accent"]);
        Assert.NotNull(raw.AccentBloom);
        Assert.Equal(6, raw.AccentBloom!.Blur);
        Assert.Equal(0.5, raw.AccentBloom.Alpha);
    }

    [Fact]
    public void ParseRawTheme_then_normalize_accepts_a_full_theme_and_rejects_a_short_one()
    {
        // A complete theme (every required field) normalizes; a missing field is rejected.
        var full = Themes.NormalizeTheme("ocean", FullTheme());
        Assert.NotNull(full);
        Assert.Equal("Ocean", full!.Name);

        var bad = Themes.ParseRawTheme("""{"name":"Half","bg":"#000000"}""");
        Assert.Null(Themes.NormalizeTheme("half", bad));
    }

    private static RawTheme FullTheme()
    {
        // Build JSON with all 15 required fields, then parse it (exercises the real path).
        var fields = new[]
        {
            "name", "bg", "glass", "glass_on_mica", "title_bg", "border",
            "text", "text_secondary", "text_dim", "text_muted",
            "accent", "bar_bg", "footer_bg", "pace_marker", "sparkline",
        };
        var parts = fields.Select(f => f == "name" ? "\"name\":\"Ocean\"" : $"\"{f}\":\"#101820\"");
        var json = "{" + string.Join(",", parts) + ",\"accent_bloom\":{\"blur\":4,\"alpha\":0.3}}";
        return Themes.ParseRawTheme(json);
    }
}
