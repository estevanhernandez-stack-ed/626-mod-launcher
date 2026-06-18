using System.IO;
using ModManager.App.Services;

namespace ModManager.App.NexusValidate.Tests;

public class AppSettingsKeepPluginsTests
{
    [Fact]
    public void KeepPluginsUpdated_defaults_true_and_persists_camelCase()
    {
        var svc = new AppSettingsService();          // fresh machine default
        Assert.True(svc.KeepPluginsUpdated);

        svc.SetKeepPluginsUpdated(false);
        var json = File.ReadAllText(svc.Path);
        Assert.Contains("\"keepPluginsUpdated\":false", json);   // camelCase on disk

        // A new instance reads the persisted value back.
        Assert.False(new AppSettingsService().KeepPluginsUpdated);

        new AppSettingsService().SetKeepPluginsUpdated(true);     // restore default for other tests
    }
}
