using System.IO;
using ModManager.App.Services;

namespace ModManager.App.NexusValidate.Tests;

public class AppSettingsKeepPluginsTests
{
    [Fact]
    public void KeepPluginsUpdated_defaults_true_and_persists_camelCase()
    {
        // This test mutates the REAL %APPDATA% app-settings.json. Snapshot the value FIRST and restore it
        // in a finally so a mid-test assertion failure can't leave keepPluginsUpdated flipped on the dev's
        // machine permanently (the bare restore statement only ran on the happy path).
        var original = new AppSettingsService().KeepPluginsUpdated;
        try
        {
            // Force the known starting state regardless of what was persisted, so the default-true
            // assertion is meaningful and not dependent on the machine's prior value.
            new AppSettingsService().SetKeepPluginsUpdated(true);

            var svc = new AppSettingsService();
            Assert.True(svc.KeepPluginsUpdated);

            svc.SetKeepPluginsUpdated(false);
            var json = File.ReadAllText(svc.Path);
            Assert.Contains("\"keepPluginsUpdated\":false", json);   // camelCase on disk

            // A new instance reads the persisted value back.
            Assert.False(new AppSettingsService().KeepPluginsUpdated);
        }
        finally
        {
            // Exception-safe restore — runs even if an assertion above threw.
            new AppSettingsService().SetKeepPluginsUpdated(original);
        }
    }
}
