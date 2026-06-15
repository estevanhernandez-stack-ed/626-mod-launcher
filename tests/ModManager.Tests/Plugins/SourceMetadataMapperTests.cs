using ModManager.Core;
using ModManager.Core.Plugins;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Plugins;

public class SourceMetadataMapperTests
{
    [Fact]
    public void Maps_source_metadata_onto_mod_meta_fields()
    {
        var dto = new SourceModMetadata(Endorsements: 12, Downloads: 3400, LatestVersion: "2.1", Available: true, Endorsed: true);
        var meta = new ModMeta();                              // existing core type
        var mapped = SourceMetadataMapper.Apply(meta, dto);    // returns the updated ModMeta
        // Reconciled to ModMeta's real field names (Step 1):
        //   Endorsements -> EndorsementCount, Downloads -> Downloads,
        //   LatestVersion -> NexusLatestVersion, Available -> Available, Endorsed -> Endorsed.
        Assert.Equal(12, mapped.EndorsementCount);
        Assert.Equal(3400, mapped.Downloads);
        Assert.Equal("2.1", mapped.NexusLatestVersion);
        Assert.True(mapped.Available);
        Assert.True(mapped.Endorsed);
    }

    [Fact]
    public void Null_dto_fields_preserve_existing_mod_meta_values()
    {
        var dto = new SourceModMetadata(Endorsements: null, Downloads: null, LatestVersion: null, Available: null, Endorsed: null);
        var meta = new ModMeta { EndorsementCount = 7, Downloads = 99, NexusLatestVersion = "1.0", Available = true, Endorsed = true };
        var mapped = SourceMetadataMapper.Apply(meta, dto);
        // EVERY field is nullable and falls back to the existing value — a source that reports nothing clobbers nothing.
        Assert.Equal(7, mapped.EndorsementCount);
        Assert.Equal(99, mapped.Downloads);
        Assert.Equal("1.0", mapped.NexusLatestVersion);
        Assert.True(mapped.Available);
        Assert.True(mapped.Endorsed);
    }

    [Fact]
    public void Per_mod_fetch_without_endorse_state_never_wipes_the_persisted_heart()
    {
        // Regression guard (the silent-heart-wipe NexusRefresh.Overlay was written to prevent): endorse
        // state is owned by the bulk endorsements sweep, so a per-mod metadata fetch carries Endorsed: null.
        // Mapping it must NEVER overwrite a user's real Endorsed=true, even while it enriches the live stats.
        var dto = new SourceModMetadata(Endorsements: 50, Downloads: 1000, LatestVersion: "3.0", Available: true, Endorsed: null);
        var meta = new ModMeta { Endorsed = true };
        var mapped = SourceMetadataMapper.Apply(meta, dto);
        Assert.True(mapped.Endorsed);               // heart preserved
        Assert.Equal(50, mapped.EndorsementCount);  // stats still enriched
    }
}
