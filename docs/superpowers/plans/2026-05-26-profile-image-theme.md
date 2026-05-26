# Profile image → derived theme + app icon — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:subagent-driven-development`. Steps use checkbox (`- [ ]`) syntax. Test command: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. Build command: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Kill the running `ModManager.App.exe` before building (DLL lock per `portable-build-file-lock` memory).

**Goal:** User can pick an image (Discord avatar, photo, anything PNG/JPG) in a new Profile dialog. The launcher extracts the dominant palette from that image with classical k-means color quantization (no AI), maps the palette to the existing 15-color theme contract, saves the result as a user theme, AND uses the image as the in-app title-bar icon + Windows window/taskbar icon. Zero new runtime dependencies — uses built-in WinRT image APIs (`BitmapDecoder`) for decoding, hand-rolled PNG-wrapping `.ico` for the window icon.

**Architecture:** Two new pure-core classes (palette extraction + palette→theme mapping) backed by tests; one new App service (avatar storage + .ico writer); one new XAML `ProfileDialog` accessible from `⋯ More → Profile…`. The MainWindow title bar's `<Image Source="ms-appx:///Assets/icon.ico" />` swaps to a binding on `ViewModel.AppIconSource` that returns the user avatar path when set, falling back to the bundled icon. `AppWindow.SetIcon` is re-called whenever the avatar changes. Honors the existing data-dir conventions (`%APPDATA%\ModManagerBuilder\profile\avatar.png` + `avatar.ico`).

**Tech Stack:** WinUI 3 on .NET 10, CommunityToolkit.Mvvm, xUnit. Image decoding via `Windows.Graphics.Imaging.BitmapDecoder` (built-in). No new NuGet deps.

---

## File Structure

| File | Purpose |
| --- | --- |
| Create: `src/ModManager.Core/PaletteExtractor.cs` | Pure k-means color quantization. Input: RGBA pixel array + dimensions + k. Output: K dominant RGB colors sorted by frequency. No Electron / no WinRT. |
| Create: `src/ModManager.Core/PaletteToTheme.cs` | Pure palette→theme mapping. Input: 5 dominant colors + name. Output: `RawTheme` with all 15 required + 4 optional + 4 tag tokens. Honors contrast (synthesizes dark bg / light text if the palette is monochromatic). |
| Create: `tests/ModManager.Tests/PaletteExtractorTests.cs` | K-means on known inputs; edge cases (single-color image, sparse palettes, tiny images). |
| Create: `tests/ModManager.Tests/PaletteToThemeTests.cs` | Slot mapping: all 15 required fields populated; bg darker than text (contrast invariant); accent is the highest-chroma color. |
| Create: `src/ModManager.App/Services/AvatarService.cs` | Manages the avatar PNG + derived .ico on disk under `%APPDATA%\ModManagerBuilder\profile\`. Hand-rolled .ico writer (PNG-wrapping). Decode images via `BitmapDecoder`. |
| Create: `src/ModManager.App/ProfileDialog.xaml` + `.xaml.cs` | The dialog: image picker, palette preview, checkboxes (use as icon / derive theme), Apply / Close. |
| Modify: `src/ModManager.App/MainWindow.xaml` | Title-bar `Image.Source` binds to `ViewModel.AppIconSource`. Add `⋯ More → Profile…` menu item. |
| Modify: `src/ModManager.App/MainWindow.xaml.cs` | New `OnProfile` handler that opens `ProfileDialog`. |
| Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` | `AppIconSource` getter (returns avatar URI if set, bundled URI otherwise) + notify on avatar change. |

---

## Task 1: Core — `PaletteExtractor` (k-means dominant colors)

**Files:**
- Create: `src/ModManager.Core/PaletteExtractor.cs`
- Create: `tests/ModManager.Tests/PaletteExtractorTests.cs`

