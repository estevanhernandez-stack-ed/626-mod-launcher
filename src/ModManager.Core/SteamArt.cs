using System.IO;

namespace ModManager.Core;

/// <summary>Pure cover-art selection from a Steam librarycache app folder's file list. The IO
/// (enumerating appcache/librarycache/&lt;appid&gt;/) is the App adapter's job; this just chooses.
/// Grounded on the observed layout: a named header.jpg when present, else newer hashed &lt;sha1&gt;.jpg.
/// Returns null when there's no usable image.</summary>
public static class SteamArt
{
    public static string? PickCover(IReadOnlyList<string> filesInAppFolder)
    {
        string? header = null;
        string? anyJpg = null;
        foreach (var f in filesInAppFolder)
        {
            var name = Path.GetFileName(f);
            if (name.Equals("header.jpg", StringComparison.OrdinalIgnoreCase)) { header = f; break; }
            if (anyJpg is null && name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) anyJpg = f;
        }
        return header ?? anyJpg;
    }
}
