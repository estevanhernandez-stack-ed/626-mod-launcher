using System.IO;

namespace ModManager.Core;

/// <summary>Which shape of cover a surface wants: a portrait grid card (the Library home) or a
/// landscape capsule thumbnail (the title-bar switcher).</summary>
public enum CoverShape { Landscape, Portrait }

/// <summary>Pure cover-art selection from a Steam librarycache app folder's file list. The IO
/// (enumerating appcache/librarycache/&lt;appid&gt;/) is the App adapter's job; this just chooses.
/// Steam caches named art files: <c>library_600x900.jpg</c> (2:3 portrait grid), <c>header.jpg</c>
/// (landscape capsule), <c>library_hero.jpg</c> (wide hero). The hashed <c>&lt;sha1&gt;.jpg</c> files
/// are 32x32 app ICONS, not covers — so we pick by KNOWN cover name for the requested shape and return
/// null (→ themed placeholder) when the app has no real cover cached, rather than upscaling a 32px icon
/// into a card. Falls back to the other-shape named cover (a wrong-aspect real cover still beats an
/// icon) before giving up.</summary>
public static class SteamArt
{
    private const string PortraitArt = "library_600x900.jpg";
    private const string HeaderArt = "header.jpg";

    public static string? PickCover(IReadOnlyList<string> filesInAppFolder, CoverShape shape = CoverShape.Landscape)
    {
        var order = shape == CoverShape.Portrait
            ? new[] { PortraitArt, HeaderArt }
            : new[] { HeaderArt, PortraitArt };

        foreach (var wanted in order)
            foreach (var f in filesInAppFolder)
                if (Path.GetFileName(f).Equals(wanted, StringComparison.OrdinalIgnoreCase))
                    return f;

        return null; // no named cover cached — caller shows the themed placeholder, never a 32px icon
    }
}