K-means in RGB space. Naive implementation is fine — input is downsampled to ~4096 pixels (64×64) before clustering, so iterations are cheap. Deterministic seeding (pick k evenly-spaced pixels by brightness) so tests are repeatable.

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class PaletteExtractorTests
{
    // A 2-color image (half black, half white): k=2 should recover both clusters.
    [Fact]
    public void Extracts_two_dominant_colors_from_a_bicolor_image()
    {
        // 8 pixels: 4 black, 4 white. RGBA byte order.
        var pixels = new byte[]
        {
            0,0,0,255,  0,0,0,255,  0,0,0,255,  0,0,0,255,
            255,255,255,255,  255,255,255,255,  255,255,255,255,  255,255,255,255,
        };
        var palette = PaletteExtractor.Extract(pixels, width: 4, height: 2, k: 2);
        Assert.Equal(2, palette.Count);
        // Sorted by brightness ascending — first is darkest.
        Assert.True(palette[0].R < 40 && palette[0].G < 40 && palette[0].B < 40);
        Assert.True(palette[1].R > 200 && palette[1].G > 200 && palette[1].B > 200);
    }

    // A solid-color image: every cluster collapses to the same color.
    [Fact]
    public void Solid_color_image_yields_palette_of_that_one_color()
    {
        var pixels = new byte[16];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 80; pixels[i+1] = 120; pixels[i+2] = 200; pixels[i+3] = 255; }
        var palette = PaletteExtractor.Extract(pixels, width: 2, height: 2, k: 5);
        Assert.NotEmpty(palette);
        Assert.All(palette, c => { Assert.InRange(c.R, 78, 82); Assert.InRange(c.G, 118, 122); Assert.InRange(c.B, 198, 202); });
    }

    // k larger than pixel count must still return at most pixel-count distinct clusters.
    [Fact]
    public void K_larger_than_pixels_does_not_throw_and_returns_at_most_n_colors()
    {
        var pixels = new byte[] { 10,10,10,255,  200,200,200,255 };
        var palette = PaletteExtractor.Extract(pixels, width: 2, height: 1, k: 10);
        Assert.True(palette.Count <= 2);
        Assert.NotEmpty(palette);
    }

    // Transparent pixels (alpha == 0) should be excluded so a PNG with a transparent background
    // doesn't pull the palette toward an arbitrary fill color.
    [Fact]
    public void Fully_transparent_pixels_are_excluded_from_clustering()
    {
        var pixels = new byte[]
        {
            // 2 fully-transparent (could be any color)
            255,0,0,0,  0,255,0,0,
            // 2 solid blue
            0,0,200,255,  0,0,200,255,
        };
        var palette = PaletteExtractor.Extract(pixels, width: 2, height: 2, k: 1);
        Assert.Single(palette);
        Assert.True(palette[0].B > 150);
        Assert.True(palette[0].R < 50);
    }

    // Empty input is refused with an ArgumentException (caller must guard).
    [Fact]
    public void Empty_pixels_throws()
    {
        Assert.Throws<ArgumentException>(() => PaletteExtractor.Extract(Array.Empty<byte>(), 0, 0, 3));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile-fail counts as red)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PaletteExtractorTests"`
Expected: FAIL — `PaletteExtractor` does not exist.

- [ ] **Step 3: Implement `PaletteExtractor`**

