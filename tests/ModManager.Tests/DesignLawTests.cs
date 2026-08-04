using System.Text.RegularExpressions;

namespace ModManager.Tests;

// F-048 (vibe-glow wave 3): the shipped design laws from waves 1-3 have no compile-time
// enforcement — a new CornerRadius="6", a 9px label, a hardcoded mono face, raw-opacity
// dimming, or a bare TextBlock in a DataTemplate would regress silently. Each detector is a
// pure function with synthetic positive/negative controls (so a regex typo can't leave the
// suite permanently green), then the file scans run the proven detectors over the App layer.
public class DesignLawTests
{
    // ---- detectors (pure) ----

    private static string StripXmlComments(string text) => Regex.Replace(text, "<!--.*?-->", "", RegexOptions.Singleline);

    internal static IEnumerable<string> FindNonZeroRadiusXaml(string text)
        => Regex.Matches(StripXmlComments(text), "CornerRadius=\"[0-9., ]*[1-9][0-9., ]*\"").Select(m => m.Value);

    internal static IEnumerable<string> FindNonZeroRadiusCode(string text)
        => Regex.Matches(text, @"new CornerRadius\((?!0\))[0-9., ]+\)").Select(m => m.Value);

    internal static IEnumerable<string> FindSubTagFontSizeXaml(string text)
        => Regex.Matches(StripXmlComments(text), "FontSize=\"[0-9](\\.[0-9]+)?\"").Select(m => m.Value);

    internal static IEnumerable<string> FindSubTagFontSizeCode(string text)
        => Regex.Matches(text, @"FontSize = [0-9](\.[0-9]+)?[,;\s]").Select(m => m.Value.Trim());

    internal static IEnumerable<string> FindHardcodedMonoXaml(string text)
        => Regex.Matches(StripXmlComments(text), "FontFamily=\"Consolas\"").Select(m => m.Value);

    internal static IEnumerable<string> FindHardcodedMonoCode(string text)
        => Regex.Matches(text, "FontFamily\\(\"Consolas\"\\)").Select(m => m.Value);

    internal static IEnumerable<string> FindRawOpacityDimming(string text)
        => Regex.Matches(StripXmlComments(text), "<TextBlock\\b[^>]*?/?>", RegexOptions.Singleline)
            .Select(m => m.Value)
            .Where(tag => tag.Contains("Opacity=\"0.") && !tag.Contains("Foreground=") && !tag.Contains("Style="));

    internal static IEnumerable<string> FindUnnamedIconOnlyButtons(string text)
    {
        // A Button whose visible content is icon-only (FontIcon/SymbolIcon, no TextBlock/Run and
        // no string Content=) must carry AutomationProperties.Name — tooltips do not feed the UIA
        // Name, so Narrator announces a bare "button" (vibe-glow F-021).
        var clean = StripXmlComments(text);
        // Self-closing form must match FIRST — otherwise a self-closed sibling swallows the next
        // real Button into one block and its text content masks the icon-only one.
        foreach (Match m in Regex.Matches(clean, "<Button\\b[^>]*?/>|<Button\\b.*?</Button>", RegexOptions.Singleline))
        {
            var block = m.Value;
            var openTag = Regex.Match(block, "<Button\\b[^>]*?>", RegexOptions.Singleline).Value;
            var hasIcon = Regex.IsMatch(block, "<(FontIcon|SymbolIcon)\\b");
            var hasText = block.Contains("<TextBlock") || block.Contains("<Run")
                          || Regex.IsMatch(openTag, "Content=\"[^\"{]");
            if (hasIcon && !hasText && !block.Contains("AutomationProperties.Name"))
                yield return openTag;
        }
    }

    internal static IEnumerable<string> FindUninkedTemplateText(string text)
    {
        var clean = StripXmlComments(text);
        var spans = new List<(int Start, int End)>();
        foreach (Match dt in Regex.Matches(clean, "<DataTemplate\\b"))
        {
            var depth = 0;
            foreach (Match t in Regex.Matches(clean.Substring(dt.Index), "</?DataTemplate\\b"))
            {
                depth += t.Value[1] != '/' ? 1 : -1;
                if (depth == 0) { spans.Add((dt.Index, dt.Index + t.Index + t.Length)); break; }
            }
        }
        foreach (Match m in Regex.Matches(clean, "<TextBlock\\b[^>]*?/?>", RegexOptions.Singleline))
        {
            if (!spans.Any(s => s.Start <= m.Index && m.Index < s.End)) continue;
            if (!m.Value.Contains("Foreground=") && !m.Value.Contains("Style="))
                yield return m.Value;
        }
    }

    // ---- positive/negative controls: every detector must fire on bad and stay quiet on good ----

