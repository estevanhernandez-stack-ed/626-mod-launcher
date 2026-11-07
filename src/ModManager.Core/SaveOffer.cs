namespace ModManager.Core;

/// <summary>What one save file holds, as far as the offer needs to know. <see cref="Characters"/> is null when the
/// file exists but couldn't be read: unknown, never guessed at.</summary>
public sealed record SaveFileFacts(string FileName, bool Exists, int? Characters, DateTime LastWriteUtc,
    string? FirstCharacter = null, int? FirstLevel = null);

/// <summary>How loudly a copy warns. Only copies into the vanilla save warn (save-type offer, decisions 1 and 2).</summary>
public enum SaveOfferWarning
{
    None,
    /// <summary>A line in the dialog; the copy is still one click (Seamless into vanilla).</summary>
    Warn,
    /// <summary>The line plus a box the player must tick before the copy enables (Reforged, Alt Saves into vanilla).</summary>
    Acknowledge,
}

public sealed record SaveOfferSource(SaveType Type, SaveFileFacts File, SaveOfferWarning Warning);

/// <summary>An offer to make: the save the launch reads, and the saves from other modes that hold characters,
/// newest first. The first is the default choice.</summary>
public sealed record SaveOfferPlan(SaveType Target, string TargetFileName, bool TargetExists, IReadOnlyList<SaveOfferSource> Sources);

/// <summary>
/// Whether a launch should offer to copy a save over from another mode (spec 2026-11-07-save-type-offer-design).
/// Pure: the file facts come in through a function, so this decides and never touches the disk.
///
/// <para>It only ever offers when the save the launch reads has <b>no</b> characters. A FromSoft save file holds
/// every slot and the profile, so the copy replaces all of it, and a target with even one character is off-limits.</para>
/// </summary>
public static class SaveOffer
{
    /// <summary>The save extension a launch will read, or null when 626 can't say (then nothing is offered).
    /// Vanilla reads the vanilla type; Seamless's launcher reads the co-op type; Reforged's launcher reads what
    /// its altsaves.toml names, when the game declares that type and the launch isn't an alt save profile.</summary>
    public static string? TargetExtension(LaunchMode mode, LaunchTarget? target, ReforgedLaunchInfo? reforged,
        IReadOnlyList<SaveType> types)
    {
        if (mode == LaunchMode.Vanilla) return OfKind(types, SaveTypeKind.Vanilla);
        if (reforged is not null)
        {
            if (reforged.AltProfile || reforged.SaveExtension is not { } ext) return null;
            return types.Any(t => SameExt(t.Extension, ext)) ? ext : null;
        }
        if (target is not null && IsSeamlessLauncher(target.Target)) return OfKind(types, SaveTypeKind.Coop);
        return null;
    }

    public static SaveOfferPlan? Evaluate(IReadOnlyList<SaveType> types, SaveLayout layout, string? targetExt,
        Func<string, SaveFileFacts> facts, string baseName, IReadOnlyCollection<string> dismissed, bool writesRefused)
    {
        if (layout != SaveLayout.TypedFiles || writesRefused || targetExt is null) return null;
        var target = types.FirstOrDefault(t => SameExt(t.Extension, targetExt));
        if (target is null || dismissed.Any(d => SameExt(d, target.Extension))) return null;

        // The target first: on most launches it already has characters and nothing else is read.
        var targetName = baseName + target.Extension;
        var targetFacts = facts(targetName);
        if (targetFacts.Exists && targetFacts.Characters is not 0) return null;   // unreadable, or has a character

        var sources = new List<SaveOfferSource>();
        foreach (var type in types)
        {
            if (ReferenceEquals(type, target) || SameExt(type.Extension, target.Extension)) continue;
            var f = facts(baseName + type.Extension);
            if (f.Exists && f.Characters > 0) sources.Add(new SaveOfferSource(type, f, WarningFor(type, target)));
        }
        if (sources.Count == 0) return null;
        var ordered = sources.Select((s, i) => (s, i))
            .OrderByDescending(x => x.s.File.LastWriteUtc).ThenBy(x => x.i).Select(x => x.s).ToList();
        return new SaveOfferPlan(target, targetName, targetFacts.Exists, ordered);
    }

    /// <summary>Copies into vanilla warn: a co-op save warns, an overhaul (or anything of unknown kind) needs an
    /// acknowledgment, a vanilla save is already vanilla. Copies into a modded save never warn: modded online play
    /// runs on its own servers.</summary>
    public static SaveOfferWarning WarningFor(SaveType source, SaveType target)
    {
        if (!IsKind(target, SaveTypeKind.Vanilla) || IsKind(source, SaveTypeKind.Vanilla)) return SaveOfferWarning.None;
        return IsKind(source, SaveTypeKind.Coop) ? SaveOfferWarning.Warn : SaveOfferWarning.Acknowledge;
    }

    private static bool IsSeamlessLauncher(string? path)
    {
        if (!LaunchTargetPicker.IsModLauncher(path)) return false;
        var name = Path.GetFileName(path!.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));
        return name.Contains("ersc_launcher", StringComparison.OrdinalIgnoreCase)
            || name.Contains("seamless", StringComparison.OrdinalIgnoreCase);
    }

    private static string? OfKind(IReadOnlyList<SaveType> types, string kind)
        => types.FirstOrDefault(t => IsKind(t, kind))?.Extension;

    private static bool IsKind(SaveType t, string kind) => string.Equals(t.Kind, kind, StringComparison.OrdinalIgnoreCase);

    private static bool SameExt(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What the player chose in the save-type offer at Play.</summary>
public enum SaveOfferChoice
{
    /// <summary>Snapshot the save folder, copy <see cref="SaveOfferAnswer.Source"/> over the target, then launch.</summary>
    CopyAndPlay,
    /// <summary>Launch with nothing changed.</summary>
    PlayWithout,
    /// <summary>Launch with nothing changed, and stop offering for this game and this target type.</summary>
    DontAsk,
}

/// <summary>The dialog's answer. <see cref="Source"/> is set for <see cref="SaveOfferChoice.CopyAndPlay"/>.</summary>
public sealed record SaveOfferAnswer(SaveOfferChoice Choice, SaveOfferSource? Source = null);
