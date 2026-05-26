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
}
