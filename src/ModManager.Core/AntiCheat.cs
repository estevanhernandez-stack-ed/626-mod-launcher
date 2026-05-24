namespace ModManager.Core;

/// <summary>Whether a game's anti-cheat is currently engaged. Unsupported = no bootstrapper present.</summary>
public enum AntiCheatState { On, Off, Unsupported }

/// <summary>
/// Reversible anti-cheat toggle for EAC games (Elden Ring et al.). The game's anti-cheat is started
/// by a bootstrapper exe (start_protected_game.exe); the proven way to load mods is to put a copy of
/// the real game exe in the bootstrapper's place so Steam's normal Play launches offline, no EAC.
///
/// We do it backup-first and never destructively: the original bootstrapper is preserved as
/// <c>&lt;name&gt;.626off</c> while anti-cheat is off, and restored to re-enable it. Idempotent.
/// </summary>
public static class AntiCheat
{
    private const string OffSuffix = ".626off"; // original bootstrapper, parked here while AC is off

    public static AntiCheatState State(string playFolder, string bootstrapper)
    {
        var boot = Path.Combine(playFolder, bootstrapper);
        if (File.Exists(boot + OffSuffix)) return AntiCheatState.Off;
        return File.Exists(boot) ? AntiCheatState.On : AntiCheatState.Unsupported;
    }

    /// <summary>Turn anti-cheat OFF: back up the bootstrapper, copy the real exe into its place.</summary>
    public static void Disable(string playFolder, string bootstrapper, string realExe)
    {
        var boot = Path.Combine(playFolder, bootstrapper);
        var backup = boot + OffSuffix;
        var real = Path.Combine(playFolder, realExe);
        if (File.Exists(backup)) return; // already off — don't clobber the preserved original
        if (!File.Exists(boot)) throw new InvalidOperationException($"Couldn't find {bootstrapper}.");
        if (!File.Exists(real)) throw new InvalidOperationException($"Couldn't find {realExe}.");

        File.Move(boot, backup); // preserve the original (never deleted)
        try
        {
            File.Copy(real, boot, overwrite: false);
        }
        catch (Exception e)
        {
            // Roll back so the game is never left without a working launcher.
            try { if (File.Exists(boot)) File.Delete(boot); } catch { /* best effort */ }
            try { File.Move(backup, boot); } catch { /* best effort */ }
            throw new InvalidOperationException($"Couldn't disable anti-cheat ({e.Message}) — is the game running?", e);
        }
    }

    /// <summary>Turn anti-cheat ON: remove the real-exe copy, restore the original bootstrapper.</summary>
    public static void Enable(string playFolder, string bootstrapper)
    {
        var boot = Path.Combine(playFolder, bootstrapper);
        var backup = boot + OffSuffix;
        if (!File.Exists(backup)) return; // already on
        if (File.Exists(boot)) File.Delete(boot); // the real-exe copy we placed
        File.Move(backup, boot);                   // restore the original bootstrapper
    }
}
