using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// The holding-folder name for a mod. Windows strips a trailing dot or space from a path segment and opens
/// CON, NUL and the rest as devices, so a mod named <c>Foo.</c> used to be held in <c>Foo</c>'s folder. An
/// ordinary name is its own folder, unchanged; only a name Windows would not keep as written is encoded.
/// </summary>
public class HoldingNameTests
{
    [Theory]
    [InlineData("Foo")]
    [InlineData("Cool Mod")]
    [InlineData("Better.HUD.v2")]
    [InlineData("Ünïcödé 模组")]
    [InlineData("#1 Mod")]
    [InlineData("Wow!")]
    [InlineData("Faster Ships [1.2]")]
    [InlineData(" leading space")]
    [InlineData("CONSOLE")]
    [InlineData("COM10")]
    [InlineData("Nullable")]
    [InlineData("626~mod")]
    public void An_ordinary_name_is_its_own_folder(string name)
        => Assert.Equal(name, HoldingName.Folder(name));

    [Theory]
    [InlineData("Foo.")]
    [InlineData("Foo ")]
    [InlineData("Foo..")]
    [InlineData("Foo. ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("   ")]
    [InlineData("Foo:alt")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("x\\..\\Bystander")]
    [InlineData("what?")]
    [InlineData("star*")]
    [InlineData("pipe|")]
    [InlineData("quote\"")]
    [InlineData("<angle>")]
    [InlineData("tab\there")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("Con.txt")]
    [InlineData("NUL")]
    [InlineData("nul.tar.gz")]
    [InlineData("AUX")]
    [InlineData("PRN")]
    [InlineData("COM1")]
    [InlineData("com9")]
    [InlineData("LPT1")]
    [InlineData("lpt9")]
    [InlineData("COM¹")]
    [InlineData("LPT³")]
    [InlineData("CON .txt")]
    [InlineData("~626~466f6f")]
    [InlineData("~626~")]
    public void A_risky_name_gets_an_encoded_folder(string name)
    {
        var folder = HoldingName.Folder(name);

        Assert.StartsWith(HoldingName.Prefix, folder);
        Assert.Equal(HoldingName.Prefix + Convert.ToHexStringLower(System.Text.Encoding.UTF8.GetBytes(name)), folder);
        Assert.True(FolderNames.NamesOneFolder(folder!));
    }

    // An ordinary name is never encoded, whatever its length: it behaves exactly as it always did.
    [Theory]
    [InlineData(201)]
    [InlineData(230)]
    [InlineData(255)]
    public void A_long_ordinary_name_is_still_its_own_folder(int length)
    {
        var name = new string('a', length);
        Assert.Equal(name, HoldingName.Folder(name));
    }

    // ~626~ plus two hex digits a byte must fit NTFS's 255-character name: 125 bytes is the most.
    [Fact]
    public void A_risky_name_whose_encoding_would_pass_255_characters_has_no_folder()
    {
        var fits = new string('a', 124) + ".";      // 125 bytes -> 5 + 250 = 255
        var tooLong = new string('a', 125) + ".";   // 126 bytes -> 257

        Assert.Equal(255, HoldingName.Folder(fits)!.Length);
        Assert.Null(HoldingName.Folder(tooLong));
        Assert.Null(HoldingName.Folder(new string('a', 129) + "."));
        Assert.Null(HoldingName.Folder(new string('é', 63) + "."));   // 127 bytes, 64 characters
    }

    [Fact]
    public void The_encoding_is_the_documented_one()
    {
        Assert.Equal("~626~466f6f2e", HoldingName.Folder("Foo."));
        Assert.Equal("~626~466f6f20", HoldingName.Folder("Foo "));
        Assert.Equal("~626~434f4e", HoldingName.Folder("CON"));
    }

    [Fact]
    public void The_empty_name_never_becomes_the_root_itself()
        => Assert.Equal(HoldingName.Prefix, HoldingName.Folder(""));

    public static TheoryData<string> RoundTripNames() => new()
    {
        "Foo", "Foo.", "Foo ", "Foo..", "..", ".", "CON", "con.txt", "COM¹", "a/b", "Foo:alt", "~626~466f6f",
        "~626~zz", "Ünïcödé 模组.", "emoji 🎮 ", new string('x', 250), "\ud800 lone high.", "lone low \udc00 ",
    };

    [Theory]
    [MemberData(nameof(RoundTripNames))]
    public void Every_name_round_trips(string name)
        => Assert.Equal(name, HoldingName.ModName(HoldingName.Folder(name)!));

    [Fact]
    public void No_two_names_share_a_folder()
    {
        var names = new[]
        {
            "Foo", "Foo.", "Foo ", "Foo..", "Foo. ", "~626~466f6f", "~626~466f6f2e", "CON", "con", "Con",
            "\ud800.", "\udc00.", "\ud800\udc00.",
        };
        var folders = names.Select(HoldingName.Folder).ToList();

        // Case-insensitive, as Windows compares folder names. Two ORDINARY names that differ only in case
        // ("Foo", "foo") are still one folder, as they always were; that is outside this encoding.
        Assert.Equal(names.Length, folders.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("~626~4")]          // odd length
    [InlineData("~626~zz")]         // not hex
    [InlineData("~626~466F6F2E")]   // uppercase hex is not what Folder writes
    [InlineData("~626~ff")]         // not UTF-8
    [InlineData("~626~c0af")]       // overlong
    [InlineData("~626~")]           // nothing encoded
    public void A_malformed_encoding_decodes_to_nothing(string folder)
        => Assert.Null(HoldingName.ModName(folder));

    [Theory]
    [InlineData("Bar")]
    [InlineData("Cool Mod")]
    [InlineData("Foo.")]          // a folder only a \\?\-aware tool could make: still its own name
    [InlineData("626~x")]
    public void A_legacy_folder_name_decodes_to_itself(string folder)
        => Assert.Equal(folder, HoldingName.ModName(folder));
}
