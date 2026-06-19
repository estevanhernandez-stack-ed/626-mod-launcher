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

    [Fact]
    public void Maps_identity_and_credit_fields_onto_mod_meta()
    {
        // B2a: the grown DTO carries everything md5-identify produces. Reconciled to ModMeta names:
        //   ImageUrl -> Image, ModUrl -> Url, the rest 1:1.
        var dto = new SourceModMetadata(
            Endorsements: 5, Downloads: 100, LatestVersion: "2.0", Available: true, Endorsed: null,
            Title: "Cool Mod", Description: "Does cool things", Author: "Mxyz",
            AuthorUrl: "https://nexus/users/mxyz", ImageUrl: "https://nexus/img.png",
            ModUrl: "https://nexus/mods/777", Category: "Gameplay",
            ContainsAdultContent: false, NexusFileId: 9);
        var meta = new ModMeta();
        var mapped = SourceMetadataMapper.Apply(meta, dto);
        Assert.Equal("Cool Mod", mapped.Title);
        Assert.Equal("Does cool things", mapped.Description);
        Assert.Equal("Mxyz", mapped.Author);
        Assert.Equal("https://nexus/users/mxyz", mapped.AuthorUrl);
        Assert.Equal("https://nexus/img.png", mapped.Image);
        Assert.Equal("https://nexus/mods/777", mapped.Url);
        Assert.Equal("Gameplay", mapped.Category);
        Assert.False(mapped.ContainsAdultContent);
        Assert.Equal(9, mapped.NexusFileId);
    }

    [Fact]
    public void Null_identity_dto_fields_preserve_existing_mod_meta_values()
    {
        // The same never-clobber discipline holds for the identity/credit fields: a source that
        // reports nothing leaves prior enrichment intact.
        var dto = new SourceModMetadata(
            Endorsements: null, Downloads: null, LatestVersion: null, Available: null, Endorsed: null);
        var meta = new ModMeta
        {
            Title = "Kept Title", Description = "Kept Desc", Author = "Kept Author",
            AuthorUrl = "kept-author-url", Image = "kept-image", Url = "kept-url",
            Category = "Kept Cat", ContainsAdultContent = true, NexusFileId = 42,
        };
        var mapped = SourceMetadataMapper.Apply(meta, dto);
        Assert.Equal("Kept Title", mapped.Title);
        Assert.Equal("Kept Desc", mapped.Description);
        Assert.Equal("Kept Author", mapped.Author);
        Assert.Equal("kept-author-url", mapped.AuthorUrl);
        Assert.Equal("kept-image", mapped.Image);
        Assert.Equal("kept-url", mapped.Url);
        Assert.Equal("Kept Cat", mapped.Category);
        Assert.True(mapped.ContainsAdultContent);
        Assert.Equal(42, mapped.NexusFileId);
    }

    [Fact]
    public void FromIdentify_sets_modId_from_ref_and_fields_from_metadata()
    {
        // NexusModId is the stable handle the identify produced — it comes from the ref, NOT the metadata.
        var r = new SourceIdentifyResult(
            new SourceModRef("nexus", "skyrimspecialedition", 777, "2.0"),
            new SourceModMetadata(5, 100, "2.0", true, null, Title: "Cool", Author: "Mxyz", NexusFileId: 9));
        var meta = SourceMetadataMapper.FromIdentify(r);
        Assert.Equal(777, meta.NexusModId);
        Assert.Equal("Cool", meta.Title);
        Assert.Equal("Mxyz", meta.Author);
        Assert.Equal(9, meta.NexusFileId);
    }

    [Fact]
    public void FromIdentify_seeds_installed_version_from_the_ref_so_no_false_update_chip()
    {
        // The ref carries the INSTALLED-file version; FromIdentify must seed ModMeta.Version from it.
        // When the installed version matches the upstream latest, the UPDATE chip (NexusLatestVersion !=
        // Version) must read false — the regression this guards is Version landing null (chip always on).
        var r = new SourceIdentifyResult(
            new SourceModRef("nexus", "skyrimspecialedition", 777, "1.2.0"),
            new SourceModMetadata(5, 100, LatestVersion: "1.2.0", Available: true, Endorsed: null));
        var meta = SourceMetadataMapper.FromIdentify(r);
        Assert.Equal("1.2.0", meta.Version);                  // installed version seeded from the ref
        Assert.Equal("1.2.0", meta.NexusLatestVersion);       // upstream latest from the metadata
        Assert.Equal(meta.NexusLatestVersion, meta.Version);  // equal => no UPDATE chip (the fix)
    }

    [Fact]
    public void FromIdentify_with_empty_ref_version_leaves_version_null()
    {
        // An empty installed-file version (the md5 hit had no file_details.version) maps to null, not "".
        var r = new SourceIdentifyResult(
            new SourceModRef("nexus", "skyrimspecialedition", 777, ""),
            new SourceModMetadata(5, 100, LatestVersion: "2.0", Available: true, Endorsed: null));
        var meta = SourceMetadataMapper.FromIdentify(r);
        Assert.Null(meta.Version);
    }

    [Fact]
    public void Apply_never_wipes_a_filled_heart_even_when_a_source_reports_endorsed_false()
    {
        // Fill-only-never-wipe: a non-null `false` from any source must NOT clear a persisted Endorsed=true.
        // Only the bulk ApplyEndorsements sweep clears hearts; a per-mod metadata mapping never does.
        var dto = new SourceModMetadata(Endorsements: 50, Downloads: 1000, LatestVersion: "3.0", Available: true, Endorsed: false);
        var meta = new ModMeta { Endorsed = true };
        var mapped = SourceMetadataMapper.Apply(meta, dto);
        Assert.True(mapped.Endorsed);               // heart preserved despite dto Endorsed=false
        Assert.Equal(50, mapped.EndorsementCount);  // stats still enriched
    }

    [Fact]
    public void Apply_fills_an_empty_heart_when_a_source_reports_endorsed_true()
    {
        // The other side of fill-only: a true DOES fill an unset heart.
        var dto = new SourceModMetadata(Endorsements: 1, Downloads: 1, LatestVersion: "1.0", Available: true, Endorsed: true);
        var mapped = SourceMetadataMapper.Apply(new ModMeta { Endorsed = null }, dto);
        Assert.True(mapped.Endorsed);
    }
}
