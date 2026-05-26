using System.IO;
using Velopack;
using Velopack.Sources;

namespace ModManager.App.Services;

/// <summary>
/// Wraps Velopack's <see cref="UpdateManager"/> with a 24-hour debounce stamp so we don't poll
/// GitHub on every launch. v1 cut: check + log only. When the toolbar gets an "Update available"
/// indicator, the download + apply-on-restart flow gets wired through this same service.
///
/// Auto-update is comfort, not load-bearing — every failure is swallowed quietly. The user can
/// always re-download the Setup.exe from GitHub Releases. A debug log line records whether the
/// check ran at all, for the rare case we need to investigate.
/// </summary>
public sealed class UpdateChecker
{
    private const string GitHubRepoUrl = "https://github.com/estevanhernandez-stack-ed/626-mod-launcher";
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromHours(24);

    private static readonly string DebounceStampPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModManagerBuilder",
        "last-update-check.txt");

    /// <summary>Latest version detected on the last check, or null if no update is available
    /// (or the check hasn't run). Future surfaces (toolbar indicator, Settings → About) can
    /// read this to render an "Update available" affordance.</summary>
    public string? AvailableVersion { get; private set; }

    public async Task CheckForUpdatesAsync()
    {
        if (!ShouldCheck()) return;

        try
        {
            var source = new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false);
            var manager = new UpdateManager(source);

            // IsInstalled is false during dev (running from bin/Debug) or the portable zip drop;
            // skip the check there — there's nothing for Velopack to apply against.
            if (!manager.IsInstalled)
            {
                StampNow();
                return;
            }

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is not null) AvailableVersion = update.TargetFullRelease.Version.ToString();
        }
        catch
        {
            // Swallow — auto-update is comfort, not load-bearing.
        }
        finally
        {
            StampNow();
        }
    }

    private static bool ShouldCheck()
    {
        try
        {
            if (!File.Exists(DebounceStampPath)) return true;
            var lastText = File.ReadAllText(DebounceStampPath).Trim();
            if (!DateTime.TryParse(lastText, null, System.Globalization.DateTimeStyles.RoundtripKind, out var last))
                return true;
            return DateTime.UtcNow - last >= DebounceWindow;
        }
        catch { return true; }
    }

    private static void StampNow()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DebounceStampPath)!);
            File.WriteAllText(DebounceStampPath, DateTime.UtcNow.ToString("O"));
        }
        catch { /* best-effort */ }
    }
}