```csharp
namespace ModManager.Core;

/// <summary>One dominant color extracted from an image. Frequency is the number of source pixels
/// that ended up in this cluster (so callers can weight by visual prominence).</summary>
public sealed record PaletteColor(byte R, byte G, byte B, int Frequency);

/// <summary>
/// Classical k-means color quantization in RGB space — pure data in / data out, no WinRT, no UI.
/// Deterministic: seeds clusters by brightness-rank percentiles so the same input yields the same
/// palette every run (tests stay stable).
/// </summary>
public static class PaletteExtractor
{
    /// <summary>Extract up to <paramref name="k"/> dominant colors from RGBA pixel data. Fully
    /// transparent pixels are excluded (so transparent-PNG backgrounds don't drag the palette).
    /// Returns colors sorted by brightness ascending (darkest first) — matches the slot-mapper's
    /// expectation. Throws <see cref="ArgumentException"/> on empty input.</summary>
    public static IReadOnlyList<PaletteColor> Extract(byte[] rgbaPixels, int width, int height, int k)
    {
        if (rgbaPixels is null || rgbaPixels.Length == 0 || width <= 0 || height <= 0)
            throw new ArgumentException("Empty or invalid pixel buffer.");
        if (k < 1) k = 1;

        // Collect opaque pixels.
        var opaque = new List<(byte R, byte G, byte B)>(width * height);
        for (var i = 0; i + 3 < rgbaPixels.Length; i += 4)
        {
            if (rgbaPixels[i + 3] == 0) continue; // fully transparent
            opaque.Add((rgbaPixels[i], rgbaPixels[i + 1], rgbaPixels[i + 2]));
        }
        if (opaque.Count == 0) return Array.Empty<PaletteColor>();

        // k can't exceed the distinct opaque pixel count.
        var distinct = opaque.Distinct().Count();
        var effectiveK = Math.Min(k, distinct);

        // Deterministic seeding: sort by brightness, pick evenly-spaced percentile points.
        var sortedByBrightness = opaque
            .OrderBy(p => 0.299 * p.R + 0.587 * p.G + 0.114 * p.B)
            .ToList();
        var centers = new List<(double R, double G, double B)>(effectiveK);
        for (var i = 0; i < effectiveK; i++)
        {
            var idx = (int)((double)i / Math.Max(1, effectiveK - 1) * (sortedByBrightness.Count - 1));
            if (effectiveK == 1) idx = sortedByBrightness.Count / 2;
            var p = sortedByBrightness[idx];
            centers.Add((p.R, p.G, p.B));
        }

        // K-means iterations. Stop when no center moves >0.5 or after 20 iterations.
        const int maxIterations = 20;
        var assignments = new int[opaque.Count];
        for (var iter = 0; iter < maxIterations; iter++)
        {
            // Assign each pixel to its nearest center.
            for (var pi = 0; pi < opaque.Count; pi++)
            {
                var p = opaque[pi];
                var bestIdx = 0;
                var bestDist = double.MaxValue;
                for (var ci = 0; ci < centers.Count; ci++)
                {
                    var c = centers[ci];
                    var dr = p.R - c.R;
                    var dg = p.G - c.G;
                    var db = p.B - c.B;
                    var d = dr * dr + dg * dg + db * db;
                    if (d < bestDist) { bestDist = d; bestIdx = ci; }
                }
                assignments[pi] = bestIdx;
            }
            // Recompute centers as the mean of assigned pixels.
            var sums = new (double R, double G, double B, int N)[centers.Count];
            for (var pi = 0; pi < opaque.Count; pi++)
            {
                var p = opaque[pi];
                var ci = assignments[pi];
                sums[ci] = (sums[ci].R + p.R, sums[ci].G + p.G, sums[ci].B + p.B, sums[ci].N + 1);
            }
            var maxMove = 0.0;
            for (var ci = 0; ci < centers.Count; ci++)
            {
                if (sums[ci].N == 0) continue; // dead cluster — leave it where it was
                var newR = sums[ci].R / sums[ci].N;
                var newG = sums[ci].G / sums[ci].N;
                var newB = sums[ci].B / sums[ci].N;
                var moveR = newR - centers[ci].R;
                var moveG = newG - centers[ci].G;
                var moveB = newB - centers[ci].B;
                var move = Math.Sqrt(moveR * moveR + moveG * moveG + moveB * moveB);
                if (move > maxMove) maxMove = move;
                centers[ci] = (newR, newG, newB);
            }
            if (maxMove < 0.5) break;
        }

        // Frequency by cluster.
        var freq = new int[centers.Count];
        foreach (var a in assignments) freq[a]++;

        // Drop any zero-frequency clusters, then sort by brightness ascending.
        var result = new List<PaletteColor>();
        for (var ci = 0; ci < centers.Count; ci++)
        {
            if (freq[ci] == 0) continue;
            var c = centers[ci];
            result.Add(new PaletteColor(
                (byte)Math.Clamp(Math.Round(c.R), 0, 255),
                (byte)Math.Clamp(Math.Round(c.G), 0, 255),
                (byte)Math.Clamp(Math.Round(c.B), 0, 255),
                freq[ci]));
        }
        return result
            .OrderBy(p => 0.299 * p.R + 0.587 * p.G + 0.114 * p.B)
            .ToList();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Expected: all 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/PaletteExtractor.cs tests/ModManager.Tests/PaletteExtractorTests.cs
git commit -m "feat: PaletteExtractor (k-means color quantization, deterministic seeding)"
```

---

## Task 2: Core — `PaletteToTheme` (palette → 15-color theme)

**Files:**
- Create: `src/ModManager.Core/PaletteToTheme.cs`
- Create: `tests/ModManager.Tests/PaletteToThemeTests.cs`

Maps the extracted palette to the 15-color contract from `Themes.RequiredFields`. Strategy:

