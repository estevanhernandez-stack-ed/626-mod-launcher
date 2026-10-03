using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// Round 9 (review r8, I-1): on the vanilla sheet a mod is "still active" ONLY on evidence (a refused
/// turn-off, or a row note in leftInPlace). Everything else that was on before the clear went off with it,
/// by whatever route: the toggle, its loader's manifest (BepInEx / UE4SS), the vanilla moves or a framework
/// uninstall. Plus the singular in the "could tell" line.
/// </summary>
public class SafeClearRound9Tests
{
    private static GameArchive Vanilla(
        ArchivedMod[] mods,
        ClearedMod[]? turnedOff = null,
        LoaderModState[]? loaders = null,
        FrameworkArchive[]? frameworks = null,
        MovedFile[]? moved = null,
        InPlaceNote[]? left = null,
        ClearSkip[]? skipped = null) => new(
        "t", "T", "/games/T", "vanilla", Array.Empty<LaunchTarget>(), null,
        frameworks ?? Array.Empty<FrameworkArchive>(), loaders ?? Array.Empty<LoaderModState>(), Array.Empty<OwnedModNote>(),
        moved ?? Array.Empty<MovedFile>(), mods, null,
        TurnedOffByClear: turnedOff ?? Array.Empty<ClearedMod>(),
        TurnOffSkipped: skipped,
        VanillaRemainder: Array.Empty<MovedFile>(),
        LeftInPlace: left ?? Array.Empty<InPlaceNote>());

    private static ArchivedMod On(string name) => new(name, true, null, null, null);

    private static (string Sheet, IReadOnlyList<OffBoardingModLine> Lines) Render(GameArchive ga)
    {
        var report = OffBoardingHydrator.Hydrate(ga, "rp");
        return (OffBoardingSheet.Render(report), report.Mods);
    }

    private static string Section(string sheet, string heading)
    {
        var i = sheet.IndexOf(heading, StringComparison.Ordinal);
        if (i < 0) return "";
        // The section runs to the next two-space heading (or the end of the mod list).
        var end = sheet.Length;
        foreach (var h in new[] { "  Turned off by 626", "  Still active (626 left these on)", "  Already off before the reset", "\n\n" })
        {
            var j = sheet.IndexOf(h, i + heading.Length, StringComparison.Ordinal);
            if (j > i && j < end) end = j;
        }
        return sheet[i..end];
    }

    private const string OffHeading = "Turned off by 626";
    private const string ActiveHeading = "Still active (626 left these on)";

    [Fact]
    public void A_BepInEx_plugin_turned_off_through_its_loader_is_listed_as_turned_off_with_the_reason()
    {
        var (sheet, lines) = Render(Vanilla(
            new[] { On("CoolPlugin"), On("OtherPlugin") },
            loaders: new[]
            {
                new LoaderModState("CoolPlugin", "bepinex", true, "mods"),
                new LoaderModState("OtherPlugin", "bepinex", true, "mods"),
            }));

        Assert.All(lines, l => Assert.Equal(OffBoardingModState.TurnedOff, l.State));
        Assert.DoesNotContain(ActiveHeading, sheet);
        Assert.Contains("Turned off by 626 (restoring turns these back on) (2):", sheet);
        Assert.Contains("CoolPlugin", Section(sheet, OffHeading));
        Assert.Contains("its loader was turned off", Section(sheet, OffHeading));
    }

    [Fact]
    public void A_row_named_in_leftInPlace_is_listed_as_still_active()
    {
        var (sheet, lines) = Render(Vanilla(
            new[] { On("Gone"), On("Skyrim") },
            turnedOff: new[] { new ClearedMod("Gone", "mods") },
            left: new[] { new InPlaceNote("Skyrim", RestorePointEngine.CantTellRowPrefix + "Data, where 626 can't tell") }));

        Assert.Equal(OffBoardingModState.StillActive, lines.Single(l => l.Name == "Skyrim").State);
        Assert.Equal(OffBoardingModState.TurnedOff, lines.Single(l => l.Name == "Gone").State);
        Assert.Contains("Still active (626 left these on) (1):", sheet);
        Assert.Contains("Turned off by 626 (restoring turns these back on) (1):", sheet);
        Assert.Contains("Skyrim", Section(sheet, ActiveHeading));
        Assert.DoesNotContain("Skyrim", Section(sheet, OffHeading));
    }

    [Fact]
    public void A_refused_turn_off_is_listed_as_still_active()
    {
        var (_, lines) = Render(Vanilla(
            new[] { On("Bravo") },
            turnedOff: new[] { new ClearedMod("Bravo", "mods") },
            skipped: new[] { new ClearSkip("Bravo", "held copy") }));
        Assert.Equal(OffBoardingModState.StillActive, lines.Single().State);
    }

    [Fact]
    public void A_row_a_framework_uninstall_took_out_is_listed_as_turned_off()
    {
        var (sheet, lines) = Render(Vanilla(
            new[] { On("DLL mod loader"), On("Foo") },
            turnedOff: new[] { new ClearedMod("Foo", "direct-inject") },
            frameworks: new[] { new FrameworkArchive("elden-mod-loader", "Elden Mod Loader", "TechieW", "/games/T/Game", new[] { "dinput8.dll" }, "frameworks-state/elden-mod-loader") }));

        Assert.All(lines, l => Assert.Equal(OffBoardingModState.TurnedOff, l.State));
        Assert.DoesNotContain(ActiveHeading, sheet);
        Assert.Contains("Turned off by 626 (restoring turns these back on) (2):", sheet);
        Assert.Contains("DLL mod loader", Section(sheet, OffHeading));
    }

    [Fact]
    public void A_mod_that_was_already_off_stays_already_off()
    {
        var (_, lines) = Render(Vanilla(new[] { new ArchivedMod("WasOff", false, null, null, null) }));
        Assert.Equal(OffBoardingModState.AlreadyOff, lines.Single().State);
    }

    // ---- grammar ----

    private static GameArchive WithItems(int off) => Vanilla(
        Enumerable.Range(0, off).Select(i => On("Off" + i)).ToArray(),
        turnedOff: Enumerable.Range(0, off).Select(i => new ClearedMod("Off" + i, "mods")).ToArray(),
        left: new[]
        {
            new InPlaceNote("Item", RestorePointEngine.CantTellRowPrefix + "Data, where 626 can't tell"),
            new InPlaceNote("Data", "626 can't tell the game's own files from mods in Data; 1 files no mod claims are still in place"),
        });

    [Fact]
    public void One_mod_reads_singular()
    {
        var line = OffBoardingHydrator.WhatHappened(WithItems(1));
        Assert.StartsWith("626 turned off the 1 mod it could tell was a mod", line);
        Assert.DoesNotContain("1 mod it could tell were mods", line);
    }

    [Fact]
    public void Several_mods_read_plural()
        => Assert.StartsWith("626 turned off the 3 mods it could tell were mods", OffBoardingHydrator.WhatHappened(WithItems(3)));
}
