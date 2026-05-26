using ModManager.Core;

namespace ModManager.Tests;

public class Ue4ssBuiltinsTests
{
    [Theory]
    [InlineData("BPModLoaderMod")]
    [InlineData("ConsoleEnablerMod")]
    [InlineData("Keybinds")]
    [InlineData("shared")]
    [InlineData("bpmodloadermod")]   // case-insensitive
    public void IsBuiltin_recognizes_ue4ss_framework_folders(string name)
        => Assert.True(Ue4ssBuiltins.IsBuiltin(name));

    [Theory]
    [InlineData("PetBoarPlus")]
    [InlineData("ExpandedPickupRadius1.2-134-1-2-1776872771")]
    [InlineData("")]
    public void IsBuiltin_is_false_for_user_mods(string name)
        => Assert.False(Ue4ssBuiltins.IsBuiltin(name));

    [Fact]
    public void Lookup_returns_title_description_and_docs_for_a_builtin()
    {
        var b = Ue4ssBuiltins.Lookup("BPModLoaderMod");
        Assert.NotNull(b);
        Assert.False(string.IsNullOrWhiteSpace(b!.Title));
        Assert.False(string.IsNullOrWhiteSpace(b.Description));
        Assert.StartsWith("https://", b.DocsUrl);
    }

    [Fact]
    public void Lookup_is_null_for_a_user_mod() => Assert.Null(Ue4ssBuiltins.Lookup("PetBoarPlus"));
}
