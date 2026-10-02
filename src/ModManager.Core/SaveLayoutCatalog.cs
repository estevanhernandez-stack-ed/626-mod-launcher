using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Live game -> save layout lookup, a facade over <see cref="EffectiveManifest"/> (twin of
/// <see cref="BanRiskCatalog"/>).
///
/// <para><b>This replaces a single hardcoded app id.</b> The layout used to be
/// <c>steamAppId == "1623730" ? Worlds : TypedFiles</c> — one game recognised, and a claim of
/// <c>TypedFiles</c> asserted over 149 others nobody had ever checked, roughly 30 of which are
/// folder-per-save. A new one is now a data PR to the feed rather than an app release, exactly as it
/// already is for engines and mod paths.</para>
///
/// <para>Resolving live rather than off a persisted field means a feed correction reaches a game the
/// user already added, with no migration.</para>
///
/// <para><b>Unknown still resolves to <see cref="SaveLayout.TypedFiles"/>.</b> That is what every game
/// does today and the floor the panel already handles — whole-folder backup and restore. The
/// manifest's null is meaningful to a CURATOR (nobody looked) but must not become a third runtime
/// state the UI has to explain.</para>
/// </summary>
public static class SaveLayoutCatalog
{
    /// <summary>Parse a manifest value. Anything unrecognised — including a word from a newer feed
    /// this binary has never heard of — is the default, never a throw.</summary>
    public static SaveLayout Parse(string? value)
        => string.Equals(value, "worlds", StringComparison.OrdinalIgnoreCase)
            ? SaveLayout.Worlds
            : SaveLayout.TypedFiles;

    /// <summary>The declared layout for a registered game, resolved through every identity it carries
    /// (<see cref="ManifestIdLookup.ConfirmedEntryFor"/>), or <see cref="SaveLayout.TypedFiles"/>. There
    /// is no by-app-id form: a game with no Steam id would be shut out by it.</summary>
    public static SaveLayout For(GameEntry? game)
        => Parse(ManifestIdLookup.ConfirmedEntryFor(game)?.SaveLayout);
}
