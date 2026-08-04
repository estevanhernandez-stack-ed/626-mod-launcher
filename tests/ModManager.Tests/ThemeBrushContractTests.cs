using System.Text.RegularExpressions;

namespace ModManager.Tests;

// vibe-glow wave 1: the theming design rests on a three-way contract between App.xaml's brush
// declarations, ThemeService.Apply's Set calls, and DialogTheming.SharedKeys — all referring to
// the same single mutable instances. Set() and DialogTheming both silently no-op on a missing
// key, so a typo ships as a dead override with no compile error. This test parses the three
// sources and fails loud when they drift.
public class ThemeBrushContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "ModManager.App", "App.xaml")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static HashSet<string> AppXamlBrushKeys(string root)
    {
        var xaml = File.ReadAllText(Path.Combine(root, "src", "ModManager.App", "App.xaml"));
        return Regex.Matches(xaml, "<SolidColorBrush x:Key=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToHashSet();
    }

    private static HashSet<string> AppXamlSharedResourceKeys(string root)
    {
        // DialogTheming may share non-brush resources too (CornerRadius, wave 7) — the subset
        // check accepts any keyed App.xaml resource, while the brush/Apply equality stays strict.
        var xaml = File.ReadAllText(Path.Combine(root, "src", "ModManager.App", "App.xaml"));
        return Regex.Matches(xaml, "<(?:SolidColorBrush|CornerRadius|FontFamily|x:Double) x:Key=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToHashSet();
    }

    private static List<string> ThemeServiceSetKeys(string root)
    {
        var cs = File.ReadAllText(Path.Combine(root, "src", "ModManager.App", "Services", "ThemeService.cs"));
        return Regex.Matches(cs, "Set\\(res, \"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToList();
    }

    private static List<string> DialogSharedKeys(string root)
    {
        var cs = File.ReadAllText(Path.Combine(root, "src", "ModManager.App", "Services", "DialogTheming.cs"));
        var array = Regex.Match(cs, "SharedKeys\\s*=\\s*\\{(.*?)\\};", RegexOptions.Singleline);
        Assert.True(array.Success, "SharedKeys array not found in DialogTheming.cs");
        return Regex.Matches(array.Groups[1].Value, "\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToList();
    }

    [Fact]
    public void App_xaml_brush_keys_are_unique()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "ModManager.App", "App.xaml"));
        var all = Regex.Matches(xaml, "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        var dupes = all.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
    }

    [Fact]
    public void ThemeService_sets_exactly_the_app_xaml_brushes()
    {
        var root = RepoRoot();
        var xamlKeys = AppXamlBrushKeys(root);
        var setKeys = ThemeServiceSetKeys(root);

        var setDupes = setKeys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(setDupes);

        var setMissingFromXaml = setKeys.Except(xamlKeys).ToList();     // Set silently no-ops on these
        var xamlNeverSet = xamlKeys.Except(setKeys).ToList();           // first-paint-only colors
        Assert.True(setMissingFromXaml.Count == 0,
            $"ThemeService sets keys App.xaml never declares (silent no-ops): {string.Join(", ", setMissingFromXaml)}");
        Assert.True(xamlNeverSet.Count == 0,
            $"App.xaml brushes never re-colored by Apply (first-paint-only): {string.Join(", ", xamlNeverSet)}");
    }

    [Fact]
    public void DialogTheming_shared_keys_all_exist_in_app_xaml()
    {
        var root = RepoRoot();
        var xamlKeys = AppXamlSharedResourceKeys(root);
        var shared = DialogSharedKeys(root);

        var sharedDupes = shared.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(sharedDupes);

        var missing = shared.Except(xamlKeys).ToList();                 // Apply() silently skips these
        Assert.True(missing.Count == 0,
            $"DialogTheming.SharedKeys not declared in App.xaml (silently skipped): {string.Join(", ", missing)}");
    }
}
