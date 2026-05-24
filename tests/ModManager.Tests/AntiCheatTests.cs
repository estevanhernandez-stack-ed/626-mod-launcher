using ModManager.Core;

namespace ModManager.Tests;

// Reversible anti-cheat toggle for EAC games (Elden Ring): disabling backs up the EAC bootstrapper
// (start_protected_game.exe) and swaps in a copy of the real exe so Steam's Play runs offline+modded;
// enabling restores the original. Backup-first, never destructive, idempotent.
public class AntiCheatTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mmb-ac-" + Guid.NewGuid().ToString("N"));
    private const string Boot = "start_protected_game.exe";
    private const string Real = "eldenring.exe";

    public AntiCheatTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, Boot), "EAC");
        File.WriteAllText(Path.Combine(_dir, Real), "GAME");
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private string Read(string name) => File.ReadAllText(Path.Combine(_dir, name));

    [Fact]
    public void Starts_on()
        => Assert.Equal(AntiCheatState.On, AntiCheat.State(_dir, Boot));

    [Fact]
    public void Disable_backs_up_eac_and_swaps_in_the_real_exe()
    {
        AntiCheat.Disable(_dir, Boot, Real);

        Assert.Equal(AntiCheatState.Off, AntiCheat.State(_dir, Boot));
        Assert.Equal("GAME", Read(Boot));                 // Play now runs the real game (no EAC)
        Assert.True(File.Exists(Path.Combine(_dir, Boot + ".626off"))); // original preserved
        Assert.Equal("EAC", Read(Boot + ".626off"));
        Assert.Equal("GAME", Read(Real));                 // real exe untouched
    }

    [Fact]
    public void Enable_restores_the_original_and_clears_the_copy()
    {
        AntiCheat.Disable(_dir, Boot, Real);
        AntiCheat.Enable(_dir, Boot);

        Assert.Equal(AntiCheatState.On, AntiCheat.State(_dir, Boot));
        Assert.Equal("EAC", Read(Boot));                  // original bootstrapper back
        Assert.False(File.Exists(Path.Combine(_dir, Boot + ".626off")));
    }

    [Fact]
    public void Toggling_is_idempotent()
    {
        AntiCheat.Disable(_dir, Boot, Real);
        AntiCheat.Disable(_dir, Boot, Real); // no-op, must not clobber the backup with the copy
        Assert.Equal("EAC", Read(Boot + ".626off"));

        AntiCheat.Enable(_dir, Boot);
        AntiCheat.Enable(_dir, Boot); // no-op
        Assert.Equal(AntiCheatState.On, AntiCheat.State(_dir, Boot));
        Assert.Equal("EAC", Read(Boot));
    }
}
