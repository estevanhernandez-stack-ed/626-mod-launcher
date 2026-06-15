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
        var dto = new SourceModMetadata(Endorsements: null, Downloads: null, LatestVersion: null, Available: false, Endorsed: false);
        var meta = new ModMeta { EndorsementCount = 7, Downloads = 99, NexusLatestVersion = "1.0" };
        var mapped = SourceMetadataMapper.Apply(meta, dto);
        // Nullable source fields fall back to the existing ModMeta values (additive enrichment, never clobber).
        Assert.Equal(7, mapped.EndorsementCount);
        Assert.Equal(99, mapped.Downloads);
        Assert.Equal("1.0", mapped.NexusLatestVersion);
        // Non-nullable DTO booleans always write through.
        Assert.False(mapped.Available);
        Assert.False(mapped.Endorsed);
    }
}
