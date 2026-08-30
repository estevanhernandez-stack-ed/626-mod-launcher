using System.IO;
using ModManager.Core.Transport;

namespace ModManager.App.Services;

/// <summary>
/// Where this machine keeps game contents that are waiting for their game.
///
/// <para>Thin on purpose — <see cref="PendingRestore"/> is the whole mechanism and stays pure. This
/// exists only to name the folder in one place, so the report screen that fills it and the game-state
/// chip that reads it cannot drift onto two different paths.</para>
///
/// <para><b>Local, not roaming.</b> A held game is the contents of a backup, which on a real profile
/// ran to gigabytes. <c>%APPDATA%</c> follows a user between machines on a domain and is the wrong
/// place for that; <c>%LOCALAPPDATA%</c> is where the launcher already keeps its other bulk (covers,
/// logs, update stamps).</para>
/// </summary>
public sealed class HeldBackupsService
{
    /// <summary><c>%LOCALAPPDATA%\ModManagerBuilder\held</c>. Not created until something is held.</summary>
    public string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModManagerBuilder", "held");

    public string Hold(string archivePath, string gameId) => PendingRestore.Hold(archivePath, gameId, Dir);

    public IReadOnlyList<HeldGame> List() => PendingRestore.List(Dir);

    /// <summary>What is waiting for one game, or null. Asked on every reload, so it must be cheap and
    /// must never throw.</summary>
    public HeldGame? For(string? gameId)
    {
        if (string.IsNullOrEmpty(gameId)) return null;
        try { return PendingRestore.For(gameId!, Dir); }
        catch { return null; }
    }

    public bool Discard(string gameId) => PendingRestore.Discard(gameId, Dir);
}
