using ModManager.Core.LooseMods;

namespace ModManager.Core.Discovery;

/// <summary>
/// One mod, one review row (C5). When the downloads-archive hash pass and the name search both find
/// the same mod, the identify review used to show it twice, one pre-checked row per section, with a
/// button reading "Apply 2 changes" when one would land. The write was already right: the hash write
/// goes first and <see cref="LooseIdentify.ExcludeKeys"/> drops the name guess for any key it wrote.
/// The dialog over-promised.
///
/// <para><b>Not deduplicated at propose time.</b> The classifier deliberately does not filter archives
/// against known row keys; that would cost the hash tier its shot at an exact identification. The
/// overlap is resolved where both lists are already in hand: the review.</para>
/// </summary>
public static class IdentifyReviewOverlap
{
    /// <summary>
    /// The name-match rows an adoption already covers: mod key to the adoption that will write it.
    /// Only an adoption that WILL write counts (<see cref="AdoptionReach.NamesAMod"/> with resolved
    /// <see cref="AdoptionProposal.WriteKeys"/>). A download never installed, or a mod already named,
    /// writes nothing, so the name match is still the only change and keeps its own row.
    /// </summary>
    public static IReadOnlyDictionary<string, AdoptionProposal> CoveredKeys(IEnumerable<AdoptionProposal> adoptions)
    {
        var covered = new Dictionary<string, AdoptionProposal>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in adoptions)
        {
            if (a.Reach != AdoptionReach.NamesAMod || a.WriteKeys is null) continue;
            // The strongest evidence claims a key first: the order adoptions apply in.
            foreach (var k in a.WriteKeys) covered.TryAdd(k, a);
        }
        return covered;
    }
}
