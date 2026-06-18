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
/// (mirrors the never-clobber discipline in <c>NexusRefresh.Overlay</c>). Same for <c>Available</c>.</para></summary>
public static class SourceMetadataMapper
{
    /// <summary>Builds a fresh Nexus-sourced ModMeta from an md5-identify hit. The mod id is the stable
    /// handle the identify produced — it comes from the <see cref="SourceIdentifyResult.Ref"/>, NOT the
    /// metadata payload. Every descriptive field comes from the metadata via <see cref="Apply"/>. The
    /// installed <c>Version</c> is owned by the identify path (set from the file context), never written
    /// here — <c>Apply</c> writes the upstream latest to <c>NexusLatestVersion</c> instead.</summary>
    public static ModMeta FromIdentify(SourceIdentifyResult r)
        => Apply(new ModMeta { NexusModId = r.Ref.ModId }, r.Metadata);

    public static ModMeta Apply(ModMeta meta, SourceModMetadata dto)
    {
        meta.EndorsementCount = dto.Endorsements ?? meta.EndorsementCount;
        meta.Downloads = dto.Downloads ?? meta.Downloads;
        meta.NexusLatestVersion = dto.LatestVersion ?? meta.NexusLatestVersion;
        meta.Available = dto.Available ?? meta.Available;
        meta.Endorsed = dto.Endorsed ?? meta.Endorsed;
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
