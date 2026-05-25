using ModManager.Core;

namespace ModManager.Tests;

// Vortex's deployment manifest maps each deployed folder to its Nexus source archive name, which
// encodes the modId: "<name> <ver>-<modId>-<fileId>-<timestamp>". We recover folder -> modId.
public class VortexManifestTests
{
    [Theory]
    [InlineData("PetBoarPlus V1.0-227-1-0-1777312199", 227)]
    [InlineData("ExpandedPickupRadius1.2-134-1-2-1776872771", 134)]
    [InlineData("Some-Dashed-Name-988-3-1-1700000000", 988)]
    public void ParseNexusModId_reads_the_modid(string source, int expected)
        => Assert.Equal(expected, VortexManifest.ParseNexusModId(source));

    [Theory]
    [InlineData("PlainFolderNoId")]
    [InlineData("")]
    [InlineData("NameWithTrailingNumber123")]   // no -id-..-timestamp run
    public void ParseNexusModId_is_null_without_the_pattern(string source)
        => Assert.Null(VortexManifest.ParseNexusModId(source));

    [Fact]
    public void Read_maps_each_folder_to_its_source_and_modid_deduped()
    {
        var dir = TestSupport.TempDir("vtx-");
        File.WriteAllText(Path.Combine(dir, "vortex.deployment.windrose.json"), """
        { "files": [
          { "relPath": "PetBoarPlus\\config.txt", "source": "PetBoarPlus V1.0-227-1-0-1777312199" },
          { "relPath": "PetBoarPlus\\Scripts\\main.lua", "source": "PetBoarPlus V1.0-227-1-0-1777312199" },
          { "relPath": "ExpandedPickupRadius1.2-134-1-2-1776872771\\enabled.txt", "source": "ExpandedPickupRadius1.2-134-1-2-1776872771" }
        ]}
        """);
        var refs = VortexManifest.Read(dir);
        Assert.Equal(2, refs.Count);                                        // deduped by folder
        Assert.Equal(227, refs.First(r => r.Folder == "PetBoarPlus").NexusModId);
        Assert.Equal(134, refs.First(r => r.Folder == "ExpandedPickupRadius1.2-134-1-2-1776872771").NexusModId);
    }

    [Fact]
    public void Read_returns_empty_when_no_manifest()
        => Assert.Empty(VortexManifest.Read(TestSupport.TempDir("vtx-none-")));
}
