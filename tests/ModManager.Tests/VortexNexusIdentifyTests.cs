using ModManager.Core;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests;

public class VortexNexusIdentifyTests
{
    // The Vortex-by-modId path fetches metadata by ref (FetchMetadataAsync), not by md5. The func is
    // handed the SourceModRef the rewired Scanner builds from the Vortex-recorded modId.
    private sealed class FakeModSource : IModSource
    {
        private readonly Func<SourceModRef, Task<SourceModMetadata?>> _byRef;
        public FakeModSource(Func<SourceModRef, Task<SourceModMetadata?>> byRef) => _byRef = byRef;
        public string Id => "nexus";
        public bool RequiresApiKey => true;
        public Task<SourceIdentifyResult?> IdentifyByHashAsync(string g, string m) => throw new NotSupportedException();
        public Task<SourceModMetadata?> FetchMetadataAsync(SourceModRef modRef) => _byRef(modRef);
        public Task<bool> IsUpdateAvailableAsync(SourceModRef modRef, string installedVersion) => throw new NotSupportedException();
        public Task<EndorseResult> SetEndorsedAsync(SourceModRef modRef, bool endorsed) => throw new NotSupportedException();
    }

    private static (string modsDir, GameContext c) Fixture(string? domain)
    {
        var root = TestSupport.TempDir("vtxid-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "ue4ss", "Mods");
        Directory.CreateDirectory(Path.Combine(modsDir, "PetBoarPlus"));
        File.WriteAllText(Path.Combine(modsDir, "vortex.deployment.windrose.json"), """
        { "files": [ { "relPath": "PetBoarPlus\\Scripts\\main.lua", "source": "PetBoarPlus V1.0-227-1-0-1777312199" } ]}
        """);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("ue4ss", "UE4SS", "ue4ss/Mods") { Form = "folders" } },
            NexusGameDomain = domain,
        });
        return (modsDir, c);
    }

    [Fact]
    public async Task Identifies_a_vortex_folder_by_nexus_modid()
    {
        var (_, c) = Fixture("windrose");
        int? seenId = null; string? seenDomain = null;
        var fake = new FakeModSource(modRef => { seenDomain = modRef.GameDomain; seenId = modRef.ModId;
            return Task.FromResult<SourceModMetadata?>(new SourceModMetadata(
                null, null, null, null, null,
                Title: "Pet Boar Plus", Author: "IceBox", ModUrl: "https://www.nexusmods.com/windrose/mods/227")); });

        var r = await Scanner.IdentifyVortexNexusAsync(c, fake);

        Assert.Equal("windrose", seenDomain);
        Assert.Equal(227, seenId);
        Assert.Equal(1, r.Matched);
        var meta = Scanner.LoadMetadata(c);
        var key = Variant.ParseVariant("PetBoarPlus").Base;
        Assert.Equal("Pet Boar Plus", meta[key].Title);
        Assert.Equal("IceBox", meta[key].Author);
    }

    [Fact]
    public async Task Returns_zero_without_a_nexus_domain()
    {
        var (_, c) = Fixture(null);
        var fake = new FakeModSource(_ => Task.FromResult<SourceModMetadata?>(
            new SourceModMetadata(null, null, null, null, null, Title: "X")));
        Assert.Equal(0, (await Scanner.IdentifyVortexNexusAsync(c, fake)).Matched);
    }

    [Fact]
    public async Task Skips_folders_without_a_parseable_modid()
    {
        var (modsDir, c) = Fixture("windrose");
        File.WriteAllText(Path.Combine(modsDir, "vortex.deployment.windrose.json"), """
        { "files": [ { "relPath": "CleanName\\main.lua", "source": "CleanName" } ]}
        """);
        Assert.Equal(0, (await Scanner.IdentifyVortexNexusAsync(c, new FakeModSource(_ => Task.FromResult<SourceModMetadata?>(
            new SourceModMetadata(null, null, null, null, null, Title: "X"))))).Matched);
    }
}
