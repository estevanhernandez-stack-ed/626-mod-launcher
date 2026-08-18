using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// A17 slice one. A proxy DLL at the play-folder root is loaded by the OS on every start, and on any
/// engine but FromSoft the launcher showed no sign of it — Monster Hunter Wilds listed six lua mods
/// and nothing about the REFramework that loads them, so "turn everything off" left the game loading
/// a seventeen-month-old loader and crashing.
/// </summary>
public class ProxyLoaderRowsTests
{
    // Verbatim from G:\SteamLibrary\steamapps\common\MonsterHunterWilds, 2026-08-18.
    private static readonly string[] RealGameRoot =
    {
        "MonsterHunterWilds.exe", "dinput8.dll", "dstorage.dll", "nvngx_dlss.dll",
        "sl.interposer.dll", "amd_fidelityfx_upscaler_dx12.dll", "steam_api64.dll",
    };

    [Fact]
    public void Surfaces_the_loader_a_non_fromsoft_game_was_hiding()
    {
        var rows = ProxyLoaderRows.Build(RealGameRoot, heldNames: null, alreadyListedNames: null);

        var row = Assert.Single(rows);
        Assert.Equal("dinput8.dll", row.Name);
        Assert.True(row.IsLoader);
        Assert.True(row.Enabled);
        Assert.Equal(ProxyLoaderRows.LocationTag, row.Location);
    }

    [Fact]
    public void Does_not_mistake_the_game_s_own_dlls_for_loaders()
    {
        var shipped = RealGameRoot.Where(f => f != "dinput8.dll");

        Assert.Empty(ProxyLoaderRows.Build(shipped, null, null));
    }

    [Fact]
    public void Does_not_double_list_a_loader_another_lane_already_owns()
    {
        // FromSoft's direct-inject lane owns dinput8.dll as its "DLL mod loader" row. Two rows for one
        // file would give the user two switches wired to different mechanisms.
        var rows = ProxyLoaderRows.Build(RealGameRoot, null, alreadyListedNames: new[] { "dinput8.dll" });

        Assert.Empty(rows);
    }

    [Fact]
    public void A_stepped_aside_loader_still_gets_a_row_reading_off()
    {
        // If the row vanished when disabled, a user who stepped it aside would have no way to put it
        // back from the UI — a one-way door in a launcher whose whole promise is reversibility.
        var rows = ProxyLoaderRows.Build(
            topLevelNames: new[] { "MonsterHunterWilds.exe" },
            heldNames: new[] { "dinput8.dll" },
            alreadyListedNames: null);

        var row = Assert.Single(rows);
        Assert.Equal("dinput8.dll", row.Name);
        Assert.False(row.Enabled);
    }

    [Fact]
    public void A_loader_present_in_both_places_reads_as_on()
    {
        // A stale held copy must never make an active loader read as off — the row has to describe
        // what the OS will actually load, which is the file at the root.
        var rows = ProxyLoaderRows.Build(
            topLevelNames: new[] { "dinput8.dll" },
            heldNames: new[] { "dinput8.dll" },
            alreadyListedNames: null);

        Assert.True(Assert.Single(rows).Enabled);
    }

    [Fact]
    public void Surfaces_every_proxy_name_not_just_dinput8()
    {
        // ReShade ships as dxgi.dll, ASI loaders as version.dll. Each is a separate injector and each
        // needs its own switch.
        var rows = ProxyLoaderRows.Build(new[] { "dxgi.dll", "version.dll", "game.exe" }, null, null);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsLoader));
    }

    [Fact]
    public void The_row_does_not_claim_to_know_which_loader_it_is()
    {
        // dinput8.dll is REFramework here, Elden Mod Loader on Elden Ring, and an ASI loader
        // elsewhere. Naming a product from a filename would be a guess presented as a fact.
        var row = Assert.Single(ProxyLoaderRows.Build(new[] { "dinput8.dll" }, null, null));

        Assert.Contains("dinput8.dll", row.DisplayName);
        Assert.Contains("626 did not install this", row.Description);
    }
}

/// <summary>
/// Naming a loader from disk. Este's question: does that DLL come with the game or the mod set? It
/// comes with the mod set — dinput8.dll is a Windows system DLL, and a loader ships under that name
/// because Windows searches the app folder before System32. Proof from his own install: every game
/// file was rewritten to 2026-08-17 by a reinstall and the proxy stayed at 2025-03-10, because Steam
/// does not own it.
/// </summary>
public class ProxyLoaderNamingTests
{
    [Fact]
    public void Names_reframework_from_the_folder_beside_the_proxy()
    {
        var rows = ProxyLoaderRows.Build(
            topLevelNames: new[] { "dinput8.dll", "MonsterHunterWilds.exe" },
            heldNames: null,
            alreadyListedNames: null,
            siblingEntries: new[] { "dinput8.dll", "MonsterHunterWilds.exe", "reframework" });

        var row = Assert.Single(rows);
        Assert.Contains("REFramework", row.DisplayName);
        Assert.Equal("praydog", row.Author);
        Assert.NotNull(row.ModUrl);
        Assert.Contains("626 did not install this", row.Description);
    }

    [Fact]
    public void The_same_filename_is_a_different_loader_elsewhere()
    {
        // dinput8.dll is REFramework next to reframework/, and Elden Mod Loader next to
        // mod_loader_config.ini. The filename alone decides nothing.
        var elm = ProxyLoaderRows.Build(
            new[] { "dinput8.dll" }, null, null,
            new[] { "dinput8.dll", "mod_loader_config.ini" });

        Assert.Contains("Elden Mod Loader", Assert.Single(elm).DisplayName);
    }

    [Fact]
    public void Stays_honest_when_the_signature_is_absent()
    {
        // A bare proxy with nothing beside it could be anything. Guessing a product name here would
        // put a fact on screen that we do not have.
        var row = Assert.Single(ProxyLoaderRows.Build(new[] { "dinput8.dll" }, null, null, new[] { "dinput8.dll" }));

        Assert.Equal("dinput8.dll — mod loader", row.DisplayName);
        Assert.Null(row.Author);
        Assert.Contains("can't be named from the file alone", row.Description);
    }
}