- **Background palette** (bg, title_bg, bar_bg, footer_bg, glass, glass_on_mica, border): derived from the DARKEST input color. If the darkest color is still bright (image is all light tones), synthesize a dark background by multiplying the darkest color toward `#0a0a0a` until brightness < 60.
- **Text palette** (text, text_secondary, text_dim, text_muted): derived from the LIGHTEST input color. If the lightest is still dark, lighten toward `#f0f0f0` until brightness > 200.
- **Accent**: the input color with the highest chroma (HSV saturation × value). `sparkline` = same. `pace_marker` = a complementary or shifted-hue variant.
- **Status colors** (success / warning / danger / info): inherited from `OptionalDefaults` — these need to mean what they mean across themes, so we don't derive them from arbitrary images.
- **Tag tokens**: default to the accent + status palette (matches the built-in pattern).

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class PaletteToThemeTests
{
    [Fact]
    public void Derives_all_15_required_fields()
    {
        var palette = new List<PaletteColor>
        {
            new(20, 20, 40, 100),
            new(80, 80, 100, 80),
            new(160, 160, 180, 60),
            new(220, 220, 230, 40),
            new(60, 180, 220, 30),
        };
        var raw = PaletteToTheme.Derive(palette, name: "Test theme");
        foreach (var f in Themes.RequiredFields)
            Assert.True(raw.Tokens.ContainsKey(f), $"Missing required field: {f}");
        Assert.Equal("Test theme", raw.Tokens["name"]);
    }

    [Fact]
    public void Background_is_darker_than_text_after_normalization()
    {
        // Even on a bright-only palette, the derived theme should still have a dark bg / light text.
        var palette = new List<PaletteColor>
        {
            new(180, 180, 180, 50),
            new(200, 210, 220, 50),
            new(230, 230, 240, 50),
            new(240, 220, 200, 50),
            new(220, 240, 200, 50),
        };
        var raw = PaletteToTheme.Derive(palette, "Bright palette");
        var bg = ParseHex(raw.Tokens["bg"]);
        var text = ParseHex(raw.Tokens["text"]);
        Assert.True(Brightness(bg) < Brightness(text),
            $"bg ({raw.Tokens["bg"]}) must be darker than text ({raw.Tokens["text"]}).");
    }

    [Fact]
    public void Accent_is_the_highest_chroma_color_when_one_is_clearly_colorful()
    {
        // 4 near-grays + 1 vivid blue → accent should pick the blue.
        var palette = new List<PaletteColor>
        {
            new(30, 30, 30, 100),
            new(80, 80, 82, 80),
            new(180, 180, 180, 60),
            new(220, 220, 222, 40),
            new(40, 100, 220, 30), // vivid blue
        };
        var raw = PaletteToTheme.Derive(palette, "Mostly gray + blue");
        var accent = ParseHex(raw.Tokens["accent"]);
        // The accent's blue channel should dominate.
        Assert.True(accent.B > accent.R + 40, $"accent {raw.Tokens["accent"]} should be blue-dominant.");
    }

    [Fact]
    public void Empty_palette_throws()
    {
        Assert.Throws<ArgumentException>(() => PaletteToTheme.Derive(new List<PaletteColor>(), "Empty"));
    }

    [Fact]
    public void Result_normalizes_into_a_valid_Theme()
    {
        var palette = new List<PaletteColor>
        {
            new(15, 20, 35, 200),
            new(70, 80, 110, 150),
            new(200, 210, 220, 100),
            new(58, 200, 230, 50),
            new(220, 90, 130, 40),
        };
        var raw = PaletteToTheme.Derive(palette, "From avatar");
        var normalized = Themes.NormalizeTheme("from-avatar", raw);
        Assert.NotNull(normalized);
        Assert.Equal("From avatar", normalized!.Name);
    }

    // --- helpers ---
    private static (int R, int G, int B) ParseHex(string hex)
    {
        var h = hex.TrimStart('#');
        return (Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
    }
    private static double Brightness((int R, int G, int B) c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement `PaletteToTheme`**

```csharp
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
        // RGB → HSV → shift hue → RGB. Bounded to integer math, no Color struct dependency.
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
        => $"#{Math.Clamp(c.R, 0, 255):X2}{Math.Clamp(c.G, 0, 255):X2}{Math.Clamp(c.B, 0, 255):X2}".ToLowerInvariant().Replace("#", "#");
}
```

- [ ] **Step 4: Run tests to verify they pass**

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/PaletteToTheme.cs tests/ModManager.Tests/PaletteToThemeTests.cs
git commit -m "feat: PaletteToTheme maps palette to the 15-color theme contract"
```

---

## Task 3: App — `AvatarService` (storage + .ico writer)

**Files:**
- Create: `src/ModManager.App/Services/AvatarService.cs`

The avatar PNG + derived `.ico` live under `%APPDATA%\ModManagerBuilder\profile\`. Decode uploaded images via `Windows.Graphics.Imaging.BitmapDecoder` (built-in). The `.ico` writer hand-rolls a PNG-wrapping ICO file (modern Windows accepts a PNG payload in an .ico container — header + one directory entry + the PNG bytes).

- [ ] **Step 1: Create the service**

```csharp
using System.IO;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ModManager.App.Services;

/// <summary>
/// Manages the user's avatar PNG + derived .ico under %APPDATA%\ModManagerBuilder\profile\. Uses
/// built-in WinRT BitmapDecoder for decode (no System.Drawing dep). The .ico writer wraps a PNG
/// payload — modern Windows reads PNG-in-ICO directly, no BMP conversion needed.
/// </summary>
public sealed class AvatarService
{
    public string ProfileDir { get; }
    public string AvatarPngPath { get; }
    public string AvatarIcoPath { get; }

    public AvatarService()
    {
        ProfileDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModManagerBuilder", "profile");
        AvatarPngPath = Path.Combine(ProfileDir, "avatar.png");
        AvatarIcoPath = Path.Combine(ProfileDir, "avatar.ico");
    }

    public bool HasAvatar => File.Exists(AvatarPngPath);

    /// <summary>Decode a source image (any format BitmapDecoder supports), re-encode as a square
    /// 256×256 PNG, and persist as the avatar. Also generates a matching 64×64 .ico.</summary>
    public async Task ImportAsync(string sourceImagePath)
    {
        Directory.CreateDirectory(ProfileDir);

        // Decode source → resized RGBA pixel buffer (256×256, square-cropped center).
        var (png256, rgba256) = await ResizeToSquareAsync(sourceImagePath, 256);
        File.WriteAllBytes(AvatarPngPath, png256);

        // Re-encode again at 64×64 for the .ico payload.
        var (png64, _) = await ResizeToSquareAsync(sourceImagePath, 64);
        WritePngInIco(AvatarIcoPath, png64, 64);
    }

    public void Delete()
    {
        if (File.Exists(AvatarPngPath)) File.Delete(AvatarPngPath);
        if (File.Exists(AvatarIcoPath)) File.Delete(AvatarIcoPath);
    }

    /// <summary>Decode → square-crop center → resize → encode PNG. Returns (pngBytes, rgbaPixels).</summary>
    public static async Task<(byte[] PngBytes, byte[] RgbaPixels)> ResizeToSquareAsync(string path, uint side)
    {
        using var src = File.OpenRead(path);
        var randomAccess = src.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccess);
        var transform = new BitmapTransform { ScaledWidth = side, ScaledHeight = side, InterpolationMode = BitmapInterpolationMode.Fant };
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var rgba = pixels.DetachPixelData();

        var ms = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ms);
        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied, side, side, 96, 96, rgba);
        await encoder.FlushAsync();
        ms.Seek(0);
        var pngBytes = new byte[ms.Size];
        await ms.ReadAsync(pngBytes.AsBuffer(), (uint)ms.Size, InputStreamOptions.None);
        return (pngBytes, rgba);
    }

    /// <summary>Write a PNG-wrapping .ico file. Header (6 bytes) + 1 directory entry (16 bytes) +
    /// the PNG payload. Width/height stored as 0 mean "256 or larger" — we use the literal byte
    /// for sub-256 sizes.</summary>
    private static void WritePngInIco(string path, byte[] pngBytes, byte side)
    {
        using var fs = File.Create(path);
        // ICONDIR
        fs.Write(new byte[] { 0, 0, 1, 0, 1, 0 }); // reserved, type=1 icon, count=1
        // ICONDIRENTRY (16 bytes)
        var sideByte = side >= 256 ? (byte)0 : side;
        fs.WriteByte(sideByte); // width
        fs.WriteByte(sideByte); // height
        fs.WriteByte(0);        // color count (0 for >256 colors)
        fs.WriteByte(0);        // reserved
        fs.Write(BitConverter.GetBytes((ushort)1));    // color planes
        fs.Write(BitConverter.GetBytes((ushort)32));   // bits per pixel
        fs.Write(BitConverter.GetBytes((uint)pngBytes.Length)); // image size
        fs.Write(BitConverter.GetBytes((uint)22));     // offset (6 + 16)
        fs.Write(pngBytes);
    }
}
```

Required usings: `System.IO`, `Windows.Graphics.Imaging`, `Windows.Storage.Streams`, `System.Runtime.InteropServices.WindowsRuntime` (for `AsBuffer` extension).

- [ ] **Step 2: Register the service in DI**

In `App.xaml.cs` (or wherever the DI container is built), add:

```csharp
services.AddSingleton<AvatarService>();
```

Find the line that registers `ThemeService`, `LauncherService`, etc., and add the new line alongside them.

- [ ] **Step 3: Build the App**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/Services/AvatarService.cs src/ModManager.App/App.xaml.cs
git commit -m "feat: AvatarService — store user avatar PNG + write a PNG-in-ICO icon"
```

