using ModManager.Core.Transport;

namespace ModManager.Tests;

/// <summary>
/// Where an archive entry is allowed to land.
///
/// <para>Written after a security review caught the same flaw at two sites: both used
/// <c>dest.StartsWith(root)</c>, which is a prefix match rather than containment. The escape it lets
/// through is a SIBLING directory, and it is easy to miss precisely because the obvious
/// <c>../../escaped.txt</c> case is caught correctly.</para>
/// </summary>
public class SafeExtractPathTests
{
    private static string Root => Path.Combine(Path.GetTempPath(), "626-root", "pal");

    [Fact]
    public void A_sibling_directory_sharing_the_roots_NAME_is_outside_it()
    {
        // THE BUG. "…/626-root/pal" is a string prefix of "…/626-root/palworld-evil", so a prefix
        // check waves this through. It is a different folder entirely.
        var sibling = Path.Combine(Path.GetTempPath(), "626-root", "palworld-evil", "x.sav");

        Assert.False(SafeExtractPath.IsInside(Root, sibling));
        Assert.StartsWith(Path.GetFullPath(Root), Path.GetFullPath(sibling));   // ...and here is why
    }

    [Fact]
    public void The_ordinary_traversal_shapes_are_refused()
    {
        Assert.Throws<InvalidOperationException>(() => SafeExtractPath.ResolveOrThrow(Root, "../escaped.txt"));
        Assert.Throws<InvalidOperationException>(() => SafeExtractPath.ResolveOrThrow(Root, "../../../escaped.txt"));
        Assert.Throws<InvalidOperationException>(() => SafeExtractPath.ResolveOrThrow(Root, "W1/../../escaped.txt"));
    }

    [Fact]
    public void An_absolute_entry_cannot_choose_its_own_destination()
    {
        Assert.Throws<InvalidOperationException>(
            () => SafeExtractPath.ResolveOrThrow(Root, @"C:\Windows\System32\evil.dll"));
        Assert.Throws<InvalidOperationException>(
            () => SafeExtractPath.ResolveOrThrow(Root, @"\server\share\evil.dll"));
    }

    [Fact]
    public void Real_entries_still_land_where_they_should()
    {
        Assert.True(SafeExtractPath.IsInside(Root, Path.Combine(Root, "W1", "Level.sav")));
        Assert.True(SafeExtractPath.IsInside(Root, Root));                       // the root itself
        Assert.True(SafeExtractPath.IsInside(Root, Path.Combine(Root, "a", "..", "b.sav")));  // collapses

        Assert.EndsWith(Path.Combine("W1", "Level.sav"),
            SafeExtractPath.ResolveOrThrow(Root, "W1/Level.sav"));
    }

    [Fact]
    public void The_refusal_names_the_ENTRY_not_the_resolved_path()
    {
        // The resolved path is the attacker's choice; echoing it into a UI is its own small gift.
        var ex = Assert.Throws<InvalidOperationException>(
            () => SafeExtractPath.ResolveOrThrow(Root, "../../escaped.txt"));

        Assert.Contains("../../escaped.txt", ex.Message);
        Assert.DoesNotContain("626-root\escaped", ex.Message);
    }

    [Fact]
    public void Nonsense_input_is_not_inside_anything()
    {
        Assert.False(SafeExtractPath.IsInside("", "anything"));
        Assert.False(SafeExtractPath.IsInside(Root, ""));
        Assert.False(SafeExtractPath.IsInside(null!, null!));
    }
}
