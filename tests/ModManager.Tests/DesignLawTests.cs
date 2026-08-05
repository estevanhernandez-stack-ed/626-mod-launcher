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

    // Style= exemption is CONDITIONAL now (F-056): x:Null passes (inherits a themed parent —
    // the F-052 rule), and a StaticResource style passes only when the style actually sets
    // Foreground (the ink-style set is parsed from the App's own resources at scan time; the
    // synthetic controls pass null to keep the pure harness self-contained).
    private static bool StyleExempts(string tag, ISet<string>? inkStyles)
    {
        if (!tag.Contains("Style=")) return false;
        if (tag.Contains("Style=\"{x:Null}\"")) return true;
        var m = Regex.Match(tag, "Style=\"\\{(?:Static|Theme)Resource ([^}\"]+)\\}\"");
        if (!m.Success) return false;
        return inkStyles is null || inkStyles.Contains(m.Groups[1].Value.Trim());
    }

    internal static ISet<string> FindInkStyleKeys(string text)
    {
        // A TextBlock style counts as an INK style when its own body sets Foreground.
        var keys = new HashSet<string>();
        foreach (Match s in Regex.Matches(StripXmlComments(text),
            "<Style\\b[^>]*x:Key=\"([^\"]+)\"[^>]*TargetType=\"TextBlock\"[^>]*>(.*?)</Style>", RegexOptions.Singleline))
        {
            if (s.Groups[2].Value.Contains("Property=\"Foreground\"")) keys.Add(s.Groups[1].Value);
        }
        return keys;
    }

    // F-073: raw-opacity dimming is banned on EVERY text carrier, not just TextBlock —
    // HyperlinkButton and FontIcon dim through token brushes too.
    internal static IEnumerable<string> FindRawOpacityDimming(string text, ISet<string>? inkStyles = null)
        => Regex.Matches(StripXmlComments(text), "<(?:TextBlock|HyperlinkButton|FontIcon|Run)\\b[^>]*?/?>", RegexOptions.Singleline)
            .Select(m => m.Value)
            .Where(tag => tag.Contains("Opacity=\"0.") && !tag.Contains("Foreground=") && !StyleExempts(tag, inkStyles));

    internal static IEnumerable<string> FindRawOpacityDimmingCode(string text)
        // Code-built text dims through the token brushes, same as XAML (wave-7 F-013 close).
        // Any fractional Opacity literal in App code is treated as dimming — the App layer has
        // no legitimate non-token fractional opacity in code today; add an allowlist if one appears.
        => Regex.Matches(text, @"Opacity\s*=\s*0?\.\d+").Select(m => m.Value);

    internal static IEnumerable<string> FindUnnamedIconOnlyButtons(string text)
    {
        // A Button whose visible content is icon-only (FontIcon/SymbolIcon, no TextBlock/Run and
        // no string Content=) must carry AutomationProperties.Name — tooltips do not feed the UIA
        // Name, so Narrator announces a bare "button" (vibe-glow F-021).
        // Depth-stack matching: a lazy <Button.*?</Button> regex is nesting-blind — an outer
        // button swallows its inner button whole and the outer's text masks it (the library-card
        // shape). Property elements (<Button.Flyout>) are not buttons and are skipped.
        // Known gap: glyph-string Content ("&#xE74D;") reads as text — zero instances today.
        var clean = StripXmlComments(text);
        var found = new List<string>();
        var tokens = Regex.Matches(clean,
            "<Button(?![.\\w])[^>]*?/>|<Button(?![.\\w])[^>]*?>|</Button>", RegexOptions.Singleline);
        var stack = new Stack<(string OpenTag, int ContentStart)>();
        foreach (Match t in tokens)
        {
            if (t.Value.StartsWith("</"))
            {
                if (stack.Count == 0) continue;
                var (openTag, contentStart) = stack.Pop();
                // Evaluate the full inner block; nested buttons are judged separately when their
                // own close token pops, and a parent containing a TextBlock anywhere legitimately
                // derives its UIA name from that text.
                Judge(openTag, clean.Substring(contentStart, t.Index - contentStart));
            }
            else if (t.Value.EndsWith("/>")) Judge(t.Value, "");
            else stack.Push((t.Value, t.Index + t.Length));
        }
        return found;

        void Judge(string openTag, string inner)
        {
            // Glyph-string Content ("&#xE74D;") is an ICON, not text (F-067 closed the gap):
            // it renders as a symbol and derives a garbage UIA name.
            var glyphContent = Regex.IsMatch(openTag, "Content=\"&#x");
            var hasIcon = glyphContent || Regex.IsMatch(inner, "<(FontIcon|SymbolIcon)\\b");
            var hasText = inner.Contains("<TextBlock") || inner.Contains("<Run")
                          || (!glyphContent && Regex.IsMatch(openTag, "Content=\"[^\"{]"));
            if (hasIcon && !hasText && !openTag.Contains("AutomationProperties.Name"))
                found.Add(openTag);
        }
    }

    // F-071: a Text="literal" on an x:Name'd element whose code-behind ALSO assigns
    // <name>.Text ships dead copy — the F-027 failure mode (reviewers read the XAML string,
    // users never see it). Conditional reassignment is legitimate (XAML default + override),
    // so confirmed-legit pairs live in the allowlist WITH their justification.
    internal static IEnumerable<string> FindDeadXamlStrings(string xaml, string codeBehind, ISet<string> allow)
    {
        foreach (Match m in Regex.Matches(StripXmlComments(xaml),
            "x:Name=\"(\\w+)\"[^>]*\\bText=\"[^\"{]+\"|\\bText=\"[^\"{]+\"[^>]*x:Name=\"(\\w+)\"", RegexOptions.Singleline))
        {
            var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            if (allow.Contains(name)) continue;
            if (Regex.IsMatch(codeBehind, $@"\b{Regex.Escape(name)}\.Text\s*="))
                yield return name;
        }
    }

    // F-074: the emoji-strip law's carve-out, written down. Any non-ASCII character in a
    // literal Text=/Content= must be on this list — it grew silently before it existed.
    internal static readonly IReadOnlySet<char> GlyphAllowlist = new HashSet<char>
    {
        '—', // — em dash (repo voice)
        '…', // … ellipsis (continuation affordances)
        '→', // → step arrows in install how-tos
        '↻', // ↻ refresh verb glyph
        '♥', // ♥ Nexus endorse heart
    };

    internal static IEnumerable<string> FindDisallowedGlyphs(string text)
        => Regex.Matches(StripXmlComments(text), "(?:Text|Content)=\"([^\"{]*)\"")
            .SelectMany(m => m.Groups[1].Value.Where(ch => ch > 127 && !GlyphAllowlist.Contains(ch)))
            .Select(ch => $"U+{(int)ch:X4} '{ch}'");

    internal static IEnumerable<string> FindUninkedTemplateText(string text, ISet<string>? inkStyles = null)
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
            if (!m.Value.Contains("Foreground=") && !StyleExempts(m.Value, inkStyles))
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
        new object[] { "opacity-dim-code", "new TextBlock { Text = \"x\", Opacity = 0.8 };", "new TextBlock { Text = \"x\", Foreground = brush }; el.Opacity = 1.0;" },
        new object[] { "template-ink", "<DataTemplate><TextBlock Text=\"x\" /></DataTemplate>", "<DataTemplate><TextBlock Text=\"x\" Foreground=\"{StaticResource ThemeInk}\" /></DataTemplate><DataTemplate><TextBlock Style=\"{x:Null}\" /></DataTemplate><TextBlock Text=\"outside\" />" },
        new object[] { "icon-only-name", "<Button Click=\"X\"><FontIcon Glyph=\"&#xE713;\" /></Button>", "<Button Click=\"X\" AutomationProperties.Name=\"Settings\"><FontIcon Glyph=\"&#xE713;\" /></Button><Button Click=\"Y\"><StackPanel><FontIcon Glyph=\"&#xE721;\" /><TextBlock Text=\"Find\" /></StackPanel></Button><Button Content=\"Plain\" /><Button.Flyout><FontIcon Glyph=\"&#xE700;\" /></Button.Flyout>" },
        new object[] { "icon-only-name-nested", "<Button Click=\"Outer\" AutomationProperties.Name=\"Card\"><Grid><TextBlock Text=\"Game\" /><Button Click=\"Inner\"><FontIcon Glyph=\"&#xE768;\" /></Button></Grid></Button>", "<Button Click=\"Outer\" AutomationProperties.Name=\"Card\"><Grid><TextBlock Text=\"Game\" /><Button Click=\"Inner\" AutomationProperties.Name=\"Play\"><FontIcon Glyph=\"&#xE768;\" /></Button></Grid></Button>" },
        new object[] { "icon-only-name-glyph-content", "<Button Click=\"X\" Content=\"&#xE74D;\" />", "<Button Click=\"X\" Content=\"&#xE74D;\" AutomationProperties.Name=\"Uninstall\" /><Button Click=\"Y\" Content=\"Plain words\" />" },
        new object[] { "opacity-dim-carriers", "<FontIcon Glyph=\"&#xE838;\" Opacity=\"0.7\" /><HyperlinkButton Content=\"x\" Opacity=\"0.75\" />", "<FontIcon Glyph=\"&#xE838;\" Foreground=\"{StaticResource ThemeInkSoft}\" /><HyperlinkButton Content=\"x\" Foreground=\"{StaticResource ThemeAccent}\" /><Border Opacity=\"0.5\" />" },
        new object[] { "style-exemption", "<DataTemplate><TextBlock Text=\"x\" Style=\"{StaticResource NotAnInkStyle}\" /></DataTemplate>", "<DataTemplate><TextBlock Text=\"x\" Style=\"{x:Null}\" /></DataTemplate>" },
        new object[] { "glyph-allowlist", "<TextBlock Text=\"nice ✓ done\" />", "<TextBlock Text=\"endorse ♥ — refresh ↻\" /><TextBlock Text=\"{x:Bind Bound}\" />" },
        new object[] { "dead-string", "DEADSTRING-SAMPLE", "DEADSTRING-GOOD" },
    };

    private static IEnumerable<string> RunDetector(string id, string sample) => id switch
    {
        "radius-xaml" or "radius-xaml-tuple" => FindNonZeroRadiusXaml(sample),
        "radius-code" => FindNonZeroRadiusCode(sample),
        "fontsize-xaml" => FindSubTagFontSizeXaml(sample),
        "fontsize-code" => FindSubTagFontSizeCode(sample),
        "mono-xaml" => FindHardcodedMonoXaml(sample),
        "mono-code" => FindHardcodedMonoCode(sample),
        "opacity-dim" or "opacity-dim-carriers" => FindRawOpacityDimming(sample),
        "opacity-dim-code" => FindRawOpacityDimmingCode(sample),
        "template-ink" => FindUninkedTemplateText(sample).ToList(),
        "style-exemption" => FindUninkedTemplateText(sample, new HashSet<string>()).ToList(), // empty ink set: only x:Null passes
        "icon-only-name" or "icon-only-name-nested" or "icon-only-name-glyph-content" => FindUnnamedIconOnlyButtons(sample).ToList(),
        "glyph-allowlist" => FindDisallowedGlyphs(sample).ToList(),
        "dead-string" => sample == "DEADSTRING-SAMPLE"
            ? FindDeadXamlStrings("<TextBlock x:Name=\"Foo\" Text=\"literal\" />", "Foo.Text = \"other\";", new HashSet<string>()).ToList()
            : FindDeadXamlStrings("<TextBlock x:Name=\"Foo\" Text=\"literal\" /><TextBlock x:Name=\"Ok\" Text=\"kept\" />", "Bar.Text = \"other\"; // Ok never assigned", new HashSet<string>()).ToList(),
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

    private static ISet<string> AppInkStyles()
        => Sources("*.xaml").SelectMany(s => FindInkStyleKeys(s.Text)).ToHashSet();

    [Fact]
    public void Ink_law_no_raw_opacity_dimming_on_untinted_text()
    {
        var ink = AppInkStyles();
        Assert.Empty(Scan("*.xaml", t => FindRawOpacityDimming(t, ink)).Concat(Scan("*.cs", FindRawOpacityDimmingCode)));
    }

    [Fact]
    public void Cs_sources_are_non_empty() // companion glob guard for the .cs scan arms
        => Assert.True(Sources("*.cs").Count() >= 10, "App .cs glob matched suspiciously few files");

    [Fact]
    public void Ink_law_data_template_text_declares_its_ink()
    {
        var ink = AppInkStyles();
        Assert.Empty(Scan("*.xaml", t => FindUninkedTemplateText(t, ink)));
    }

    [Fact]
    public void A11y_law_icon_only_buttons_carry_a_name()
        => Assert.Empty(Scan("*.xaml", t => FindUnnamedIconOnlyButtons(t)));

    [Fact]
    public void Copy_law_glyphs_stay_on_the_allowlist() // F-074
        => Assert.Empty(Scan("*.xaml", FindDisallowedGlyphs));

    [Fact]
    public void Copy_law_no_dead_xaml_strings() // F-071 — the F-027 failure mode, mechanized
    {
        // Confirmed-legit conditional overrides go here WITH the reason, or they rot silently.
        var allow = new HashSet<string>
        {
            "CharactersEmpty", // SavesDialog: XAML fallback + conditional error override (F-027 fix shape)
            "Blurb", // DiscoveryReviewDialog: XAML carries the mods blurb; code-behind overrides it
                     // ONLY when every proposal is a ProxyLoader (same conditional-override shape).
        };
        var findings = new List<string>();
        foreach (var (path, xaml) in Sources("*.xaml"))
        {
            var cs = path + ".cs";
            if (!File.Exists(cs)) continue;
            findings.AddRange(FindDeadXamlStrings(xaml, File.ReadAllText(cs), allow)
                .Select(n => $"{Path.GetFileName(path)}: {n}"));
        }
        Assert.Empty(findings);
    }

    [Fact]
    public void Template_pin_squared_controls_matches_the_shipped_sdk() // F-075
    {
        var appDir = AppRoot();
        var csproj = File.ReadAllText(Path.Combine(appDir, "ModManager.App.csproj"));
        var sdk = Regex.Match(csproj, "Include=\"Microsoft\\.WindowsAppSDK\"\\s+Version=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(sdk), "couldn't read the WindowsAppSDK version from the csproj");
        var template = File.ReadAllText(Path.Combine(appDir, "Styles", "SquaredControls.xaml"));
        Assert.True(template.Contains($"Microsoft.WindowsAppSDK {sdk}"),
            $"SquaredControls.xaml header doesn't name WindowsAppSDK {sdk} — the csproj bumped past the "
            + "template snapshot. Re-extract the stock ToggleSwitch template from the new package's "
            + "generic.xaml, re-zero the radii, and update the header (see the file's provenance note).");
    }
}