---

## Task 4: App — `ProfileDialog` + menu wiring

**Files:**
- Create: `src/ModManager.App/ProfileDialog.xaml` + `.xaml.cs`
- Modify: `src/ModManager.App/MainWindow.xaml` (add `⋯ More → Profile…` menu item)
- Modify: `src/ModManager.App/MainWindow.xaml.cs` (add `OnProfile` handler)
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (add `AppIconSource` getter + notify hook)

### 4a — ProfileDialog XAML

```xml
<?xml version="1.0" encoding="utf-8"?>
<ContentDialog
    x:Class="ModManager.App.ProfileDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Profile"
    PrimaryButtonText="Apply"
    CloseButtonText="Close"
    DefaultButton="Primary"
    RequestedTheme="Dark"
    PrimaryButtonClick="OnApply">

    <StackPanel Spacing="14" Width="440">
        <TextBlock TextWrapping="Wrap" Opacity="0.75" FontSize="12"
                   Text="Pick an image — a Discord avatar, a screenshot, a photo. Use it as the launcher's icon and derive a theme from its colors. Everything stays on your machine." />

        <Grid ColumnSpacing="12">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <Border Grid.Column="0" Width="96" Height="96" CornerRadius="8"
                    Background="{StaticResource ThemePanel}" BorderBrush="{StaticResource ThemeBorder}" BorderThickness="1">
                <Image x:Name="PreviewImage" Stretch="UniformToFill" />
            </Border>
            <StackPanel Grid.Column="1" Spacing="6" VerticalAlignment="Center">
                <Button Content="Pick image…" Click="OnPickImage" />
                <Button x:Name="RemoveButton" Content="Remove avatar" Click="OnRemove" Visibility="Collapsed" />
                <TextBlock x:Name="FileLabel" Opacity="0.65" FontSize="11" TextWrapping="NoWrap" TextTrimming="CharacterEllipsis" />
            </StackPanel>
        </Grid>

        <TextBlock Text="Extracted palette" FontWeight="SemiBold" />
        <StackPanel x:Name="PaletteStrip" Orientation="Horizontal" Spacing="6" Height="32" />
        <TextBlock x:Name="PaletteEmpty" Text="Pick an image to see its dominant colors." Opacity="0.5" FontSize="12" />

        <CheckBox x:Name="UseAsIconCheck" Content="Use this image as the launcher's icon" IsChecked="True" />
        <CheckBox x:Name="DeriveThemeCheck" Content="Save a theme derived from these colors" IsChecked="True" />
        <TextBox x:Name="ThemeNameBox" Header="Theme name" Text="From avatar" Visibility="{x:Bind DeriveThemeCheck.IsChecked, Mode=OneWay}" />

        <TextBlock x:Name="StatusText" Opacity="0.75" FontSize="12" TextWrapping="Wrap" />
    </StackPanel>
</ContentDialog>
```

