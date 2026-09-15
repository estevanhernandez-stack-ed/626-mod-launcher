using ModManager.Core.Stores;

namespace ModManager.Tests.Stores;

public class EaInstallerDataTests
{
    // Synthetic, shaped like the real DiPManifest 4.0 files. EA's own files are never committed.
    internal const string Sample = """
<?xml version="1.0" encoding="utf-8"?>
<DiPManifest version="4.0">
  <buildMetaData>
    <gameVersion version="1.0.140.17622" />
  </buildMetaData>
  <contentIDs>
    <contentID>16425899</contentID>
  </contentIDs>
  <gameTitles>
    <gameTitle locale="de_DE">EA SPORTS College Football 27 (DE)</gameTitle>
    <gameTitle locale="en_US">EA SPORTS College Football 27</gameTitle>
  </gameTitles>
  <runtime>
    <launcher uid="2-2"><name locale="en_US">EA SPORTS College Football 27</name><filePath>[HKEY_LOCAL_MACHINE\SOFTWARE\EA Sports\EA SPORTS College Football 27\Install Dir]EAAntiCheat.GameServiceLauncher.exe</filePath></launcher>
  </runtime>
</DiPManifest>
""";

    [Fact]
    public void Reads_content_ids_title_and_version()
    {
        var i = EaInstallerData.Parse(Sample)!;

        Assert.Equal(new[] { "16425899" }, i.ContentIds);
        Assert.Equal("EA SPORTS College Football 27", i.Title);       // en_US preferred over document order
        Assert.Equal("1.0.140.17622", i.GameVersion);
    }

    [Fact]
    public void Falls_back_to_the_first_title_when_there_is_no_en_US()
    {
        var xml = Sample.Replace("locale=\"en_US\"", "locale=\"fr_FR\"");
        Assert.Equal("EA SPORTS College Football 27 (DE)", EaInstallerData.Parse(xml)!.Title);
    }

    [Fact]
    public void Keeps_every_content_id_in_order_and_skips_blanks()
    {
        var xml = Sample.Replace("<contentID>16425899</contentID>",
            "<contentID>111</contentID><contentID>  </contentID><contentID>deadspace_na</contentID>");
        Assert.Equal(new[] { "111", "deadspace_na" }, EaInstallerData.Parse(xml)!.ContentIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not xml at all")]
    [InlineData("<SomethingElse><contentIDs><contentID>1</contentID></contentIDs></SomethingElse>")]
    [InlineData("<DiPManifest version=\"4.0\"><contentIDs></contentIDs></DiPManifest>")]
    [InlineData("<DiPManifest version=\"4.0\"><contentIDs><contentID>1</contentID>")]
    public void Anything_that_is_not_an_ea_install_is_null_not_an_exception(string? xml)
        => Assert.Null(EaInstallerData.Parse(xml));

    [Fact]
    public void A_doctype_with_an_external_entity_is_refused_without_resolving_it()
    {
        // Any installer can write this file. An XXE that reads a local file must not work.
        var secret = Path.Combine(Path.GetTempPath(), "ea-xxe-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(secret, "SECRET");
        try
        {
            var xml = $"""
<?xml version="1.0"?>
<!DOCTYPE DiPManifest [ <!ENTITY x SYSTEM "file:///{secret.Replace('\\', '/')}"> ]>
<DiPManifest version="4.0"><contentIDs><contentID>&x;</contentID></contentIDs></DiPManifest>
""";
            var i = EaInstallerData.Parse(xml);
            Assert.True(i is null || !i.ContentIds.Contains("SECRET"));
        }
        finally { File.Delete(secret); }
    }
}
