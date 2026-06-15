namespace ModManager.Core;

/// <summary>
/// The per-mod Nexus refresh primitive: poll Nexus <em>by mod id</em> (no archive required) to
/// refresh live stats and capture the current upstream version, while preserving the installed
/// side of the update compare. One primitive (<see cref="RefreshOneAsync"/>) feeds two paths —
/// the manual sweep and the debounced auto-check.
///
/// <para>The mod id is recovered two ways: the stored <see cref="ModMeta.NexusModId"/> (set by
/// Backfill) or, failing that, parsed from the stored <see cref="ModMeta.Url"/> (Nexus only).</para>
///
/// <para>Update-available is never trusted from disk: it is computed on the in-memory
/// <see cref="Mod"/> as <c>NexusLatestVersion is not null &amp;&amp; NexusLatestVersion != Version</c>.</para>
/// </summary>
public static class NexusRefresh
{
    /// <summary>
    /// Resolve the Nexus mod id for an entry: the stored <see cref="ModMeta.NexusModId"/> wins;
    /// otherwise parse <see cref="ModMeta.Url"/> (Nexus mod pages only). A CurseForge URL, an
    /// unparseable URL, or no id at all returns null (the mod is skipped — no archive, no id).
    /// </summary>
    public static int? ResolveModId(ModMeta meta)
    {
        if (meta.NexusModId is { } id) return id;

        var parts = ModSiteUrl.Parse(meta.Url);
        if (parts is { Provider: ModSiteProvider.Nexus } && int.TryParse(parts.ModRef, out var parsed))
            return parsed;

        return null;
    }

    /// <summary>
    /// Refresh one mod's Nexus stats by id. Resolves the id (<see cref="ResolveModId"/>); if none,
    /// returns null without calling the client. Otherwise <c>GetMod</c> by id and overlay only the
    /// live stats + the upstream current version onto a clone of <paramref name="existing"/>:
    /// <list type="bullet">
    ///   <item>refresh <see cref="ModMeta.EndorsementCount"/>, <see cref="ModMeta.Downloads"/>,
    ///         <see cref="ModMeta.Available"/>,</item>
    ///   <item>set <see cref="ModMeta.NexusLatestVersion"/> = the fetched current version,</item>
    ///   <item><strong>preserve</strong> the installed <see cref="ModMeta.Version"/> and
    ///         <see cref="ModMeta.NexusFileId"/> (the "what you have" side), plus all identity
    ///         (Title/Author/Source/links), <see cref="ModMeta.IsManual"/>, etc.</item>
    /// </list>
    /// A <see cref="NexusRateLimitException"/> from the client propagates (the caller decides how to
    /// handle a 429 — the sweep stops and reports partial progress).
    /// </summary>
    public static async Task<ModMeta?> RefreshOneAsync(ModMeta existing, string domain, INexusClient client)
    {
        var id = ResolveModId(existing);
        if (id is null) return null;

        var fetched = await client.GetModAsync(domain, id.Value);
        if (fetched is null) return null;

        return Overlay(existing, fetched);
    }

    /// <summary>
    /// Produce a refreshed clone: copy every field from <paramref name="existing"/> (the installed
    /// truth) then overwrite only the live-stat fields from <paramref name="fetched"/> and capture
    /// the upstream version as <see cref="ModMeta.NexusLatestVersion"/>. The installed
    /// <see cref="ModMeta.Version"/> / <see cref="ModMeta.NexusFileId"/> are never touched.
    /// </summary>
    private static ModMeta Overlay(ModMeta existing, ModMeta fetched) => new()
    {
        // identity + installed side — preserved verbatim from existing
        Title = existing.Title,
        Description = existing.Description,
        Author = existing.Author,
        AuthorUrl = existing.AuthorUrl,
        Url = existing.Url,
        Source = existing.Source,
        Donate = existing.Donate,
        Image = existing.Image,
        CurseforgeId = existing.CurseforgeId,
        Category = existing.Category,
        IsManual = existing.IsManual,
        InstalledUtc = existing.InstalledUtc,
        SourceConfidence = existing.SourceConfidence,
        ContainsAdultContent = existing.ContainsAdultContent,
        NexusModId = existing.NexusModId,
        NexusFileId = existing.NexusFileId,   // installed file — NOT the upstream latest
        Version = existing.Version,           // installed version — the "what you have" side

        // live stats — refreshed from the fetched mod
        EndorsementCount = fetched.EndorsementCount ?? existing.EndorsementCount,
        Downloads = fetched.Downloads ?? existing.Downloads,
        Available = fetched.Available ?? existing.Available,

        // the "what's available" side of the compare
        NexusLatestVersion = fetched.Version ?? existing.NexusLatestVersion,
    };
}
