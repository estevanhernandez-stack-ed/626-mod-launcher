using ModManager.Core;

namespace ModManager.Tests;

// Ports profile-core.test.js — a profile name becomes <name>.json, so it must not escape
// the profiles dir or trip Windows filename pitfalls. Reject rather than silently sanitize.
public class ProfileTests
{
    [Fact]
    public void Accepts_a_normal_name_unchanged()
        => Assert.Equal("Hardcore", Profile.SafeProfileName("Hardcore"));

    [Fact]
    public void Trims_surrounding_whitespace()
        => Assert.Equal("Vanilla", Profile.SafeProfileName("  Vanilla  "));

    [Fact]
    public void Allows_internal_dots_spaces_and_hyphens()
        => Assert.Equal("My Profile v1.2-final", Profile.SafeProfileName("My Profile v1.2-final"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejects_empty_or_whitespace_only(string? name)
        => Assert.ThrowsAny<Exception>(() => Profile.SafeProfileName(name));

    [Theory]
    [InlineData("../evil")]
    [InlineData("..\\evil")]
    [InlineData("sub/dir")]
    [InlineData("a\\b")]
    public void Rejects_path_separators(string name)
        => Assert.ThrowsAny<Exception>(() => Profile.SafeProfileName(name));

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void Rejects_bare_dot_and_dot_dot(string name)
        => Assert.ThrowsAny<Exception>(() => Profile.SafeProfileName(name));

    [Theory]
    [InlineData('<')]
    [InlineData('>')]
    [InlineData(':')]
    [InlineData('"')]
    [InlineData('|')]
    [InlineData('?')]
    [InlineData('*')]
    public void Rejects_windows_invalid_filename_characters(char ch)
        => Assert.ThrowsAny<Exception>(() => Profile.SafeProfileName("bad" + ch + "name"));

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("Com1")]
    [InlineData("LPT9")]
    public void Rejects_windows_reserved_device_names(string name)
        => Assert.ThrowsAny<Exception>(() => Profile.SafeProfileName(name));

    [Fact]
    public void Rejects_names_ending_in_a_dot()
        => Assert.ThrowsAny<Exception>(() => Profile.SafeProfileName("profile."));

    [Fact]
    public void Trims_a_trailing_space_rather_than_rejecting_it()
        => Assert.Equal("Save 2", Profile.SafeProfileName("Save 2 "));
}
