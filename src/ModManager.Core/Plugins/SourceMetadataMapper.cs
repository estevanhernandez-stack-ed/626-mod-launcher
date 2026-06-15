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
    public static ModMeta Apply(ModMeta meta, SourceModMetadata dto)
    {
        meta.EndorsementCount = dto.Endorsements ?? meta.EndorsementCount;
        meta.Downloads = dto.Downloads ?? meta.Downloads;
        meta.NexusLatestVersion = dto.LatestVersion ?? meta.NexusLatestVersion;
        meta.Available = dto.Available ?? meta.Available;
        meta.Endorsed = dto.Endorsed ?? meta.Endorsed;
        return meta;
    }
}
