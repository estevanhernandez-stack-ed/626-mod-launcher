namespace ModManager.Core;

/// <summary>
/// WCAG relative-luminance contrast math (pure, promoted from the danger-contrast tests —
/// vibe-glow F-046). Used by <see cref="Themes.ContrastReport"/> and the Core contrast tests.
/// </summary>
public static class ColorContrast
{
    public static double Ratio(string fgHex, string bgHex)
    {
        var l1 = Luminance(fgHex);
        var l2 = Luminance(bgHex);
        var (hi, lo) = l1 >= l2 ? (l1, l2) : (l2, l1);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>Total variant for advisory paths: false when either color isn't 3/6-digit hex
    /// (named colors, rgba() strings) — an advisory must never throw on user input.</summary>
    public static bool TryRatio(string fg, string bg, out double ratio)
    {
        ratio = 0;
        if (!IsHex(fg) || !IsHex(bg)) return false;
        ratio = Ratio(fg, bg);
        return true;
    }

    private static bool IsHex(string s)
    {
        var h = s.TrimStart('#');
        return (h.Length is 3 or 6) && h.All(Uri.IsHexDigit);
    }

    public static double Luminance(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        var r = Convert.ToByte(h.Substring(0, 2), 16);
        var g = Convert.ToByte(h.Substring(2, 2), 16);
        var b = Convert.ToByte(h.Substring(4, 2), 16);
        return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
    }

    private static double Linearize(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
