using ModManager.Core;
using System.Reflection;

namespace ModManager.Tests;

public class ManualMatchMergeTests
{
    // Use reflection to call the private MergeMeta — it's internal-by-design, but these tests pin
    // the manual-wins semantic against silent regressions in future refactors.
    private static ModMeta CallMergeMeta(ModMeta cf, ModMeta? curated)
    {
        var m = typeof(Scanner).GetMethod("MergeMeta",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (ModMeta)m.Invoke(null, new object?[] { cf, curated })!;
    }

    [Fact]
    public void Manual_curated_locks_the_row_against_incoming_cf()
    {
        var existing = new ModMeta { Title = "Manually Picked", Image = "manual.png", IsManual = true };
        var incoming = new ModMeta { Title = "Auto Found",      Image = "auto.png" };
        var merged = CallMergeMeta(cf: incoming, curated: existing);
        Assert.Equal("Manually Picked", merged.Title);
        Assert.Equal("manual.png", merged.Image);
        Assert.True(merged.IsManual);
    }

    [Fact]
    public void Manual_cf_side_locks_the_row_too()
    {
        // Md5IdentifyArchivesAsync passes existing as cf. Cover that direction.
        var existing = new ModMeta { Title = "Manually Picked", IsManual = true };
        var incoming = new ModMeta { Title = "Nexus Auto Match" };
        var merged = CallMergeMeta(cf: existing, curated: incoming);
        Assert.Equal("Manually Picked", merged.Title);
        Assert.True(merged.IsManual);
    }

    [Fact]
    public void Non_manual_merge_keeps_existing_field_wins_semantic()
    {
        var existing = new ModMeta { Title = "Existing Title" };
        var incoming = new ModMeta { Title = "Incoming Title", Image = "incoming.png" };
        var merged = CallMergeMeta(cf: incoming, curated: existing);
        Assert.Equal("Existing Title", merged.Title);   // existing per-field wins
        Assert.Equal("incoming.png", merged.Image);     // existing didn't have one — incoming fills
        Assert.False(merged.IsManual);
    }

    [Fact]
    public void Merge_carries_installedUtc_from_cf_when_curated_lacks_it()
    {
        // cf brings InstalledUtc; curated has none — merge must surface it.
        var stamp = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var cfMeta = new ModMeta { Title = "CF Title", InstalledUtc = stamp, SourceConfidence = "fingerprint" };
        var curated = new ModMeta { Title = "Curated Title" };
        var merged = CallMergeMeta(cf: cfMeta, curated: curated);
        Assert.Equal(stamp, merged.InstalledUtc);
        Assert.Equal("fingerprint", merged.SourceConfidence);
    }

    [Fact]
    public void Merge_curated_installedUtc_wins_over_cf()
    {
        // curated wins per-field — same as Title/Author/etc.
        var curatedStamp = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var cfStamp = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var cfMeta = new ModMeta { Title = "CF Title", InstalledUtc = cfStamp, SourceConfidence = "nameSearch" };
        var curated = new ModMeta { Title = "Curated Title", InstalledUtc = curatedStamp, SourceConfidence = "md5" };
        var merged = CallMergeMeta(cf: cfMeta, curated: curated);
        Assert.Equal(curatedStamp, merged.InstalledUtc);
        Assert.Equal("md5", merged.SourceConfidence);
    }

    [Fact]
    public void Merge_carries_sourceConfidence_from_cf_when_curated_lacks_it()
    {
        var cfMeta = new ModMeta { SourceConfidence = "nameSearch" };
        var curated = new ModMeta { Title = "Some Title" };
        var merged = CallMergeMeta(cf: cfMeta, curated: curated);
        Assert.Equal("nameSearch", merged.SourceConfidence);
        Assert.Null(merged.InstalledUtc);
    }

    [Fact]
    public void MergeMeta_carries_nexus_fields_from_fetched_when_curated_lacks_them()
    {
        var cf = new ModMeta { NexusModId = 510, EndorsementCount = 1234, Available = false, Version = "2.3", NexusFileId = 99 };
        var curated = new ModMeta { Title = "Hand title" };   // no nexus fields
        var merged = CallMergeMeta(cf, curated);              // existing reflection helper
        Assert.Equal(510, merged.NexusModId);
        Assert.Equal(1234, merged.EndorsementCount);
        Assert.False(merged.Available);
        Assert.Equal("2.3", merged.Version);
        Assert.Equal(99, merged.NexusFileId);
        Assert.Equal("Hand title", merged.Title);            // curated still wins where it has a value
    }

    [Fact]
    public void MergeMeta_carries_nexusLatestVersion_from_fetched_when_curated_lacks_it()
    {
        var cf = new ModMeta { NexusLatestVersion = "2.1" };
        var curated = new ModMeta { Title = "Hand title" };   // no nexusLatestVersion
        var merged = CallMergeMeta(cf, curated);
        Assert.Equal("2.1", merged.NexusLatestVersion);
        Assert.Equal("Hand title", merged.Title);
    }

    [Fact]
    public void MergeMeta_curated_nexusLatestVersion_wins_per_field()
    {
        var cf = new ModMeta { NexusLatestVersion = "2.1" };
        var curated = new ModMeta { NexusLatestVersion = "9.9" };
        var merged = CallMergeMeta(cf, curated);
        Assert.Equal("9.9", merged.NexusLatestVersion);   // curated ?? cf, same as siblings
    }

    [Fact]
    public void MergeMeta_manual_curated_short_circuits_nexusLatestVersion()
    {
        // IsManual locks the row — incoming nexusLatestVersion must not leak in.
        var cf = new ModMeta { NexusLatestVersion = "2.1" };
        var curated = new ModMeta { Title = "Manual", IsManual = true };  // no latest version
        var merged = CallMergeMeta(cf, curated);
        Assert.Null(merged.NexusLatestVersion);
    }

    [Fact]
    public void MergeMeta_carries_endorsed_from_fetched_when_curated_lacks_it()
    {
        var cf = new ModMeta { Endorsed = true };
        var curated = new ModMeta { Title = "Hand title" };   // no endorsed state
        var merged = CallMergeMeta(cf, curated);
        Assert.True(merged.Endorsed);
        Assert.Equal("Hand title", merged.Title);             // curated still wins where it has a value
    }

    [Fact]
    public void MergeMeta_curated_endorsed_wins_per_field()
    {
        var cf = new ModMeta { Endorsed = false };
        var curated = new ModMeta { Endorsed = true };
        var merged = CallMergeMeta(cf, curated);
        Assert.True(merged.Endorsed);                         // curated ?? cf, same as siblings
    }

    [Fact]
    public void MergeMeta_manual_curated_short_circuits_endorsed()
    {
        // IsManual locks the row — incoming endorsed state must not leak in.
        var cf = new ModMeta { Endorsed = true };
        var curated = new ModMeta { Title = "Manual", IsManual = true };  // no endorsed state
        var merged = CallMergeMeta(cf, curated);
        Assert.Null(merged.Endorsed);
    }
}
