namespace ModManager.Core;

/// <summary>
/// Maps an extracted palette (5 dominant colors) to the 15-color theme contract. Honors contrast:
/// even if the source image is all-light or all-dark, the derived theme keeps a dark bg and a light
/// text family. Status colors inherit from the contract's optional defaults (their meaning is
/// stable across themes — we don't paint warnings yellow only when the image happens to be yellow).
/// </summary>
public static class PaletteToTheme
{
    public static RawTheme Derive(IReadOnlyList<PaletteColor> palette, string name)
    {
        if (palette is null || palette.Count == 0)
            throw new ArgumentException("Empty palette.");

        // Sort by brightness ascending, by chroma descending. Pick anchors by role.
        var byBrightness = palette.OrderBy(Brightness).ToList();
        var darkest = byBrightness[0];
        var lightest = byBrightness[^1];
        var accent = palette.OrderByDescending(Chroma).First();

        var bg = DarkenUntilDim(darkest);
        var text = LightenUntilBright(lightest);

        // Backgrounds derived around bg.
        var titleBg = Mix(bg, (0, 0, 0), 0.4);
        var barBg   = Mix(bg, (255, 255, 255), 0.05);
        var footerBg = Mix(bg, (0, 0, 0), 0.55);
        var glass    = Mix(bg, (255, 255, 255), 0.08);
        var glassOnMica = Mix(bg, (255, 255, 255), 0.03);
        var border   = Mix(bg, (255, 255, 255), 0.18);

        // Text family derived around text.
        var textSecondary = Mix(text, bg, 0.25);
        var textDim       = Mix(text, bg, 0.50);
        var textMuted     = Mix(text, bg, 0.65);

        // Accent + accent-adjacent.
        var paceMarker = ShiftHue(accent, 150); // complementary-ish
        var sparkline  = (accent.R, accent.G, accent.B);

        var tokens = new Dictionary<string, string>
        {
            ["name"]            = name,
            ["bg"]              = Hex(bg),
            ["glass"]           = Hex(glass),
            ["glass_on_mica"]   = Hex(glassOnMica),
            ["title_bg"]        = Hex(titleBg),
            ["border"]          = Hex(border),
            ["text"]            = Hex(text),
            ["text_secondary"]  = Hex(textSecondary),
            ["text_dim"]        = Hex(textDim),
            ["text_muted"]      = Hex(textMuted),
            ["accent"]          = Hex((accent.R, accent.G, accent.B)),
            ["bar_bg"]          = Hex(barBg),
            ["footer_bg"]       = Hex(footerBg),
            ["pace_marker"]     = Hex(paceMarker),
            ["sparkline"]       = Hex(sparkline),
        };
        return new RawTheme { Tokens = tokens, AccentBloom = new AccentBloom(6, 0.55) };
    }

    // ---- color math ----
    private static double Brightness(PaletteColor c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
    private static double Brightness((int R, int G, int B) c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
    private static double Chroma(PaletteColor c)
    {
        var max = Math.Max(c.R, Math.Max(c.G, c.B));
        var min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max == 0 ? 0 : (double)(max - min) / max * max; // saturation × value
    }

    private static (int R, int G, int B) DarkenUntilDim(PaletteColor c)
    {
        var rgb = ((int)c.R, (int)c.G, (int)c.B);
        // Target brightness < 30 so the bg reads as a true background regardless of source image.
        while (Brightness(rgb) > 30)
            rgb = Mix(rgb, (10, 10, 14), 0.4);
        return rgb;
    }

    private static (int R, int G, int B) LightenUntilBright(PaletteColor c)
    {
        var rgb = ((int)c.R, (int)c.G, (int)c.B);
        while (Brightness(rgb) < 210)
            rgb = Mix(rgb, (240, 240, 245), 0.4);
        return rgb;
    }

    private static (int R, int G, int B) Mix((int R, int G, int B) a, (int R, int G, int B) b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return ((int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    private static (int R, int G, int B) ShiftHue(PaletteColor c, double degrees)
    {
        var (h, s, v) = RgbToHsv(c.R, c.G, c.B);
        h = (h + degrees) % 360;
        if (h < 0) h += 360;
        return HsvToRgb(h, s, v);
    }

    private static (double H, double S, double V) RgbToHsv(int r, int g, int b)
    {
        var rn = r / 255.0; var gn = g / 255.0; var bn = b / 255.0;
        var max = Math.Max(rn, Math.Max(gn, bn));
        var min = Math.Min(rn, Math.Min(gn, bn));
        var d = max - min;
        var h = 0.0;
        if (d > 0.0001)
        {
            if (max == rn) h = 60 * (((gn - bn) / d) % 6);
            else if (max == gn) h = 60 * ((bn - rn) / d + 2);
            else h = 60 * ((rn - gn) / d + 4);
        }
        if (h < 0) h += 360;
        var s = max == 0 ? 0 : d / max;
        return (h, s, max);
    }

    private static (int R, int G, int B) HsvToRgb(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        var m = v - c;
        double r, g, b;
        if      (h < 60)  { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else              { r = c; g = 0; b = x; }
        return ((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    private static string Hex((int R, int G, int B) c)
        => $"#{Math.Clamp(c.R, 0, 255):x2}{Math.Clamp(c.G, 0, 255):x2}{Math.Clamp(c.B, 0, 255):x2}";
}
