using ModManager.Core;

namespace ModManager.Tests;

/// <summary>A registration as the save lookups see it. The save lookups take the whole game (an EA app
/// game has no Steam id to ask by), so the tests that used to pass an app id build one of these.</summary>
internal static class SaveTestGames
{
    public static GameEntry Steam(string? steamAppId, string? engine = null)
        => new() { Id = "test-" + (string.IsNullOrEmpty(steamAppId) ? "none" : steamAppId), Engine = engine, SteamAppId = steamAppId };
}
