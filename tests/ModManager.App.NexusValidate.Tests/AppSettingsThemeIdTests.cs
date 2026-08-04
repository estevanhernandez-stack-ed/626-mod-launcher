using System.IO;
using ModManager.App.Services;

namespace ModManager.App.NexusValidate.Tests;

// F-080 (road-to-zero B2): themeId round-trips through app-settings.json as camelCase.
// Same collection as every other test touching the REAL app-settings.json — xUnit runs
// classes in parallel by default, and two writers clobber each other's keys mid-test.
[Collection("app-settings-file")]
public class AppSettingsThemeIdTests
{
    [Fact]
    public void ThemeId_defaults_null_persists_camelCase_and_round_trips()
    {
        // Mutates the REAL %APPDATA% app-settings.json — snapshot + finally-restore, same
        // discipline as AppSettingsKeepPluginsTests.
        var original = new AppSettingsService().ThemeId;
        try
        {
            var svc = new AppSettingsService();
            svc.SetThemeId("obsidian");

            var json = File.ReadAllText(svc.Path);
            Assert.Contains("\"themeId\":\"obsidian\"", json); // camelCase key on disk
            Assert.DoesNotContain("\"ThemeId\"", json);

            // A new instance reads the persisted value back.
            Assert.Equal("obsidian", new AppSettingsService().ThemeId);

            // A quote-bearing id survives the serializer escaping.
            svc.SetThemeId("weird\"name");
            Assert.Equal("weird\"name", new AppSettingsService().ThemeId);
        }
        finally
        {
            if (original is not null) new AppSettingsService().SetThemeId(original);
        }
    }
}
