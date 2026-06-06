using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Single source of truth for "is this pak the base game or a mod" when both share one folder (a
/// loader-less UE-pak game like Witchfire drops mods straight into Content/Paks alongside the game's
/// own paks). Base-game paks must never be listed as mods or moved by a toggle. Pure — name + size only,
/// no IO. The two signals are OR'd: a conventional shipping name OR an implausibly large size means base.
/// </summary>
public static class PakClassifier
{
    // UE packaged-game convention: pakchunk<N>[optional]-WindowsNoEditor.pak. Case-insensitive.
    private static readonly Regex ShippingPakName =
        new(@"^pakchunk\d+.*-WindowsNoEditor\.pak$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A pak no real mod reaches — above this it's treated as base game even if the name doesn't
    /// match the shipping convention. Well above any real mod pak, below the multi-GB base chunks.</summary>
    public const long ModSizeCeilingBytes = 1536L * 1024 * 1024; // 1.5 GB

    /// <summary>True when <paramref name="fileName"/> + <paramref name="sizeBytes"/> indicate a base-game
    /// pak (hide + protect). Name pattern OR size — see the class summary.</summary>
    public static bool IsBaseGamePak(string fileName, long sizeBytes)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var name = System.IO.Path.GetFileName(fileName);
        return ShippingPakName.IsMatch(name) || sizeBytes >= ModSizeCeilingBytes;
    }
}
