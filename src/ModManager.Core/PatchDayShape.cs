using System.Text.Json;

namespace ModManager.Core;

/// <summary>The loader a patchDay block names, with the date of its file on disk.</summary>
public sealed record PatchDayLoaderShape(string Name, DateTime FileUtc);

/// <summary>The feed's newest release of that loader. Descriptive: never "fixed".</summary>
public sealed record PatchDayReleaseShape(string Version, DateTime PublishedUtc, string Url);

/// <summary>
/// The read-only <c>patchDay</c> block of <c>get_game_shape</c>. Derived from the persisted record, the
/// launch-mode file check and one stale-loader scan. There is no live Steam build here (the server does not
/// read Steam), so the build-moved trigger is never evaluated: the block reports only states a record already
/// implies (patched, playingVanilla, updateAvailable, loaderUpdated). No record means no block (null).
/// </summary>
public sealed record PatchDayShape(
    string State, string DetectedBuild, string Choice, bool Snoozed, string? RestorePoint,
    string? RestorePointError, PatchDayLoaderShape? Loader, PatchDayReleaseShape? LatestRelease)
{
    public static string StateName(PatchDayState s) => JsonNamingPolicy.CamelCase.ConvertName(s.ToString());

    /// <summary>Null when the game has no patch record.</summary>
    public static PatchDayShape? Of(GameEntry game, GameContext ctx, IReadOnlyList<LoaderRelease> releases)
    {
        var record = game.PatchDay;
        if (record is null) return null;

        var mode = VanillaLaunch.CurrentMode(ctx.DataDir);
        var report = mode == LaunchMode.Vanilla && VanillaStashStore.Load(ctx.DataDir) is { } stash
            ? StaleLoaders.Find(ctx, stash)
            : StaleLoaders.Find(ctx);

        var verdict = PatchDay.Evaluate(new PatchDayInput(
            game.LastKnownSteamBuildId, null, record, report, releases, mode));

        var loader = verdict.Loader is { } l ? new PatchDayLoaderShape(l.Name, l.LoaderUtc) : null;

        // Newest feed row for the named loader, even when it is not newer than the file on disk.
        var latest = verdict.Release;
        if (latest is null && verdict.Loader is { } named)
            latest = releases
                .Where(r => string.Equals(r.Loader, named.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.PublishedUtc).FirstOrDefault();

        return new PatchDayShape(
            StateName(verdict.State), record.BuildId,
            JsonNamingPolicy.CamelCase.ConvertName(record.Choice.ToString()),
            record.SnoozedUntilChange, record.RestorePointId, record.RestorePointError, loader,
            latest is null ? null : new PatchDayReleaseShape(latest.Version, latest.PublishedUtc, latest.Url));
    }
}
