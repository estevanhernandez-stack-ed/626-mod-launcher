using ModManager.Core;

namespace ModManager.Tests;

// F-046 (vibe-glow wave 7): theme import validates field presence but never readability.
// ContrastReport is the pure Core advisory — it names token pairs that fall below WCAG AA
// so NewThemeDialog can warn (never block; user theming stays unrestricted).
public class ThemesContrastReportTests
{
    private static Theme Build(Action<Dictionary<string, string>>? mutate = null)
    {
        var raw = new RawTheme { Tokens = new(Themes.BuiltinThemes["626-labs"].Tokens) };
        mutate?.Invoke(raw.Tokens);
        return Themes.NormalizeTheme("test", raw)!;
    }

    [Fact]
    public void Builtin_default_reports_no_warnings()
        => Assert.Empty(Themes.ContrastReport(Build()));

    [Fact]
    public void Unreadable_text_on_bg_is_named()
    {
        var warnings = Themes.ContrastReport(Build(t => t["text"] = "#1a1a2a")); // near-bg ink
        Assert.Contains(warnings, w => w.Contains("text") && w.Contains("bg"));
    }

    [Fact]
    public void Low_danger_on_bar_bg_is_named()
    {
        var warnings = Themes.ContrastReport(Build(t => t["danger"] = "#3a2530"));
        Assert.Contains(warnings, w => w.Contains("danger"));
    }

    [Fact]
    public void Warnings_carry_the_computed_ratio()
    {
        var warnings = Themes.ContrastReport(Build(t => t["text"] = "#1a1a2a"));
        Assert.Contains(warnings, w => w.Contains(":1")); // e.g. "1.2:1"
    }

    [Fact]
    public void Unparseable_color_formats_are_skipped_never_thrown()
    {
        // The theme is already persisted when the report runs — an advisory must be total.
        var t = Build(tokens => tokens["text"] = "rgba(0,0,0,1)");
        var warnings = Themes.ContrastReport(t);
        Assert.DoesNotContain(warnings, w => w.StartsWith("text on"));
    }

    [Fact]
    public void All_builtins_pass_their_own_report_for_primary_pairs()
    {
        foreach (var (id, raw) in Themes.BuiltinThemes)
        {
            var t = Themes.NormalizeTheme(id, new RawTheme { Tokens = new(raw.Tokens), AccentBloom = raw.AccentBloom })!;
            var warnings = Themes.ContrastReport(t).Where(w => w.StartsWith("text ")).ToList();
            Assert.True(warnings.Count == 0, $"{id}: {string.Join("; ", warnings)}");
        }
    }
}
