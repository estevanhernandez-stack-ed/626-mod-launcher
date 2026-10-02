using ModManager.Core.Transport;

namespace ModManager.Core;

/// <summary>
/// Whether the launcher may write into a game's save folder at all. One answer, read by every path
/// that does: restore, per-type restore, clone, bundle import, character edit, save mods, world edits,
/// and the machine-transport restore.
///
/// <para><b>EA app games: read and back up, never write, for now.</b> The EA app syncs these saves to
/// EA's cloud at both ends of every session, and the owner's recorded decision for EA football is
/// "option 1, scoped to roster files first": writes start with roster files only, through a gate
/// that also checks the EA app is closed (grand plan, section 9 and Band D). None of that is built.
/// A whole-folder restore writes every file, career saves and the profile included, into the synced
/// folder, so it is exactly what the decision rules out. Backups are untouched: a snapshot reads the
/// save folder and writes into the launcher's own data folder.</para>
///
/// <para>Keyed on store identity, the registration's own EA content id or the one its manifest entry
/// carries, never on a name: a game added by hand that resolves to an EA entry is the same game with
/// the same cloud sync.</para>
/// </summary>
public static class SaveWritePolicy
{
    /// <summary>What the saves panel says up front for such a game, before anyone reaches for a write.</summary>
    public const string EaNotice =
        "Backups only. This game's saves sync to EA's cloud, so the launcher doesn't write into them yet.";

    /// <summary>What a refused write says. Same sentence, plus the fact the player needs most.</summary>
    public const string EaRefusal = EaNotice + " Nothing was changed.";

    /// <summary>The up-front notice for this game, or null when its saves are writable.</summary>
    public static string? Notice(GameEntry? game) => Refusal(game) is null ? null : EaNotice;

    /// <summary>Null when the launcher may write into this game's save folder; otherwise the sentence
    /// to show the player, which says nothing was changed.</summary>
    public static string? Refusal(GameEntry? game)
    {
        if (game is null) return null;
        var eaId = !string.IsNullOrWhiteSpace(game.EaContentId)
            ? game.EaContentId
            : ManifestIdLookup.ConfirmedEntryFor(game)?.Stores.EaContentId;
        return string.IsNullOrWhiteSpace(eaId) ? null : EaRefusal;
    }

    /// <summary>The parts of a machine-transport restore this game may take: everything asked for,
    /// less <see cref="RestoreParts.Saves"/> when its saves are not the launcher's to write.</summary>
    public static RestoreParts Permitted(GameEntry? game, RestoreParts asked)
        => Refusal(game) is null ? asked : asked & ~RestoreParts.Saves;
}
