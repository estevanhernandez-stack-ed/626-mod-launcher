using ModManager.Core;
using ModManager.Core.Nexus;

namespace ModManager.Tests;

/// <summary>E1, third slice: the Core readers the app and the agent now share.</summary>
public class AppSettingsFileTests : IDisposable
{
    private readonly string _dir = TestSupport.TempDir("mmb-appsettings-");
    private string FilePath => AppSettingsFile.PathIn(_dir);

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    [Fact]
    public void A_missing_file_is_all_defaults_and_says_missing()
    {
        var s = AppSettingsFile.Read(FilePath);

        Assert.Equal("missing", s.FileState);
        Assert.Equal(("solid", true, true, true, false, (string?)null),
            (s.Backdrop, s.AutoUpdateDefinitions, s.AutoCheckModUpdates, s.KeepPluginsUpdated, s.CloseToTray, s.ThemeId));
        Assert.Equal(6, s.Defaulted.Count);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("\"a string\"")]
    public void A_corrupt_file_is_all_defaults_and_says_unreadable(string content)
    {
        File.WriteAllText(FilePath, content);

        var s = AppSettingsFile.Read(FilePath);

        Assert.Equal("unreadable", s.FileState);
        Assert.Equal("solid", s.Backdrop);
        Assert.False(s.CloseToTray);
    }

    [Fact]
    public void Each_key_defaults_on_its_own()
    {
        File.WriteAllText(FilePath, """{ "backdrop": "ACRYLIC", "closeToTray": true, "keepPluginsUpdated": 0, "themeId": "  " }""");

        var s = AppSettingsFile.Read(FilePath);

        Assert.Equal("ok", s.FileState);
        Assert.Equal("acrylic", s.Backdrop);
        Assert.True(s.CloseToTray);
        Assert.True(s.KeepPluginsUpdated);   // 0 is not a bool
        Assert.Null(s.ThemeId);              // blank is no pick
        Assert.Equal(new[] { "autoUpdateDefinitions", "autoCheckModUpdates", "keepPluginsUpdated", "themeId" }, s.Defaulted);
    }

    [Fact]
    public void An_unknown_backdrop_reads_as_solid()
    {
        File.WriteAllText(FilePath, """{ "backdrop": "glass" }""");

        var s = AppSettingsFile.Read(FilePath);

        Assert.Equal("solid", s.Backdrop);
        Assert.Contains("backdrop", s.Defaulted);   // review on #371: say why it is solid
    }

    [Fact]
    public void A_recognised_value_is_not_reported_as_defaulted()
    {
        File.WriteAllText(FilePath, """{ "backdrop": "solid", "themeId": "forge" }""");
        var d = AppSettingsFile.Read(FilePath).Defaulted;
        Assert.DoesNotContain("backdrop", d);
        Assert.DoesNotContain("themeId", d);
    }

    // ---- themes ----

    [Fact]
    public void Picking_the_active_theme_falls_back_to_the_default_and_says_when_a_pick_went_missing()
    {
        var themes = Themes.BuildThemeList(Themes.BuiltinThemes, Array.Empty<(string, RawTheme)>());

        Assert.Equal((Themes.DefaultThemeId, false), (Themes.PickActive(themes, null).Active.Id, Themes.PickActive(themes, null).SavedMissing));
        Assert.Equal((Themes.DefaultThemeId, true), (Themes.PickActive(themes, "gone").Active.Id, Themes.PickActive(themes, "gone").SavedMissing));
        var other = themes.First(t => t.Id != Themes.DefaultThemeId).Id;
        Assert.Equal(other, Themes.PickActive(themes, other).Active.Id);
    }

    [Fact]
    public void Loading_user_themes_names_the_files_it_could_not_read()
    {
        var full = new Dictionary<string, string>(Themes.BuiltinThemes[Themes.DefaultThemeId].Tokens) { ["name"] = "Good" };
        File.WriteAllText(Path.Combine(_dir, "Good.json"), System.Text.Json.JsonSerializer.Serialize(full));
        File.WriteAllText(Path.Combine(_dir, "bad.json"), "{ nope");
        File.WriteAllText(Path.Combine(_dir, "partial.json"), """{ "name": "Partial" }""");

        var load = Themes.LoadUserThemes(_dir);

        Assert.Equal("good", Assert.Single(load.Themes).Id);
        Assert.Equal(new[] { ("bad.json", "not theme JSON") },
            load.Unusable.Where(u => u.File == "bad.json").Select(u => (u.File, u.Reason)));
        var partial = load.Unusable.Single(u => u.File == "partial.json");
        Assert.Contains("bg", partial.Reason);
        Assert.Empty(Themes.LoadUserThemes(Path.Combine(_dir, "no-such-folder")).Themes);
    }

    // ---- the Nexus store, described without opening it ----

    [Fact]
    public void Describing_the_token_store_never_needs_to_decrypt_it()
    {
        var json = NexusTokenStore.Serialize(new NexusTokenSet("a", "r", DateTimeOffset.UtcNow, "public"), "Este", _ => "blob");

        Assert.Equal(new NexusTokenStore.StoreSummary(true, "Este", false, false), NexusTokenStore.Describe(json));
        Assert.Equal(new NexusTokenStore.StoreSummary(false, "Este", false, false),
            NexusTokenStore.Describe(NexusTokenStore.Serialize(null, "Este", _ => "unused")));
        Assert.Equal(new NexusTokenStore.StoreSummary(false, null, true, false), NexusTokenStore.Describe("""{ "apiKey": "k" }"""));
        Assert.Equal(new NexusTokenStore.StoreSummary(false, null, false, true), NexusTokenStore.Describe("{ broken"));
        Assert.Equal(new NexusTokenStore.StoreSummary(false, null, false, true), NexusTokenStore.Describe("""{ "tokensProtected": 42 }"""));
        Assert.Equal(new NexusTokenStore.StoreSummary(false, null, false, true), NexusTokenStore.Describe("""{ "connectedUser": { } }"""));
    }
}
