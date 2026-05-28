using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class RestoreReconcileTests
{
    private static GameArchive Ga(string id, string root) => new(
        id, id, root, "vanilla",
        Array.Empty<LaunchTarget>(), null, Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(),
        Array.Empty<OwnedModNote>(), Array.Empty<MovedFile>(), Array.Empty<ArchivedMod>(), null);

    private static RestorePointManifest M(params GameArchive[] games) =>
        new(RestorePoint.SchemaVersion, "0.4.0", "t", true, true, 0, 0, games);

    private static GameEntry Live(string id, string root) => new() { Id = id, GameName = id, GameRoot = root };

    [Fact]
    public void No_conflict_when_id_absent_from_live()
        => Assert.Empty(RestoreReconcile.Check(M(Ga("elden-ring", @"D:\ER")), Array.Empty<GameEntry>()));

    [Fact]
    public void No_conflict_when_same_id_same_root()
        => Assert.Empty(RestoreReconcile.Check(M(Ga("elden-ring", @"D:\ER")), new[] { Live("elden-ring", @"D:\ER") }));

    [Fact]
    public void Conflict_when_same_id_different_root()
    {
        var conflicts = RestoreReconcile.Check(M(Ga("elden-ring", @"D:\ER")), new[] { Live("elden-ring", @"E:\Other") });
        Assert.Single(conflicts);
        Assert.Equal("elden-ring", conflicts[0].Id);
        Assert.Equal(@"D:\ER", conflicts[0].ManifestGameRoot);
        Assert.Equal(@"E:\Other", conflicts[0].LiveGameRoot);
    }
}
