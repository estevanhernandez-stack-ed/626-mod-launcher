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

    /// <summary>What an adoption row adds when the name search found the same mod (C5): the second
    /// piece of evidence, named, on the one row that will write.</summary>
    public static string AlsoNamed(IEnumerable<string> names)
    {
        var distinct = names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 0 ? "" : $" The name search found it too, as {string.Join(", ", distinct)}.";
    }

    private static bool IsIdentified(AdoptionProposal p) => p.Evidence != AdoptionEvidence.None;
    private static bool IsLoader(AdoptionProposal p) => p.Candidate.Kind == DiscoveryKind.ProxyLoader;
    private static bool IsInert(AdoptionProposal p) => p.Reach == AdoptionReach.NothingToNameYet;
}
