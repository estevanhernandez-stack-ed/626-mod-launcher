using ModManager.Core;

namespace ModManager.Tests;

// F-012 (vibe-glow stage-1 audit): danger is the ban-risk / anti-cheat warning text color.
// Every builtin theme must render it at WCAG-AA small-text contrast (4.5:1) against both
// surfaces that carry it — the page background (bg) and the toolbar/banner background (bar_bg).
// User themes stay unrestricted; this contract covers what we ship.
public class ThemesDangerContrastTests
{
    private static double Linearize(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        var r = Convert.ToByte(h.Substring(0, 2), 16);
        var g = Convert.ToByte(h.Substring(2, 2), 16);
        var b = Convert.ToByte(h.Substring(4, 2), 16);
        return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
    }

    private static double Contrast(string fg, string bg)
    {
        var l1 = Luminance(fg);
        var l2 = Luminance(bg);
        var (hi, lo) = l1 >= l2 ? (l1, l2) : (l2, l1);
        return (hi + 0.05) / (lo + 0.05);
    }

    public static IEnumerable<object[]> BuiltinIds()
        => Themes.BuiltinThemes.Keys.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(BuiltinIds))]
    public void Danger_clears_wcag_aa_on_its_carrier_surfaces(string id)
    {
        // bg (page), bar_bg (toolbar/banner), glass (dialog/panel) — the three surfaces danger
        // text actually renders on in the shell.
        var t = Themes.BuiltinThemes[id].Tokens;
        foreach (var surface in new[] { "bg", "bar_bg", "glass" })
        {
            var ratio = Contrast(t["danger"], t[surface]);
            Assert.True(ratio >= 4.5,
                $"{id}: danger {t["danger"]} on {surface} {t[surface]} = {ratio:F2}:1 (needs 4.5:1)");
        }
    }
}
