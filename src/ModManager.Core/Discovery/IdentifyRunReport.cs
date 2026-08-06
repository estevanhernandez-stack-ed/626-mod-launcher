namespace ModManager.Core.Discovery;

/// <summary>
/// What one unified identify run actually did. Scalars only — no <c>GameContext</c>, no view-model
/// state, nothing that touches disk — so the sentence the user reads is decided by a pure function
/// with tests behind it.
/// </summary>
/// <param name="Adopted">Mod keys the adoption apply actually wrote. Not the count approved in the
/// dialog: an approved archive whose contents map to no installed mod key writes nothing.</param>
/// <param name="Named">Rows the name-search apply actually wrote. Not the size of the batch handed
/// in — a refused apply writes zero.</param>
/// <param name="Filled">Rows the fill-blanks pass wrote. It writes its partial batch when the user
/// stops, which is why this is carried into every terminal line, cancelled or not.</param>
/// <param name="DroppedNameMatches">Approved name matches that <see cref="LooseMods.LooseIdentify.ExcludeKeys"/>
/// removed because an earlier pass in the same run already wrote that key.</param>
/// <param name="Stopped">The user pressed Stop at some point during the run.</param>
/// <param name="AdoptionNote">The adoption apply's own explanation, carried when it wrote nothing
/// despite having approvals. It distinguishes three different zero-write reasons; a composed count
/// would throw that away.</param>
/// <param name="IdentifyNote">The name-search apply's own explanation, carried when it refused.</param>
/// <param name="DownloadsNote">What the downloads-folder pass contributed, from
/// <see cref="IdentifyRunReport.DownloadsFolderNote"/>.</param>
/// <param name="NothingHappenedLine">What to say when no pass wrote anything and the run was not
/// stopped. The caller supplies it because only the caller knows WHY nothing happened — a gated-out
/// pass has already explained itself, and the run must not replace that with a claim it never
/// tested.</param>
public sealed record IdentifyRunOutcome
{
    public int Adopted { get; init; }
    public int Named { get; init; }
    public int Filled { get; init; }
    public int DroppedNameMatches { get; init; }
    public bool Stopped { get; init; }
    public string? AdoptionNote { get; init; }
    public string? IdentifyNote { get; init; }
    public string? DownloadsNote { get; init; }
    public string? NothingHappenedLine { get; init; }
}

/// <summary>
/// The unified identify run's terminal status line.
///
/// <para>Every pass writes its own status as it goes — correct for the Advanced menu entries, which
/// run alone — so by the end of a four-pass run only the LAST pass's line would survive, and it
/// would speak for the whole run. This composes what actually landed instead.</para>
///
/// <para>The one law: <b>never say less than what was written.</b> The fill pass writes its partial
/// batch on cancellation, an adoption can land while a later name-search apply is refused, and an
/// md5-matched archive can expand to zero write keys — each of those makes "nothing happened" a lie
/// that is easy to reach by accident. That is why this lives in Core behind tests rather than in the
/// view model with the dialogs.</para>
/// </summary>
public static class IdentifyRunReport
{
    private static string S(int n) => n == 1 ? "" : "s";

    public static string Summarize(IdentifyRunOutcome o)
    {
        // Lowercase verb phrases so they join cleanly in any combination; the joined clause gets its
        // capital once, below. Pre-capitalizing each phrase breaks the moment the first pass that
        // did something is not the first pass in this list.
        var did = new List<string>();
        if (o.Adopted > 0) did.Add($"adopted {o.Adopted} mod{S(o.Adopted)}");
        if (o.Named > 0) did.Add($"named {o.Named} mod{S(o.Named)}");
        if (o.Filled > 0) did.Add($"filled in details for {o.Filled} mod{S(o.Filled)}");

        var clause = did.Count switch
        {
            0 => "",
            1 => did[0],
            _ => $"{string.Join(", ", did.Take(did.Count - 1))} and {did[^1]}",
        };

        var parts = new List<string>();

        if (did.Count == 0)
        {
            // A stopped run never finished looking, so it must not present a "nothing found"
            // conclusion as verified — even when the caller offers one.
            //
            // When an apply already explained why it wrote nothing, that explanation IS the line.
            // Prefixing it with a generic "Nothing was changed." buries the specific reason behind
            // a vaguer restatement of it.
            var explained = o.AdoptionNote is not null || o.IdentifyNote is not null;
            if (o.Stopped) parts.Add("Stopped. Nothing was changed.");
            else if (!explained) parts.Add(o.NothingHappenedLine ?? "Nothing was changed.");
        }
        else
        {
            parts.Add($"{char.ToUpperInvariant(clause[0])}{clause[1..]}.");
            // The reversibility promise, and only where it applies — adoption is the pass that could
            // have touched files and deliberately did not.
            if (o.Adopted > 0) parts.Add("Your files were not moved.");
        }

        // Each apply's own explanation of why it wrote nothing, kept verbatim next to the count it
        // explains. These are the strings the applies already put on the status line; replacing a
        // real reason with a number is the failure this whole composer exists to prevent.
        if (o.AdoptionNote is not null) parts.Add(o.AdoptionNote);

        if (o.DroppedNameMatches > 0)
        {
            var n = o.DroppedNameMatches;
            // Deliberately does NOT say "file hash". ExcludeKeys is tier-agnostic — the keys it
            // filters against come from every adoption the run wrote, including name-index and
            // unidentified ones. Claiming an exact hash match would assert evidence we may not have.
            parts.Add($"{n} name match{(n == 1 ? " was" : "es were")} skipped — "
                      + $"{(n == 1 ? "that mod was" : "those mods were")} already named earlier in this run.");
        }

        if (o.IdentifyNote is not null) parts.Add(o.IdentifyNote);
        if (o.DownloadsNote is not null) parts.Add(o.DownloadsNote);
        if (o.Stopped && did.Count > 0) parts.Add("Stopped early — run it again for the rest.");

        return string.Join(" ", parts);
    }

    /// <summary>What the downloads-folder pass contributed, or null when it has nothing to add
    /// (it matched something, and the review dialog will show what).
    /// <paramref name="truncated"/> means the per-run md5 cap cut the pass short — it must be set
    /// from the cap BREAK, never from "folder size &gt; cap", because duplicates already claimed by
    /// the game-folder sweep are skipped without charging the budget. A 400-archive folder that ran
    /// to completion having checked 40 has no "rest" to point the user at.
    /// <paramref name="stopped"/> suppresses every diagnosis, because a stopped pass has an opinion
    /// only about archives it actually opened.</summary>
    public static string? DownloadsFolderNote(
        int archivesFound, int checkedCount, int matched, bool truncated, bool stopped)
    {
        var parts = new List<string>();

        if (truncated && !stopped)
            parts.Add($"Checked the first {checkedCount} of {archivesFound} archives in that downloads folder — "
                      + "that's the per-run limit on Nexus hash lookups. Point at a smaller folder to check the rest.");

        // "Nothing matched, so these must not be the original archives" is a diagnosis. It is only
        // honest about archives that were actually read, so a stopped pass makes no claim at all.
        // The short form scopes itself to the capped subset via the sentence above it — without that
        // sentence, "those" has no antecedent, so it is gated on the same condition.
        if (matched == 0 && !stopped)
            parts.Add(truncated
                ? "None of those matched Nexus."
                : "Nothing in that downloads folder matched Nexus — it has to be the ORIGINAL downloaded archives for this game.");

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}
