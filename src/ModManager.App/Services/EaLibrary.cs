using System.IO;
using Microsoft.Win32;
using ModManager.Core;
using ModManager.Core.Stores;

namespace ModManager.App.Services;

/// <summary>
/// EA app installs, read from the registry and each install's installerdata. Read-only: registry reads
/// and one file read per install. It never writes a key, never starts or queries the EA app, and returns
/// an empty list on any failure. Parsing and the install rule live in Core (<see cref="EaInstallScan"/>).
/// </summary>
public sealed class EaLibrary : IStoreLibrary
{
    // The registry parent is the publisher, and varies by title.
    private static readonly string[] Publishers = { "EA Sports", "EA Games", "Electronic Arts" };

    public string StoreKind => EaInstallScan.StoreKind;

    public IReadOnlyList<InstalledGame> InstalledGames()
    {
        try { return EaInstallScan.Scan(RegistryInstalls(), ReadText); }
        catch { return Array.Empty<InstalledGame>(); }
    }

    // No local EA art cache was found.
    public string? ResolveCoverArtPath(string appId) => null;
    public string? ResolveCoverArtPath(string appId, CoverShape shape) => null;

    private static IEnumerable<EaRegistryInstall> RegistryInstalls()
    {
        var found = new List<EaRegistryInstall>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                foreach (var publisher in Publishers)
                {
                    using var parent = hklm.OpenSubKey(@"SOFTWARE\" + publisher);
                    if (parent is null) continue;
                    foreach (var title in parent.GetSubKeyNames())
                    {
                        try
                        {
                            using var k = parent.OpenSubKey(title);
                            found.Add(new EaRegistryInstall(k?.GetValue("DisplayName") as string, k?.GetValue("Install Dir") as string));
                        }
                        catch { /* one unreadable key does not hide the rest */ }
                    }
                }
            }
            catch { /* view unavailable */ }
        }
        return found;
    }

    private static string? ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }
    }
}
