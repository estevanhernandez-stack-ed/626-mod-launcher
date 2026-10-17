using Microsoft.Extensions.DependencyInjection;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>What one Patch Day evaluation read and decided. The report is kept by the game view so a chip
/// action can re-derive the verdict without another disk scan.</summary>
public sealed record PatchDayEvaluation(PatchDayVerdict Verdict, StaleLoaderReport Report);

/// <summary>
/// The Patch Day wiring that both the game view's reload and the library home's Play click run: evaluate
/// (<see cref="PatchDay.Evaluate"/>), detect and persist a new patch, and kick off the automatic restore
/// point. One copy, so the two surfaces cannot drift, and ONE in-flight set, so opening the game right after
/// a library click never starts a second copy of the same game.
///
/// <para>Core decides; this reads Steam and the disk and writes the record. Nothing here moves a file
/// except the capture, which writes only inside restore-points.</para>
/// </summary>
public sealed class PatchDayDetector
{
    private readonly LauncherService _svc;
    private readonly SteamService _steam;

    // Games whose automatic restore point is being copied right now, so a reload that lands mid-copy
    // never starts a second one for the same game.
    private readonly HashSet<string> _capturing = new(StringComparer.OrdinalIgnoreCase);

    public PatchDayDetector(LauncherService svc, SteamService steam)
    {
        _svc = svc;
        _steam = steam;
    }

    /// <summary>Raised on the UI thread after a game's restore point finished (saved or failed) and the
    /// record was updated, with the record as stored now, so a game view showing that game can re-derive
    /// its verdict.</summary>
    public event Action<string, PatchDayRecord?>? RestorePointSettled;

    /// <summary>
    /// Evaluate one game from what the caller already read: the live Steam build and the live stale-loader
    /// scan. While mods are stepped aside a live scan sees no loaders (their proxies sit in holding
    /// folders), so the verdict reads the stash's named proxies by their held files' dates instead.
    /// Persists a detected patch onto <c>ctx.Game.PatchDay</c> and the registry, and starts the snapshot.
    /// </summary>
    public async Task<PatchDayEvaluation> EvaluateAsync(GameContext ctx, string? liveBuild, StaleLoaderReport liveReport)
    {
        var mode = VanillaLaunch.CurrentMode(ctx.DataDir);
        var report = liveReport;
        if (mode == LaunchMode.Vanilla && VanillaStashStore.Load(ctx.DataDir) is { } stash)
            report = await Task.Run(() => StaleLoaders.Find(ctx, stash));

        var game = ctx.Game;
        var verdict = PatchDay.Evaluate(new PatchDayInput(
            game.LastKnownSteamBuildId, liveBuild, game.PatchDay, report, Array.Empty<LoaderRelease>(), mode));

        var takeSnapshot = false;
        if (verdict.NeedsDetect && !string.IsNullOrEmpty(liveBuild))
        {
            // The baseline stays put: it moves only in Bring my mods back.
            var detected = PatchDay.Detect(game.PatchDay, liveBuild, DateTime.UtcNow);
            _svc.SetPatchDay(game.Id, detected);
            game.PatchDay = detected;
            verdict = PatchDay.Evaluate(new PatchDayInput(
                game.LastKnownSteamBuildId, liveBuild, detected, report, Array.Empty<LoaderRelease>(), mode));
            takeSnapshot = PatchDay.NeedsRestorePoint(detected);
        }
        else if (game.PatchDay is { } held && verdict is { State: PatchDayState.Patched, Choice: PatchDayChoice.None }
                 && PatchDay.NeedsRestorePoint(held) && held.RestorePointError is null)
        {
            // A capture the app was closed in the middle of: no id, no error. Try once more.
            takeSnapshot = true;
        }

        if (takeSnapshot) _ = CaptureRestorePointAsync(game.Id);
        return new PatchDayEvaluation(verdict, report);
    }

    /// <summary>
    /// The library home's Play click: should this game open to ask instead of launching? A stored record is
    /// answered for free. With none, a game that has a Steam app id gets the game view's detection run for it
    /// alone, off the UI thread. Anything that goes wrong answers false: Play launches as it always did.
    /// </summary>
    public async Task<bool> ShouldOpenToAskAsync(GameEntry game)
    {
        try
        {
            if (game.PatchDay is not null) return PatchDay.ShouldOpenToAsk(game.PatchDay, null);
            if (string.IsNullOrEmpty(game.SteamAppId)) return false;

            // Same inputs the game view reads on reload: the local Steam scan (no network) and the loader dates.
            var (ctx, liveBuild, stale) = await Task.Run(() =>
            {
                var c = Scanner.GameContext(game, SaveLocator.EffectiveSaveDir(game));
                var build = InstalledGameMatch.ByAppId(_steam.InstalledGames(), game.SteamAppId)?.BuildId;
                return (c, build, StaleLoaders.Find(c));
            });
            if (string.IsNullOrEmpty(liveBuild)) return false;

            var evaluation = await EvaluateAsync(ctx, liveBuild, stale);
            return PatchDay.ShouldOpenToAsk(game.PatchDay, evaluation.Verdict);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The automatic restore point. Off the UI thread, never twice at once for a game, and a failure
    /// is recorded on the PATCH DAY record rather than thrown: the warning never depends on the snapshot.</summary>
    private async Task CaptureRestorePointAsync(string gameId)
    {
        if (!_capturing.Add(gameId)) return;
        try
        {
            ModManager.Core.RestorePoints.CaptureOnlyResult result;
            try
            {
                var rp = App.AppHost.Services.GetRequiredService<RestorePointService>();
                result = await Task.Run(() => rp.CaptureGameOnlyAsync(gameId));
            }
            catch (Exception e)
            {
                result = new ModManager.Core.RestorePoints.CaptureOnlyResult(false, ErrorRemedy.Describe(e), null);
            }

            // From what is stored now: the player may have chosen or snoozed while the snapshot copied.
            var saved = _svc.UpdatePatchDay(gameId, rec =>
            {
                if (rec is null || !PatchDay.NeedsRestorePoint(rec)) return rec;
                return result.Ok && result.RestorePointTimestamp is { } id
                    ? PatchDay.WithRestorePoint(rec, id)
                    : PatchDay.WithRestorePointError(rec, result.RefusedReason ?? "the snapshot could not be written");
            });
            RestorePointSettled?.Invoke(gameId, saved);
        }
        finally { _capturing.Remove(gameId); }
    }
}
