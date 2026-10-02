using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Direct-inject and loose-root holding folders are named by <see cref="EnginePresets.Slugify"/>, kept for
/// on-disk compatibility, and the slug merges names: <c>Foo</c> and <c>foo-</c> are both <c>foo</c>. The held
/// record stores the mod's name, so a slug folder answers only to the mod it holds, in both directions.
/// Loose-root toggles go through the same <see cref="DirectInject.Disable"/> / <see cref="DirectInject.Enable"/>.
/// </summary>
public class DirectInjectSlugOwnerTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-di-slug-");
    private string Play => Path.Combine(_root, "Game");
    private string Holding => Path.Combine(_root, "holding");
    private string SlugDir => Path.Combine(Holding, "foo");

    private static readonly DirectInjectMod Foo = new("Foo", "tweak", "Foo.asi", new[] { "Foo.asi" });
    private static readonly DirectInjectMod FooDash = new("foo-", "tweak", "foo-.asi", new[] { "foo-.asi" });

    public DirectInjectSlugOwnerTests()
    {
        Directory.CreateDirectory(Play);
        File.WriteAllText(Path.Combine(Play, "Foo.asi"), "FOO");
        File.WriteAllText(Path.Combine(Play, "foo-.asi"), "FOO DASH");
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private Dictionary<string, string> Tree()
        => Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(_root, p), File.ReadAllText, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Both_names_share_one_slug_folder()
        => Assert.Equal(EnginePresets.Slugify(Foo.Name), EnginePresets.Slugify(FooDash.Name));

    [Fact]
    public void The_held_record_stores_the_name_in_camelCase()
    {
        DirectInject.Disable(Play, Holding, Foo);

        var json = File.ReadAllText(Path.Combine(SlugDir, "__626mod.json"));
        Assert.Contains("\"name\"", json);
        Assert.DoesNotContain("\"Name\"", json);
        Assert.Contains("\"Foo\"", json);
    }

    [Fact]
    public void Turning_on_another_name_for_the_same_slug_refuses_and_moves_nothing()
    {
        DirectInject.Disable(Play, Holding, Foo);
        var before = Tree();

        var e = Assert.Throws<HeldCopyCollisionException>(() => DirectInject.Enable(Play, Holding, "foo-"));

        Assert.Equal("626 can't turn \"foo-\" on: the files held under that name belong to \"Foo\". Nothing was moved.", e.Message);
        Assert.Equal(before, Tree());
    }

    [Fact]
    public void Turning_on_the_held_mod_by_its_name_in_another_case_still_works()
    {
        DirectInject.Disable(Play, Holding, Foo);

        DirectInject.Enable(Play, Holding, "FOO");

        Assert.Equal("FOO", File.ReadAllText(Path.Combine(Play, "Foo.asi")));
        Assert.Empty(DirectInject.ListDisabled(Holding));
    }

    // The existing collision guard already refuses when the folder holds FILES; the stored name makes the
    // refusal say whose they are.
    [Fact]
    public void Turning_off_a_second_mod_into_an_occupied_slug_folder_refuses_and_names_the_holder()
    {
        DirectInject.Disable(Play, Holding, Foo);
        var before = Tree();

        var e = Assert.Throws<HeldCopyCollisionException>(() => DirectInject.Disable(Play, Holding, FooDash));

        Assert.Equal("626 can't turn \"foo-\" off: 626 is already holding \"Foo\" in the same folder. Nothing was moved.", e.Message);
        Assert.Equal(before, Tree());
    }

    // The case the files guard did not cover: a record with nothing behind it. Writing over it would replace
    // Foo's record with foo-'s.
    [Fact]
    public void Turning_off_into_a_slug_folder_holding_only_another_mods_record_refuses()
    {
        Directory.CreateDirectory(SlugDir);
        File.WriteAllText(Path.Combine(SlugDir, "__626mod.json"), "{\"name\":\"Foo\",\"kind\":\"tweak\",\"entries\":[]}");
        var before = Tree();

        var e = Assert.Throws<HeldCopyCollisionException>(() => DirectInject.Disable(Play, Holding, FooDash));

        Assert.Contains("already holding \"Foo\"", e.Message);
        Assert.Equal(before, Tree());
    }

    // A record an older build wrote without a name keeps today's behaviour: turned on by any name that slugs there.
    [Fact]
    public void A_record_without_a_name_keeps_todays_behaviour()
    {
        Directory.CreateDirectory(SlugDir);
        File.Move(Path.Combine(Play, "Foo.asi"), Path.Combine(SlugDir, "Foo.asi"));
        File.WriteAllText(Path.Combine(SlugDir, "__626mod.json"), "{\"kind\":\"tweak\",\"entries\":[\"Foo.asi\"]}");

        DirectInject.Enable(Play, Holding, "foo-");

        Assert.Equal("FOO", File.ReadAllText(Path.Combine(Play, "Foo.asi")));
    }
}