    public static IEnumerable<object[]> DetectorControls() => new[]
    {
        new object[] { "radius-xaml", "<Border CornerRadius=\"4\" />", "<Border CornerRadius=\"0\" /><Border CornerRadius=\"{ThemeResource ControlCornerRadius}\" /><!-- CornerRadius=\"6\" -->" },
        new object[] { "radius-xaml-tuple", "<Border CornerRadius=\"0,0,4,4\" />", "<Border CornerRadius=\"0,0,0,0\" />" },
        new object[] { "radius-code", "x.CornerRadius = new CornerRadius(6);", "x.CornerRadius = new CornerRadius(0);" },
        new object[] { "fontsize-xaml", "<TextBlock FontSize=\"9\" />", "<TextBlock FontSize=\"10\" /><!-- FontSize=\"9\" -->" },
        new object[] { "fontsize-code", "b.FontSize = 9;", "b.FontSize = 12;" },
        new object[] { "mono-xaml", "<TextBlock FontFamily=\"Consolas\" />", "<TextBlock FontFamily=\"{StaticResource MonoFontFamily}\" />" },
        new object[] { "mono-code", "new FontFamily(\"Consolas\")", "new FontFamily(\"Cascadia Mono, Consolas\")" },
        new object[] { "opacity-dim", "<TextBlock Text=\"x\" Opacity=\"0.6\" />", "<TextBlock Text=\"x\" Opacity=\"0.6\" Foreground=\"{StaticResource ThemeDanger}\" />" },
        new object[] { "template-ink", "<DataTemplate><TextBlock Text=\"x\" /></DataTemplate>", "<DataTemplate><TextBlock Text=\"x\" Foreground=\"{StaticResource ThemeInk}\" /></DataTemplate><DataTemplate><TextBlock Style=\"{x:Null}\" /></DataTemplate><TextBlock Text=\"outside\" />" },
        new object[] { "icon-only-name", "<Button Click=\"X\"><FontIcon Glyph=\"&#xE713;\" /></Button>", "<Button Click=\"X\" AutomationProperties.Name=\"Settings\"><FontIcon Glyph=\"&#xE713;\" /></Button><Button Click=\"Y\"><StackPanel><FontIcon Glyph=\"&#xE721;\" /><TextBlock Text=\"Find\" /></StackPanel></Button><Button Content=\"Plain\" />" },
    };

    private static IEnumerable<string> RunDetector(string id, string sample) => id switch
    {
        "radius-xaml" or "radius-xaml-tuple" => FindNonZeroRadiusXaml(sample),
        "radius-code" => FindNonZeroRadiusCode(sample),
        "fontsize-xaml" => FindSubTagFontSizeXaml(sample),
        "fontsize-code" => FindSubTagFontSizeCode(sample),
        "mono-xaml" => FindHardcodedMonoXaml(sample),
        "mono-code" => FindHardcodedMonoCode(sample),
        "opacity-dim" => FindRawOpacityDimming(sample),
        "template-ink" => FindUninkedTemplateText(sample).ToList(),
        "icon-only-name" => FindUnnamedIconOnlyButtons(sample).ToList(),
        _ => throw new ArgumentOutOfRangeException(id),
    };

    [Theory]
    [MemberData(nameof(DetectorControls))]
    public void Detector_fires_on_bad_and_stays_quiet_on_good(string id, string bad, string good)
    {
        Assert.NotEmpty(RunDetector(id, bad));
        Assert.Empty(RunDetector(id, good));
    }

    // ---- file scans over the App layer ----

    private static string AppRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ModManager.App")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "ModManager.App");
    }

    private static IEnumerable<(string Path, string Text)> Sources(string pattern)
        => Directory.EnumerateFiles(AppRoot(), pattern, SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (p, File.ReadAllText(p)));

    private static List<string> Scan(string pattern, Func<string, IEnumerable<string>> detector)
        => Sources(pattern)
            .SelectMany(s => detector(s.Text).Select(v => $"{Path.GetFileName(s.Path)}: {v.Trim()}"))
            .ToList();

    [Fact]
    public void App_sources_are_non_empty() // guards the guard: a broken glob would green everything
        => Assert.True(Sources("*.xaml").Count() >= 10, "App XAML glob matched suspiciously few files");

    [Fact]
    public void Shape_law_no_nonzero_corner_radius()
        => Assert.Empty(Scan("*.xaml", FindNonZeroRadiusXaml).Concat(Scan("*.cs", FindNonZeroRadiusCode)));

    [Fact]
    public void Type_law_no_font_size_below_the_tag_step()
        => Assert.Empty(Scan("*.xaml", FindSubTagFontSizeXaml).Concat(Scan("*.cs", FindSubTagFontSizeCode)));

    [Fact]
    public void Type_law_mono_face_routes_through_the_resource()
        => Assert.Empty(Scan("*.xaml", FindHardcodedMonoXaml).Concat(Scan("*.cs", FindHardcodedMonoCode)));

    [Fact]
    public void Ink_law_no_raw_opacity_dimming_on_untinted_text()
        => Assert.Empty(Scan("*.xaml", FindRawOpacityDimming));

    [Fact]
    public void Ink_law_data_template_text_declares_its_ink()
        => Assert.Empty(Scan("*.xaml", t => FindUninkedTemplateText(t)));

    [Fact]
    public void A11y_law_icon_only_buttons_carry_a_name()
        => Assert.Empty(Scan("*.xaml", t => FindUnnamedIconOnlyButtons(t)));
}
