namespace ModManager.Core.Discovery;

/// <summary>What adopting one proposal will actually achieve.</summary>
public enum AdoptionReach
{
    /// <summary>At least one mod key will be written. This is what adoption is for.</summary>
    NamesAMod,

    /// <summary>Keys resolved, and every one of them already carries an identity adoption must not
    /// overwrite. Nothing to do, and nothing wrong.</summary>
    AlreadyNamed,

    /// <summary>The proposal resolves to no mod key at all — for an archive, its contents hold nothing
    /// this game would list as a mod. Adoption attaches metadata to mods that ARE installed, so there
    /// is nothing here to attach it to. The user's route is to install it, which is a different
    /// action.</summary>
    NothingToNameYet,
}

/// <summary>
/// What adoption can do about a proposal — the decision, lifted out of the apply so the review dialog
/// and the write agree by construction instead of by coincidence.
///
/// <para><b>Why this exists.</b> Adding Monster Hunter Wilds raised the adoption dialog over thirteen
/// Nexus downloads sitting in Fluffy's library with no <c>natives/</c> directory anywhere — downloaded,
/// not deployed, not one of them installed. The dialog was headed <i>"Mods already installed"</i> and
/// offered a button reading <i>"Adopt 13 mods"</i>. Adoption attaches metadata to mods that are
/// installed; none of these were, so the button would have written nothing, and the only honest
/// outcome available was a status line afterwards saying so.</para>
///
/// <para>The apply already knew all three of these cases apart — it computes them to choose which
/// "nothing happened" line to print. It just knew too late to stop the dialog promising thirteen
/// writes it would not make. Same rule, asked earlier.</para>
///
/// <para><b>Adopt is not install.</b> Nothing here decides to install anything; it only lets a surface
/// say which of the two the user is looking at.</para>
/// </summary>
public static class AdoptionReachRules
{
    /// <summary>What adoption will do for one proposal, given the mod keys it resolves to and the
    /// metadata already on disk. <paramref name="writeKeys"/> is the caller's authoritative resolution
    /// (an archive's keys come from its CONTENTS, never its own download filename), because resolving
    /// them means reading the archive and Core does not decide when that I/O happens.</summary>
    public static AdoptionReach For(IReadOnlyList<string>? writeKeys, IReadOnlyDictionary<string, ModMeta>? existing)
    {
        if (writeKeys is null || writeKeys.Count == 0) return AdoptionReach.NothingToNameYet;
        return writeKeys.Any(k => !IsAlreadyIdentified(existing, k))
            ? AdoptionReach.NamesAMod
            : AdoptionReach.AlreadyNamed;
    }

    /// <summary>True when <paramref name="key"/> already has an identity adoption must never overwrite
    /// — a manual match, a Nexus id, or any prior source confidence. Mirrors
    /// <see cref="ModManager.Core.LooseMods.LooseIdentify"/>'s predicate for the same reason: a weaker
    /// tier must never downgrade a stronger existing match.</summary>
    public static bool IsAlreadyIdentified(IReadOnlyDictionary<string, ModMeta>? existing, string key)
        => existing is not null
           && existing.TryGetValue(key, out var meta)
           && (meta.IsManual || meta.NexusModId is not null || meta.SourceConfidence is not null);

    /// <summary>How many of these proposals adoption will actually write for. The number a button
    /// saying "Adopt N mods" has to use — the old count was the number of rows checked, which on the
    /// Wilds case was thirteen and should have been nought.</summary>
    public static int CountThatWillWrite(IEnumerable<AdoptionReach> reaches)
        => reaches.Count(r => r == AdoptionReach.NamesAMod);
}
