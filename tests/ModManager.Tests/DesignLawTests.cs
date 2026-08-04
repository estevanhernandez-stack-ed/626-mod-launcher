using System.Text.RegularExpressions;

namespace ModManager.Tests;

// F-048 (vibe-glow wave 3): the shipped design laws from waves 1-3 have no compile-time
// enforcement — a new CornerRadius="6", a 9px label, a hardcoded mono face, raw-opacity
// dimming, or a bare TextBlock in a DataTemplate would regress silently. This suite lints
// the App-layer sources so the laws fail loud instead.
public class DesignLawTests
{
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

    private static string Offend(string path, Match m) => $"{Path.GetFileName(path)}: {m.Value.Trim()}";

    [Fact]
    public void Shape_law_no_nonzero_corner_radius()
    {
        var offenders = new List<string>();
        foreach (var (path, text) in Sources("*.xaml"))
            foreach (Match m in Regex.Matches(text, "CornerRadius=\"[0-9., ]*[1-9][0-9., ]*\""))
                offenders.Add(Offend(path, m));
        foreach (var (path, text) in Sources("*.cs"))
            foreach (Match m in Regex.Matches(text, @"new CornerRadius\((?!0\))[0-9., ]+\)"))
                offenders.Add(Offend(path, m));
        Assert.Empty(offenders);
    }

    [Fact]
    public void Type_law_no_font_size_below_the_tag_step()
    {
        var offenders = new List<string>();
        foreach (var (path, text) in Sources("*.xaml"))
            foreach (Match m in Regex.Matches(text, "FontSize=\"[0-9](\\.[0-9]+)?\""))
                offenders.Add(Offend(path, m));
        Assert.Empty(offenders);
    }

    [Fact]
    public void Type_law_mono_face_routes_through_the_resource()
    {
        var offenders = new List<string>();
        foreach (var (path, text) in Sources("*.xaml"))
            foreach (Match m in Regex.Matches(text, "FontFamily=\"Consolas\""))
                offenders.Add(Offend(path, m));
        foreach (var (path, text) in Sources("*.cs"))
            foreach (Match m in Regex.Matches(text, "FontFamily\\(\"Consolas\"\\)"))
                offenders.Add(Offend(path, m));
        Assert.Empty(offenders);
    }

    [Fact]
    public void Ink_law_no_raw_opacity_dimming_on_untinted_text()
    {
        // Dimming goes through ThemeInkSoft/Dim/Muted (the token contract), never a raw
        // opacity over default ink. Opacity WITH an explicit Foreground is a deliberate tint.
        var offenders = new List<string>();
        foreach (var (path, text) in Sources("*.xaml"))
        {
            foreach (Match m in Regex.Matches(text, "<TextBlock\\b[^>]*?/?>", RegexOptions.Singleline))
            {
                var tag = m.Value;
                if (tag.Contains("Opacity=\"0.") && !tag.Contains("Foreground=") && !tag.Contains("Style="))
                    offenders.Add(Offend(path, m));
            }
        }
        Assert.Empty(offenders);
    }

    [Fact]
    public void Ink_law_data_template_text_declares_its_ink()
    {
        // Implicit TextBlock styles do not reach DataTemplate-realized TextBlocks (proven
        // per-pixel in the wave-2 re-review) — template text must carry Foreground or Style.
        var offenders = new List<string>();
        foreach (var (path, text) in Sources("*.xaml"))
        {
            var spans = new List<(int Start, int End)>();
            foreach (Match dt in Regex.Matches(text, "<DataTemplate\\b"))
            {
                var depth = 0;
                foreach (Match t in Regex.Matches(text.Substring(dt.Index), "</?DataTemplate\\b"))
                {
                    depth += t.Value[1] != '/' ? 1 : -1;
                    if (depth == 0) { spans.Add((dt.Index, dt.Index + t.Index + t.Length)); break; }
                }
            }
            foreach (Match m in Regex.Matches(text, "<TextBlock\\b[^>]*?/?>", RegexOptions.Singleline))
            {
                if (!spans.Any(s => s.Start <= m.Index && m.Index < s.End)) continue;
                var tag = m.Value;
                if (!tag.Contains("Foreground=") && !tag.Contains("Style="))
                    offenders.Add(Offend(path, m));
            }
        }
        Assert.Empty(offenders);
    }
}
