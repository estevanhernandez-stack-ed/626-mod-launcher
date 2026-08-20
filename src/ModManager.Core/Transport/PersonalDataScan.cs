namespace ModManager.Core.Transport;

/// <summary>
/// Things a bundle CARRIES that identify the person who made it.
///
/// <para><b>Distinct from <see cref="CredentialScan"/>, and the difference is the point.</b> A
/// credential is excluded — it can be used against you and an artifact that leaves the machine must
/// not contain one. This is the other category: real data, correctly included, that happens to say
/// who you are. Removing it would break the restore; saying nothing about it would let someone post a
/// file publicly without knowing what is in it.</para>
///
/// <para>So the bundle carries it and <b>says so</b>. The user decides what to do with a file they
/// understand.</para>
///
/// <para>Matched on path rather than content: these are known files with known meanings, and a
/// content scan for "does this look like a Steam id" would be guessing at 17-digit numbers.</para>
/// </summary>
public static class PersonalDataScan
{
    /// <summary>Why a carried file is worth mentioning, or null when it is not.</summary>
    public static string? ReasonFor(string relativePath)
    {
        var name = System.IO.Path.GetFileName(relativePath.Replace('\\', '/'));

        // Steam writes this beside cloud-synced saves. It holds the account id and nothing else, and
        // Steam recreates it - but it names the account, so a shared bundle names its owner.
        if (string.Equals(name, "steam_autocloud.vdf", StringComparison.OrdinalIgnoreCase))
            return "steam-account-id";

        return null;
    }

    /// <summary>
    /// One sentence for the panel, or empty when there is nothing to say.
    ///
    /// <para>Says what it is, why it is fine where it is going, and the one case where it is not.
    /// A warning that only alarms leaves the user with a decision and no way to make it.</para>
    /// </summary>
    public static string MessageFor(IReadOnlyList<BundleNotice>? notices)
    {
        if (notices is null || notices.Count == 0) return "";

        var steam = notices.Count(n => n.Reason == "steam-account-id");
        if (steam == 0) return "";

        return "It also carries your Steam account id (steam_autocloud.vdf), which is how Steam tracks "
             + "cloud saves. That is fine for moving between your own machines - but it does identify "
             + "you, so this is not a file to post publicly.";
    }
}
