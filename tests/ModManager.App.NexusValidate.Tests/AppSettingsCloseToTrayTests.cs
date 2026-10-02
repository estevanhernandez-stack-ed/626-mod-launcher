using System.IO;
using ModManager.App.Services;

namespace ModManager.App.NexusValidate.Tests;

[Collection("app-settings-file")] // serialize with every other real-file writer (see ThemeIdTests)
public class AppSettingsCloseToTrayTests
{
    [Fact]
    public void CloseToTray_persists_camelCase_and_raises_its_event()
    {
        // Mutates the REAL %APPDATA% app-settings.json: snapshot first, restore in the finally.
        var original = new AppSettingsService().CloseToTray;
        try
        {
            new AppSettingsService().SetCloseToTray(false);

            var svc = new AppSettingsService();
            Assert.False(svc.CloseToTray);

            var raised = 0;
            svc.CloseToTrayChanged += (_, _) => raised++;
            svc.SetCloseToTray(true);
            svc.SetCloseToTray(true);   // no change, no event
            Assert.Equal(1, raised);

            var json = File.ReadAllText(svc.Path);
            Assert.Contains("\"closeToTray\":true", json);   // camelCase on disk
            Assert.DoesNotContain("\"CloseToTray\"", json);

            Assert.True(new AppSettingsService().CloseToTray);
        }
        finally
        {
            new AppSettingsService().SetCloseToTray(original);
        }
    }

    [Fact]
    public void CloseToTray_defaults_off_when_the_file_never_mentioned_it()
    {
        // A settings file written before B1 has no closeToTray key: closing must still mean closing.
        var svc = new AppSettingsService();
        var path = svc.Path;
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"backdrop\":\"solid\",\"autoUpdateDefinitions\":true}");
            Assert.False(new AppSettingsService().CloseToTray);
        }
        finally
        {
            if (backup is null) File.Delete(path); else File.WriteAllText(path, backup);
        }
    }
}
