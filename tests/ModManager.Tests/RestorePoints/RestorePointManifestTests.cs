using System.Text.Json;
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class RestorePointManifestTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rp-man-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private static RestorePointManifest Sample(bool complete) => new(
        SchemaVersion: RestorePoint.SchemaVersion,
        LauncherVersion: "0.4.0",
        CreatedUtc: "2026-05-28T14:12:33Z",
        Complete: complete,
        KeepNexus: true,
        TotalBytes: 123,
        FileCount: 4,
        Games: new[]
        {
            new GameArchive("elden-ring", "ELDEN RING", @"D:\ELDEN RING\Game", "vanilla",
                LaunchTargets: new[] { new LaunchTarget("Play (Seamless Co-op)", "exe", @"Game\sc\launch.exe") { IsDefault = true } },
                RequiredLauncher: null,
                Frameworks: Array.Empty<FrameworkArchive>(),
                LoaderMods: Array.Empty<LoaderModState>(),
                OwnedMods: Array.Empty<OwnedModNote>(),
                MovedFiles: new[] { new MovedFile(@"Game\dinput8.dll", 1234, "abc123") },
                Mods: new[] { new ArchivedMod("CoolMod", false, "https://nexusmods.com/x", "fingerprint", "2026-04-02T00:00:00Z") },
                OffboardingSheetGameFolderPath: @"D:\ELDEN RING\626-launcher-how-to-launch.txt"),
        });

    [Fact]
    public void Manifest_round_trips_as_camelCase()
    {
        Directory.CreateDirectory(_tmp);
        RestorePointManifestStore.WriteSealed(_tmp, Sample(complete: true));
        var json = File.ReadAllText(Path.Combine(_tmp, RestorePointManifestStore.FileName));

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"gameName\"", json);
        Assert.Contains("\"movedFiles\"", json);
        Assert.Contains("\"sourceConfidence\"", json);
        Assert.DoesNotContain("\"SchemaVersion\"", json);
        Assert.DoesNotContain("\"GameName\"", json);

        var rt = RestorePointManifestStore.Read(_tmp)!;
        Assert.Equal("elden-ring", rt.Games[0].Id);
        Assert.Equal("vanilla", rt.Games[0].EndState);
        Assert.Equal(1234, rt.Games[0].MovedFiles[0].Bytes);
        Assert.Equal("fingerprint", rt.Games[0].Mods[0].SourceConfidence);
        Assert.True(rt.Complete);
    }

    [Fact]
    public void Read_returns_null_when_no_manifest()
    {
        Directory.CreateDirectory(_tmp);
        Assert.Null(RestorePointManifestStore.Read(_tmp));
    }

    [Fact]
    public void Validate_refuses_a_null_manifest()
    {
        // Restore calls Validate(Read(dir), ...) and Read returns null for a missing/partial point.
        var v = RestorePointManifestStore.Validate(null, RestorePoint.SchemaVersion);
        Assert.False(v.Ok);
        Assert.NotNull(v.Reason);
    }

    [Fact]
    public void Validate_refuses_unsealed_manifest()
    {
        var v = RestorePointManifestStore.Validate(Sample(complete: false), RestorePoint.SchemaVersion);
        Assert.False(v.Ok);
        Assert.Contains("incomplete", v.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_refuses_newer_schema()
    {
        var newer = Sample(complete: true) with { SchemaVersion = RestorePoint.SchemaVersion + 1 };
        var v = RestorePointManifestStore.Validate(newer, RestorePoint.SchemaVersion);
        Assert.False(v.Ok);
        Assert.Contains("newer", v.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_accepts_a_sealed_current_manifest()
        => Assert.True(RestorePointManifestStore.Validate(Sample(complete: true), RestorePoint.SchemaVersion).Ok);
}
