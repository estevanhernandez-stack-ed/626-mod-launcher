using ModManager.Core.LooseMods;
using ModManager.Plugins.Abstractions;


namespace ModManager.Core.Discovery
{
    /// <summary>
    /// One mod found twice in one identify run (C5). When the downloads-archive hash pass and the name
    /// search both find the same mod, the review used to show two pre-checked rows and a button reading
    /// "Apply 2 changes" when one would land. The write was already right: adoptions go first and
    /// <see cref="LooseIdentify.ExcludeKeys"/> drops a name guess for any key one wrote.
    ///
    /// <para><b>Linked, not folded.</b> Both rows stay, so neither choice disappears: the name match says
    /// an adoption above already names the mod, starts unticked, and counts only when that adoption is
    /// not approved. A ticked row is then always a change that lands.</para>
    ///
    /// <para><b>Not deduplicated at propose time.</b> The classifier deliberately does not filter
    /// archives against known row keys; that would cost the hash tier its shot at an exact match.</para>
    /// </summary>
    public static class IdentifyReviewOverlap
    {
        /// <summary>
        /// The adoption that names the mod at <paramref name="modKey"/>, or null. Only an adoption the
        /// review pre-checks as an identification and that will write to that key counts
        /// (<see cref="AdoptionReviewText.PreChecked"/>, <see cref="AdoptionReach.NamesAMod"/>, its
        /// resolved <see cref="AdoptionProposal.WriteKeys"/>); an unidentified file or one that writes
        /// nothing never hides a name match. Several: the strongest evidence, then the first.
        /// </summary>
        public static AdoptionProposal? Coverer(IEnumerable<AdoptionProposal> adoptions, string modKey)
            => adoptions
                .Where(a => AdoptionReviewText.PreChecked(a) && a.Reach == AdoptionReach.NamesAMod
                            && a.WriteKeys?.Contains(modKey, StringComparer.OrdinalIgnoreCase) == true)
                .Select((a, i) => (a, i))
                .OrderByDescending(x => Rank(x.a.Evidence)).ThenBy(x => x.i)
                .Select(x => x.a)
                .FirstOrDefault();

        /// <summary>The approved name matches that will actually be written: those whose mod an APPROVED
        /// adoption does not already name. The same rule the apply enforces with
        /// <see cref="LooseIdentify.ExcludeKeys"/>, so the button's count is the write's.</summary>
        public static IReadOnlyList<(string ModKey, SourceSearchHit Hit)> Effective(
            IReadOnlyList<(string ModKey, SourceSearchHit Hit)> approvedIdentifications,
            IReadOnlyList<AdoptionProposal> approvedAdoptions)
            => approvedIdentifications.Where(i => Coverer(approvedAdoptions, i.ModKey) is null).ToList();

        private static int Rank(AdoptionEvidence e) => e switch
        {
            AdoptionEvidence.Md5 => 3,
            AdoptionEvidence.NameIndex => 2,
            AdoptionEvidence.NameSearch => 1,
            _ => 0,
        };
    }
}
