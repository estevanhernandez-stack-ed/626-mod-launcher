namespace ModManager.Core;

/// <summary>What Play on the library home does with a game.</summary>
public enum HomePlayKind
{
    /// <summary>Start <see cref="HomePlayVerdict.Target"/> from the home. The game view would do nothing more.</summary>
    Launch,
    /// <summary>The game has no launch targets: the legacy <c>LaunchUrl</c> / <c>LaunchExe</c> start, as the game view does.</summary>
    LaunchLegacy,
    /// <summary>Open the game view and run its Play, which asks and guards what the home can't.</summary>
    OpenGameView,
}

public sealed record HomePlayVerdict(HomePlayKind Kind, LaunchTarget? Target, string Reason);

/// <summary>
/// Whether Play on the library home can launch in place or must hand off to the game view's Play.
/// Pure: the App supplies the probes (Seamless installed, Steam running, would the save offer ask).
/// <para>The home used to run the game's default target whatever the mode, so an Elden Ring in vanilla mode
/// started Mod Engine 2 or Seamless, both without anti-cheat. The target here is always the one
/// <see cref="LaunchTargetPicker"/> picks for the game view. Anything the game view would stop to ask about,
/// or guard first, hands off rather than being skipped.</para>
/// </summary>
public static class HomePlay
{
    public static HomePlayVerdict Decide(GameEntry game, LaunchMode mode, bool seamlessFullyInstalled, bool steamRunning,
        Func<LaunchTarget, LaunchMode, bool> saveOfferWouldAsk)
    {
        // The game view's required-launcher enforcement depends on which mods are on, which the home doesn't load.
        if (mode == LaunchMode.Modded && !string.IsNullOrEmpty(game.RequiredLauncher))
            return Hand("it needs its own launcher");

        var target = LaunchTargetPicker.Pick(game, mode, seamlessFullyInstalled);
        if (target is null)
            return mode == LaunchMode.Vanilla
                ? Hand("there's no way to start it without a mod launcher")
                : new HomePlayVerdict(HomePlayKind.LaunchLegacy, null, "no launch targets");

        if (LaunchGuard.NeedsSteamRunning(game, target) && !steamRunning)
            return Hand("Steam needs to start first");

        bool asks;
        try { asks = saveOfferWouldAsk(target, mode); }
        catch { asks = true; }   // can't tell: let the game view ask rather than launch unasked
        if (asks) return Hand("a save from another mode could be copied over");

        return new HomePlayVerdict(HomePlayKind.Launch, target, "plain launch");
    }

    private static HomePlayVerdict Hand(string reason) => new(HomePlayKind.OpenGameView, null, reason);
}
