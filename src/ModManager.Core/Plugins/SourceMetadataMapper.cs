using ModManager.Plugins.Abstractions;

namespace ModManager.Core.Plugins;

/// <summary>Maps a plugin's source metadata DTO onto the core's ModMeta mod-source fields. The boundary
/// that lets a plugin speak DTOs and never reference Core's ModMeta. camelCase persistence is unchanged —
/// these are existing ModMeta fields populated from a new source.
/// <para>ModMeta is a mutable class (not a record), so this mutates the passed instance in place and
/// returns it. EVERY DTO field is nullable and falls back to the existing ModMeta value (additive
/// enrichment — a source that doesn't report a field never clobbers what's already known). This is
/// load-bearing for <c>Endorsed</c>: endorse state is owned by the bulk endorsements sweep, so a per-mod
/// metadata fetch carries <c>Endorsed: null</c> and must NEVER overwrite the user's persisted heart
/// (mirrors the never-clobber discipline in <c>NexusRefresh.Overlay</c>). <c>Endorsed</c> is further
/// fill-only: a non-null <c>false</c> from any source is ignored — only a <c>true</c> fills, and only the
/// bulk sweep ever clears. Same never-clobber discipline for <c>Available</c>.</para></summary>
public static class SourceMetadataMapper
{
    /// <summary>Builds a fresh Nexus-sourced ModMeta from an md5-identify hit. The mod id is the stable
    /// handle the identify produced — it comes from the <see cref="SourceIdentifyResult.Ref"/>, NOT the
    /// metadata payload. Every descriptive field comes from the metadata via <see cref="Apply"/>. The
    /// installed <c>Version</c> is owned by the identify path — it's seeded here from the ref's
    /// installed-file version, and <c>Apply</c> never writes it, so it survives. <c>Apply</c> writes the
    /// upstream latest to <c>NexusLatestVersion</c> instead. Seeding the installed <c>Version</c> is what
    /// keeps the UPDATE chip honest: without it <c>Version</c> stays null and
    /// <c>NexusLatestVersion != Version</c> reads true on every identified mod (a false UPDATE chip).</summary>
    public static ModMeta FromIdentify(SourceIdentifyResult r)
        => Apply(
            new ModMeta { NexusModId = r.Ref.ModId, Version = string.IsNullOrEmpty(r.Ref.Version) ? null : r.Ref.Version },
            r.Metadata);

    public static ModMeta Apply(ModMeta meta, SourceModMetadata dto)
    {
        meta.EndorsementCount = dto.Endorsements ?? meta.EndorsementCount;
        meta.Downloads = dto.Downloads ?? meta.Downloads;
        meta.NexusLatestVersion = dto.LatestVersion ?? meta.NexusLatestVersion;
        meta.Available = dto.Available ?? meta.Available;
        // Fill-only, never wipe: a per-mod fetch can FILL a heart (Endorsed: true) but must NEVER clear
        // one. Clearing is owned by the bulk ApplyEndorsements sweep — the only writer allowed to set
        // Endorsed=false. A non-null `false` from any source (the Nexus plugin always returns null here,
        // but a future/third-party IModSource might not) would otherwise silently wipe the user's heart.
        if (dto.Endorsed == true) meta.Endorsed = true;
        // B2a identity/credit fields — reconciled to ModMeta names (ImageUrl -> Image, ModUrl -> Url).
        // Same never-clobber discipline: a source that doesn't report a field leaves prior enrichment intact.
        meta.Title = dto.Title ?? meta.Title;
        meta.Description = dto.Description ?? meta.Description;
        meta.Author = dto.Author ?? meta.Author;
        meta.AuthorUrl = dto.AuthorUrl ?? meta.AuthorUrl;
        meta.Image = dto.ImageUrl ?? meta.Image;
        meta.Url = dto.ModUrl ?? meta.Url;
        meta.Category = dto.Category ?? meta.Category;
        meta.ContainsAdultContent = dto.ContainsAdultContent ?? meta.ContainsAdultContent;
        meta.NexusFileId = dto.NexusFileId ?? meta.NexusFileId;
        return meta;
    }
}
