using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Wave 3 / A13. Framework detection ORed together a runtime and the proxy that loads it, so either
/// half alone read as "framework present".
///
/// <para>Proven live on Windrose, 2026-08-17: with <c>UE4SS.dll</c> moved aside and
/// <c>dwmapi.dll</c> left in place, 626 reported <c>27 of 27 enabled</c> and zero NEEDS chips while the
/// game refused to start with its own "Failed to load UE4SS.dll" dialog and never wrote UE4SS.log.
/// Twelve mods were dead and the launcher said everything was fine.</para>
///
/// <para>A false red is noise. A false green is a user who believes their mods are on.</para>
/// </summary>
public class FrameworkComponentTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("fwcomp-");
    private readonly string _bin;

    public FrameworkComponentTests() => _bin = Path.Combine(_root, "R5", "Binaries", "Win64");

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private GameContext UePak()
        => Scanner.GameContext(new GameEntry
        {
            Id = "fwcomp",
            Engine = "ue-pak",
            GameRoot = _root,
            DataDir = Path.Combine(_root, "_626mods", "fwcomp"),
            GroupingRule = "filename_no_ext",
            FileExtensions = new[] { "pak" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "R5/Content/Paks/~mods") },
        });

    private void Runtime()
    {
        Directory.CreateDirectory(Path.Combine(_bin, "ue4ss"));
        File.WriteAllText(Path.Combine(_bin, "ue4ss", "UE4SS.dll"), "x");
    }

    private void Loader()
    {
        Directory.CreateDirectory(_bin);
        File.WriteAllText(Path.Combine(_bin, "dwmapi.dll"), "x");
    }

    private FrameworkPresence Ue4ss() => Assert.Single(FrameworkDeps.Check(UePak()), p => p.Dep.Name == "UE4SS");

    [Fact]
    public void The_live_failure_no_longer_reads_as_present()
    {
        // The exact experiment: loader in place, runtime moved aside.
        Loader();

        var presence = Ue4ss();

        Assert.False(presence.IsPresent);
        Assert.True(presence.IsPartial);
        Assert.Equal("runtime", Assert.Single(presence.Missing).Name);
        Assert.Equal("loader", Assert.Single(presence.Present).Name);
    }

    [Fact]
    public void The_mirror_case_fails_too_because_a_runtime_does_not_load_itself()
    {
        Runtime();

        var presence = Ue4ss();

        Assert.False(presence.IsPresent);
        Assert.Equal("loader", Assert.Single(presence.Missing).Name);
    }

    [Fact]
    public void Both_halves_present_is_present()
    {
        Runtime();
        Loader();

        var presence = Ue4ss();

        Assert.True(presence.IsPresent);
        Assert.False(presence.IsPartial);
        Assert.Empty(presence.Missing);
    }

    [Fact]
    public void Neither_half_is_missing_but_not_PARTIAL()
    {
        // Nothing installed is a different state from half installed, and only the second gets the
        // qualifying sentence. Calling both "partial" would put "runtime missing, loader missing" in
        // front of a user who simply has not installed UE4SS.
        var presence = Ue4ss();

        Assert.False(presence.IsPresent);
        Assert.False(presence.IsPartial);
        Assert.Equal(2, presence.Missing.Count);
    }

    [Fact]
    public void A_half_install_says_which_half()
    {
        // The sentence that would have ended the live investigation in a second.
        Loader();

        Assert.Equal("UE4SS — loader present, runtime missing", Ue4ss().Describe());
    }

    [Fact]
    public void A_framework_missing_outright_is_named_without_qualification()
    {
        // "UE4SS — runtime missing, loader missing" is noise when the answer is "you don't have it".
        Assert.Equal("UE4SS", Ue4ss().Describe());
    }

    [Fact]
    public void A_single_component_framework_still_means_any_of()
    {
        // The whole point of one-component-equals-old-behaviour: entries that are genuinely lists of
        // alternatives did not have to change, and must not have.
        var eml = FrameworkDeps.Catalog.Single(d => d.Name == "Elden Mod Loader");

        var component = Assert.Single(eml.Components);
        Assert.Contains("dinput8.dll", component.AnyOf);
        Assert.Contains("version.dll", component.AnyOf);
        Assert.True(component.AnyOf.Count > 1, "several proxy names, one component");
    }
}

/// <summary>
/// The audit half of A13: every catalog entry got a decision, and none was changed on a guess.
/// Changing OR to AND without evidence trades a dangerous failure for an annoying one and still ships
/// a lie.
/// </summary>
public class FrameworkDepsAuditTests
{
    [Fact]
    public void Only_UE4SS_is_split_so_far_and_it_is_the_one_with_a_live_repro()
    {
        var multi = FrameworkDeps.Catalog.Where(d => d.Components.Count > 1).Select(d => d.Name).ToList();

        Assert.Equal(new[] { "UE4SS" }, multi);
    }

    [Fact]
    public void Every_component_of_a_split_framework_is_named()
    {
        // An unnamed half cannot be reported, and "part present, part missing" helps nobody. This is
        // what stops a future split from landing without the sentence that makes it useful.
        foreach (var dep in FrameworkDeps.Catalog.Where(d => d.Components.Count > 1))
            foreach (var component in dep.Components)
                Assert.False(string.IsNullOrWhiteSpace(component.Name), $"{dep.Name} has an unnamed component");
    }

    [Fact]
    public void Every_component_has_at_least_one_path_to_look_for()
    {
        // A component with no paths can never be satisfied, so it would pin its framework permanently
        // missing — a false red for every user of that engine.
        foreach (var dep in FrameworkDeps.Catalog)
        {
            Assert.NotEmpty(dep.Components);
            foreach (var component in dep.Components) Assert.NotEmpty(component.AnyOf);
        }
    }

    [Fact]
    public void The_flattened_path_list_still_carries_every_path_in_declaration_order()
    {
        // DetectRelativePaths survives for callers that want "which files", and its order is load
        // bearing for the catalog test that reads [0].
        var ue4ss = FrameworkDeps.Catalog.Single(d => d.Name == "UE4SS");

        Assert.Equal(new[] { "Binaries/Win64/ue4ss/UE4SS.dll", "Binaries/Win64/dwmapi.dll" },
            ue4ss.DetectRelativePaths);
    }

    [Fact]
    public void The_single_component_constructor_is_still_how_an_alternatives_entry_is_written()
    {
        // Kept as a real constructor so entries that did not change did not have to be rewritten.
        var dep = new FrameworkDep(
            Engine: "test", Name: "Thing",
            DetectRelativePaths: new[] { "a.dll", "b.dll" },
            GetUrl: "https://example.invalid", Note: "n");

        var component = Assert.Single(dep.Components);
        Assert.Equal(new[] { "a.dll", "b.dll" }, component.AnyOf);
        Assert.Equal("", component.Name);
    }
}