(Note: `Visibility="{x:Bind DeriveThemeCheck.IsChecked, Mode=OneWay}"` won't compile as-is — `IsChecked` is `bool?`, not `Visibility`. Implementer should swap to a code-behind handler that toggles `ThemeNameBox.Visibility` on `DeriveThemeCheck.Checked/Unchecked`. Bind to `Visible`/`Collapsed` in the handler.)

### 4b — ProfileDialog code-behind

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ModManager.App.Services;
using ModManager.Core;
using Windows.Storage.Pickers;
using Windows.UI;

namespace ModManager.App;

public sealed partial class ProfileDialog : ContentDialog
{
    private readonly IntPtr _hwnd;
    private readonly AvatarService _avatars;
    private readonly ThemeService _themes;

    private string? _pickedSourcePath;     // the original picked file (decoded for preview + palette)
    private IReadOnlyList<PaletteColor> _palette = Array.Empty<PaletteColor>();

    /// <summary>True when the user applied the avatar or its derived theme — the caller refreshes
    /// the title-bar icon binding and theme list.</summary>
    public bool Changed { get; private set; }

    public ProfileDialog(IntPtr hwnd, AvatarService avatars, ThemeService themes)
    {
        InitializeComponent();
        _hwnd = hwnd;
        _avatars = avatars;
        _themes = themes;

        DeriveThemeCheck.Checked   += (_, _) => ThemeNameBox.Visibility = Visibility.Visible;
        DeriveThemeCheck.Unchecked += (_, _) => ThemeNameBox.Visibility = Visibility.Collapsed;

        // Seed the preview from the current avatar if one is set.
        if (_avatars.HasAvatar)
        {
            PreviewImage.Source = new BitmapImage(new Uri(_avatars.AvatarPngPath));
            FileLabel.Text = "Current avatar";
            RemoveButton.Visibility = Visibility.Visible;
        }
    }

    private async void OnPickImage(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".gif");
        picker.FileTypeFilter.Add(".webp");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        _pickedSourcePath = file.Path;
        FileLabel.Text = System.IO.Path.GetFileName(file.Path);

        try
        {
            // Decode + sample at 64×64 for palette extraction (fast, plenty of signal).
            var (_, rgba64) = await AvatarService.ResizeToSquareAsync(file.Path, 64);
            _palette = PaletteExtractor.Extract(rgba64, 64, 64, k: 5);
            PreviewImage.Source = new BitmapImage(new Uri(file.Path));
            RenderPaletteStrip();
            StatusText.Text = "";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Couldn't read that image: " + ex.Message;
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        _avatars.Delete();
        Changed = true;
        Hide();
    }

    private async void OnApply(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrEmpty(_pickedSourcePath))
        {
            // No new pick — close without doing anything.
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            if (UseAsIconCheck.IsChecked == true)
                await _avatars.ImportAsync(_pickedSourcePath);

            if (DeriveThemeCheck.IsChecked == true && _palette.Count > 0)
            {
                var name = string.IsNullOrWhiteSpace(ThemeNameBox.Text) ? "From avatar" : ThemeNameBox.Text.Trim();
                var raw = PaletteToTheme.Derive(_palette, name);
                var json = SerializeRawTheme(raw);
                _themes.ImportUserTheme(json);
            }

            Changed = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void RenderPaletteStrip()
    {
        PaletteStrip.Children.Clear();
        foreach (var p in _palette)
        {
            PaletteStrip.Children.Add(new Rectangle
            {
                Width = 48, Height = 32,
                RadiusX = 4, RadiusY = 4,
                Fill = new SolidColorBrush(Color.FromArgb(255, p.R, p.G, p.B)),
            });
        }
        PaletteEmpty.Visibility = _palette.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string SerializeRawTheme(RawTheme raw)
    {
        var pairs = raw.Tokens.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\"");
        var bloom = raw.AccentBloom is null
            ? ""
            : $",\"accent_bloom\":{{\"blur\":{raw.AccentBloom.Blur},\"alpha\":{raw.AccentBloom.Alpha}}}";
        return "{" + string.Join(",", pairs) + bloom + "}";
    }
}
```

### 4c — Wire `⋯ More → Profile…`

In `MainWindow.xaml`, inside the `⋯` MenuFlyout, add as the FIRST item (before the existing "Backfill metadata"):

```xml
<MenuFlyoutItem Text="Profile…" Click="OnProfile"
                ToolTipService.ToolTip="Set your avatar — use it as the launcher icon and derive a theme from its colors.">
    <MenuFlyoutItem.Icon>
        <FontIcon Glyph="&#xE77B;" />
    </MenuFlyoutItem.Icon>
</MenuFlyoutItem>
<MenuFlyoutSeparator />
```

In `MainWindow.xaml.cs`, add the handler near the other dialog-opening handlers:

```csharp
private async void OnProfile(object sender, RoutedEventArgs e)
{
    var avatars = App.AppHost.Services.GetRequiredService<Services.AvatarService>();
    var themes  = App.AppHost.Services.GetRequiredService<Services.ThemeService>();
    var hwnd    = WinRT.Interop.WindowNative.GetWindowHandle(this);
    var dialog  = new ProfileDialog(hwnd, avatars, themes) { XamlRoot = Content.XamlRoot };
    await dialog.ShowAsync();
    if (dialog.Changed)
    {
        // Refresh themes list (may have a new derived theme) + the title-bar icon binding.
        ViewModel.RefreshThemes();
        ViewModel.NotifyAppIconChanged();
        // Re-apply the window/taskbar icon: prefer the user's, fall back to the bundled.
        var iconPath = System.IO.File.Exists(avatars.AvatarIcoPath)
            ? avatars.AvatarIcoPath
            : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (System.IO.File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
    }
}
```

### 4d — VM binding for the title-bar icon

In `MainViewModel`, add:

```csharp
private readonly AvatarService _avatars;

// (Add _avatars to the constructor parameter list + assignment.)

/// <summary>URI for the title-bar Image. Returns the user avatar if set; otherwise the bundled
/// icon. Notified when the avatar changes (so the title bar swaps live without restart).</summary>
public string AppIconSource => _avatars.HasAvatar
    ? new Uri(_avatars.AvatarPngPath).AbsoluteUri
    : "ms-appx:///Assets/icon.ico";

public void NotifyAppIconChanged() => OnPropertyChanged(nameof(AppIconSource));

public void RefreshThemes()
{
    _themes.Reload();
    ThemeOptions = _themes.Themes;
    // If the active theme survived the reload, keep it; otherwise pick the default.
    SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Id == SelectedTheme?.Id) ?? _themes.Default;
}
```

Update the MainViewModel constructor to take `AvatarService` and store it. Update DI registration in `App.xaml.cs` accordingly (the AddSingleton<AvatarService>() from Task 3).

### 4e — Title-bar Image source

In `MainWindow.xaml`, the title-bar Image becomes:

```xml
<Image Width="20" Height="20" Source="{x:Bind ViewModel.AppIconSource, Mode=OneWay}" />
```

### 4f — Build + test

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

Expected: BUILD SUCCEEDED. All tests pass.

### 4g — Commit

```bash
git add src/ModManager.App/ProfileDialog.xaml src/ModManager.App/ProfileDialog.xaml.cs \
       src/ModManager.App/MainWindow.xaml src/ModManager.App/MainWindow.xaml.cs \
       src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(ui): Profile dialog — image → derived theme + app icon"
```

---

## Smoke checklist (manual)

After all four tasks, rebuild + launch (kill the running app first):

- [ ] `⋯ More` menu has a `Profile…` item at the top.
- [ ] Profile dialog opens; preview pane is empty initially.
- [ ] Pick an image → preview shows it; palette strip shows 5 swatches.
- [ ] Uncheck "Use as icon", uncheck "Derive theme", click Apply → no-op (sanity).
- [ ] Pick an image, both checked, click Apply → title-bar icon swaps to the image; THEME dropdown has a new "From avatar" entry; selecting it applies the derived colors live (bg darker than text on any palette).
- [ ] Reopen Profile → shows the saved avatar; "Remove avatar" button visible.
- [ ] Remove avatar → title-bar icon reverts to the bundled 626 icon. The user theme stays (it's a saved theme; the user can switch off it via the THEME dropdown).
- [ ] Restart the app → avatar + derived theme both persist.

---

## Self-Review Notes

- **Spec coverage:** Image upload (Task 4), palette extraction (Task 1), palette→theme mapping (Task 2), avatar storage + .ico writer (Task 3), title-bar icon binding (Task 4d/4e), `AppWindow.SetIcon` for window/taskbar icon (Task 4c).
- **No new runtime dependencies.** All image work uses `Windows.Graphics.Imaging.BitmapDecoder` (WinRT, built-in). `.ico` writer is hand-rolled (~30 lines).
- **Honors operating laws:** Status colors don't derive from the image — their meaning stays stable across themes. The derived theme is saved as a normal user theme via `ImportUserTheme`, so it's reversible (user picks any other theme).
- **Test coverage:** 5 PaletteExtractor tests (bicolor, solid, k > N, transparency, empty) + 5 PaletteToTheme tests (all required fields, contrast invariant, accent picks chroma, empty, normalizes). No UI tests — consistent with the repo's WinUI-no-unit-tests pattern.
- **Type consistency:** `PaletteColor` record carries Frequency for future weighting; `PaletteToTheme.Derive` returns a `RawTheme` matching what `ThemeService.ImportUserTheme(json)` consumes after re-serialization.
- **The one judgment call worth naming:** the `RequestedTheme="Dark"` on the dialog matches every other programmatically-styled dialog this session (consistent with the Nexus + Saves dialogs).
