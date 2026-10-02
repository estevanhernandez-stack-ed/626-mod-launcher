namespace ModManager.Core.Discovery;

/// <summary>
/// What a review row says about one adoption proposal. ONE spelling, read by both review surfaces
/// (<c>DiscoveryReviewDialog</c> and <c>IdentifyReviewDialog</c>), so the same finding cannot be
/// described two ways. Their comments used to claim the wording "can't drift" while it already had:
/// one said "Exact match by file hash.", the other "Matched exactly by file hash." (C4).
/// </summary>
public static class AdoptionReviewText
{
    public static string Headline(AdoptionProposal p)
    {
        var name = p.Candidate.FileName;
        return (IsLoader(p), IsInert(p), IsIdentified(p)) switch
        {
            (true, _, _) => $"{name} — mod loader",
            (_, true, true) => $"{name} — {p.Title} (downloaded, not installed)",
            (_, true, false) => $"{name} — downloaded, not installed",
            (_, _, true) => $"{name} — {p.Title}",
            _ => $"{name} — not identified",
        };
    }

    public static string Detail(AdoptionProposal p)
    {
        var at = p.Candidate.RelativePath;
        var by = p.Author is null ? "" : $" · by {p.Author}";
        return (IsLoader(p), IsInert(p), p.Reach == AdoptionReach.AlreadyNamed, p.Evidence) switch
        {
            // A loader is described as what it is rather than as an unidentified mod. Saying "not
            // identified" about a version.dll implies we failed to name something nameable; we didn't.
            (true, _, _, _) => $"Found at {at}. This is the loader other mods ride on, not a mod itself. Several different loaders ship under this filename, so it can't be named from the file alone.",
            // Adoption attaches metadata to mods that ARE installed; a download never deployed has
            // nothing to attach to (A14).
            (_, true, _, _) => $"Found at {at}. This is the download, not an installed mod — nothing from it is in the game folder. Adopting names mods that are already installed, so it can't help here. Drop the file on the window to install it, and it'll be listed.",
            (_, _, true, _) => $"Found at {at}. Already named — nothing to add.",
            (_, _, _, AdoptionEvidence.Md5) => $"Matched exactly by file hash. {at}",
            // A live search is a name match too. It used to fall through to the unidentified line
            // ("Adopt it to manage it anyway") on a row that was pre-checked as identified.
            (_, _, _, AdoptionEvidence.NameIndex or AdoptionEvidence.NameSearch) => $"Matched by name{by}. {at}",
            _ => $"Found at {at}. Adopt it to manage it anyway.",
        };
    }

    /// <summary>Whether a row starts ticked: an identification adoption can write for. One rule for
    /// both review dialogs, like the wording.</summary>
    public static bool PreChecked(AdoptionProposal p)
        => IsIdentified(p) && !IsInert(p) && p.Reach != AdoptionReach.AlreadyNamed;

    /// <summary>Whether approving the row writes anything, so "Apply N" counts changes, not ticks
    /// (A14). An unresolved reach keeps the optimistic answer rather than hiding a row.</summary>
    public static bool WillWrite(AdoptionProposal p) => p.Reach is null or AdoptionReach.NamesAMod;

    /// <summary>What an adoption row adds when the name search found the same mod (C5): whether it
    /// agrees. A different name is a disagreement and is said as one, never as corroboration.</summary>
    public static string NameSearchAlso(AdoptionProposal p, IEnumerable<string> names)
    {
        var distinct = names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 0) return "";
        var disagreeing = distinct.Where(n => !string.Equals(n, p.Title, StringComparison.OrdinalIgnoreCase)).ToList();
        return disagreeing.Count == 0
            ? " The name search agrees."
            : $" The name search suggested {string.Join(", ", disagreeing)} instead; it is listed below if you'd rather use it.";
    }

    /// <summary>The detail for a name match whose mod an adoption above already names (C5).</summary>
    public static string CoveredNameMatch(AdoptionProposal coverer)
        => $"Already named by the {EvidenceNoun(coverer.Evidence)} above ({coverer.Candidate.FileName}). This applies only if you untick that one.";

    private static string EvidenceNoun(AdoptionEvidence e) => e == AdoptionEvidence.Md5 ? "file-hash match" : "match";

    private static bool IsIdentified(AdoptionProposal p) => p.Evidence != AdoptionEvidence.None;
    private static bool IsLoader(AdoptionProposal p) => p.Candidate.Kind == DiscoveryKind.ProxyLoader;
    private static bool IsInert(AdoptionProposal p) => p.Reach == AdoptionReach.NothingToNameYet;
}
