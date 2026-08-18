using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// The provenance half of the mod-provenance design. Intake computed the file list as
/// <c>IntakeResult.Added</c> and threw it away, so a row could never say which files were its own.
/// </summary>
public class ModInstallRegistryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mir-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private ModInstallManifest Make(string archive, params string[] files)
        => new(ModInstallRegistry.IdFor(archive), archive, "mods", files, DateTime.UtcNow);

    [Fact]
    public void An_empty_game_has_no_installs()
        => Assert.Empty(ModInstallRegistry.List(_dir));

    [Fact]
    public void Round_trips_what_an_install_placed()
    {
        ModInstallRegistry.Save(_dir, Make("CatLib v1.9.zip", @"_CatLib\action.lua", @"_CatLib\cache.lua"));

        var m = Assert.Single(ModInstallRegistry.List(_dir));
        Assert.Equal("CatLib v1.9.zip", m.SourceArchive);
        Assert.Equal(2, m.Files.Count);
        Assert.Equal("CatLib v1.9", m.DisplayName);
    }

    [Fact]
    public void Writes_camelCase_on_disk()
    {
        // The launcher shares its on-disk shapes with installs in the field; PascalCase would break
        // round-trips silently. Same rule as every other persisted shape.
        ModInstallRegistry.Save(_dir, Make("Kitty Big.zip", "KittyBig.lua"));

        var json = File.ReadAllText(Directory.GetFiles(Path.Combine(_dir, "installs"), "*.json").Single());
        Assert.Contains("\"sourceArchive\"", json);
        Assert.Contains("\"installedUtc\"", json);
        Assert.DoesNotContain("\"SourceArchive\"", json);
    }

    [Fact]
    public void Reinstalling_the_same_archive_replaces_its_record_rather_than_accumulating()
    {
        ModInstallRegistry.Save(_dir, Make("Kitty Big.zip", "KittyBig.lua"));
        ModInstallRegistry.Save(_dir, Make("Kitty Big.zip", "KittyBig.lua", "extra.lua"));

        var m = Assert.Single(ModInstallRegistry.List(_dir));
        Assert.Equal(2, m.Files.Count);
    }

    [Fact]
    public void Two_installs_can_claim_the_same_file_and_both_are_reported()
    {
        // Not an error. Two mods shipping utility/Statics.lua is exactly the case a disable must not
        // resolve by deleting - it is why the claim is recorded at all.
        ModInstallRegistry.Save(_dir, Make("Disable Post Processing.zip", @"utility\Statics.lua", "dpp.lua"));
        ModInstallRegistry.Save(_dir, Make("Some Other Mod.zip", @"utility\Statics.lua", "other.lua"));

        var claims = ModInstallRegistry.ClaimsOn(_dir, @"utility\Statics.lua");
        Assert.Equal(2, claims.Count);

        Assert.Single(ModInstallRegistry.ClaimsOn(_dir, "dpp.lua"));
        Assert.Empty(ModInstallRegistry.ClaimsOn(_dir, "nobody-claims-this.lua"));
    }

    [Fact]
    public void A_claim_matches_regardless_of_separator_or_case()
    {
        ModInstallRegistry.Save(_dir, Make("m.zip", @"utility\Statics.lua"));

        Assert.Single(ModInstallRegistry.ClaimsOn(_dir, "utility/Statics.lua"));
        Assert.Single(ModInstallRegistry.ClaimsOn(_dir, @"UTILITY\statics.LUA"));
    }

    [Fact]
    public void Remove_forgets_the_record()
    {
        ModInstallRegistry.Save(_dir, Make("Kitty Big.zip", "KittyBig.lua"));
        ModInstallRegistry.Remove(_dir, ModInstallRegistry.IdFor("Kitty Big.zip"));

        Assert.Empty(ModInstallRegistry.List(_dir));
    }

    [Fact]
    public void A_torn_record_is_skipped_and_never_hides_the_rest()
    {
        ModInstallRegistry.Save(_dir, Make("good.zip", "good.lua"));
        File.WriteAllText(Path.Combine(_dir, "installs", "broken.json"), "{ not json");

        Assert.Single(ModInstallRegistry.List(_dir));
    }

    [Fact]
    public void An_archive_name_that_is_not_a_valid_filename_still_gets_an_id()
    {
        // Ids become filenames. A colon or slash in an archive name must not throw at write time.
        var id = ModInstallRegistry.IdFor("weird:name/with*chars.zip");
        Assert.DoesNotContain(id, Path.GetInvalidFileNameChars().Select(c => c.ToString()));
        Assert.NotEmpty(id);
    }
}
