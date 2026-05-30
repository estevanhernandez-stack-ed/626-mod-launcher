using ModManager.Core;

namespace ModManager.Tests;

// The DLL mod loader (Elden Mod Loader = dinput8.dll) stays a VISIBLE, distinguished, independently
// toggleable row even when its mods\ folder has contents. Toggling it off moves ONLY its own
// dinput8.dll to holding — the hosted mods\ mods stay in place (inert without the loader, but
// harmless — proven live: the game still launches). DECOUPLED, not a cascade.
public class DirectInjectLoaderRowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mmb-loaderrow-" + Guid.NewGuid().ToString("N"));
    private string Play => Path.Combine(_root, "Game");
    private string Holding => Path.Combine(_root, "holding");
    private string Mods => Path.Combine(Play, "mods");

    public DirectInjectLoaderRowTests()
    {
        Directory.CreateDirectory(Mods);
        File.WriteAllText(Path.Combine(Play, "dinput8.dll"), "LOADER");          // the loader
        File.WriteAllText(Path.Combine(Play, "eldenring.exe"), "GAME");          // vanilla — never moves
        File.WriteAllText(Path.Combine(Mods, "AdjustTheFov.dll"), "MOD-A");      // hosted mod
        File.WriteAllText(Path.Combine(Mods, "RemoveVignette.dll"), "MOD-B");    // hosted mod
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static string[] Files(string dir) => Directory.GetFiles(dir).Select(Path.GetFileName).ToArray()!;

    // ---- Listing: loader row surfaced + tagged --------------------------------------------------

    [Fact]
    public void Loader_row_present_when_hosting_mods()
    {
        var game = FromSoftFixture.Build();   // Game/dinput8.dll + Game/mods/AdjustTheFov.dll
        var rows = DirectInjectListing.List(game);

        var loader = rows.SingleOrDefault(r => r.Name == DirectInject.LoaderName);
        Assert.NotNull(loader);                       // the regression: loader row was dropped when mods\ had contents
        Assert.True(loader!.IsLoader);
        Assert.Contains(rows, r => r.Name == "Adjust The Fov");   // hosted mod still surfaced alongside
    }

    [Fact]
    public void Hosted_mod_rows_not_tagged_loader()
    {
        var game = FromSoftFixture.Build();
        var rows = DirectInjectListing.List(game);
        Assert.All(rows.Where(r => r.Name != DirectInject.LoaderName), r => Assert.False(r.IsLoader));
    }

    [Fact]
    public void Disabled_loader_row_tagged_loader()
    {
        // A disabled loader row still carries IsLoader=true so the App renders the LOADER chip + lets
        // the user toggle it back on independently.
        var game = FromSoftFixture.Build();
        var holding = DirectInjectListing.Holding(game);
        var play = DirectInjectListing.PlayFolder(game.GameRoot)!;
        var loaderUnit = DirectInject.Detect(Files(play), Array.Empty<string>())
            .Single(m => m.Name == DirectInject.LoaderName);
        DirectInject.Disable(play, holding, loaderUnit);

        var loaderRow = DirectInjectListing.List(game).Single(r => r.Name == DirectInject.LoaderName);
        Assert.False(loaderRow.Enabled);
        Assert.True(loaderRow.IsLoader);
    }

    // ---- Decoupled toggle: the loader's per-mod Disable/Enable moves ONLY dinput8.dll -----------

    [Fact]
    public void Disabling_the_loader_moves_only_dinput8_leaving_hosted_mods_in_place()
    {
        // This is the decouple contract: the loader toggle (per-mod Disable on the loader unit) must
        // NOT touch the hosted mods\ mods — they stay on disk, inert-but-harmless without the loader.
        var loaderUnit = DirectInject.Detect(Files(Play), Array.Empty<string>())
            .Single(m => m.Name == DirectInject.LoaderName);
        DirectInject.Disable(Play, Holding, loaderUnit);

        Assert.False(File.Exists(Path.Combine(Play, "dinput8.dll")));        // loader moved to holding
        Assert.True(File.Exists(Path.Combine(Mods, "AdjustTheFov.dll")));    // hosted mods UNTOUCHED
        Assert.True(File.Exists(Path.Combine(Mods, "RemoveVignette.dll")));
        Assert.True(File.Exists(Path.Combine(Play, "eldenring.exe")));       // vanilla untouched

        // Only the loader is in holding — no hosted mod was cascaded.
        var disabled = DirectInject.ListDisabled(Holding);
        Assert.Single(disabled);
        Assert.Equal(DirectInject.LoaderName, disabled[0].Name);
    }

    [Fact]
    public void Re_enabling_the_loader_restores_only_dinput8()
    {
        var loaderUnit = DirectInject.Detect(Files(Play), Array.Empty<string>())
            .Single(m => m.Name == DirectInject.LoaderName);
        DirectInject.Disable(Play, Holding, loaderUnit);
        DirectInject.Enable(Play, Holding, DirectInject.LoaderName);

        Assert.Equal("LOADER", File.ReadAllText(Path.Combine(Play, "dinput8.dll")));   // restored byte-for-byte
        Assert.True(File.Exists(Path.Combine(Mods, "AdjustTheFov.dll")));              // hosted mods never moved
        Assert.Empty(DirectInject.ListDisabled(Holding));                             // holding cleared
    }

    // ---- IsLoader transient ---------------------------------------------------------------------

    [Fact]
    public void Mod_IsLoader_is_transient_not_on_disk()
    {
        // The only thing a loader disable writes is the unit's __626mod.json (DisabledMeta) — which has
        // no IsLoader field. Mod is never serialized, so the transient flag can't leak to disk.
        var loaderUnit = DirectInject.Detect(Files(Play), Array.Empty<string>())
            .Single(m => m.Name == DirectInject.LoaderName);
        DirectInject.Disable(Play, Holding, loaderUnit);

        foreach (var meta in Directory.GetFiles(Holding, "__626mod.json", SearchOption.AllDirectories))
            Assert.DoesNotContain("isLoader", File.ReadAllText(meta), StringComparison.OrdinalIgnoreCase);
    }
}
