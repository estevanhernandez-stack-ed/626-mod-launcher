using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>A game-store library adapter. The IO lives here (filesystem/registry); pure parsing +
/// selection logic lives in Core. Steam is the only implementation today; GOG/Epic/Xbox satisfy the
/// same contract on demand (StoreIds already carries their ids, so no schema migration is needed).</summary>
public interface IStoreLibrary
{
    /// <summary>Stable lowercase store key, e.g. "steam".</summary>
    string StoreKind { get; }

    /// <summary>Installed games discovered from the store's on-disk metadata.</summary>
    IReadOnlyList<InstalledGame> InstalledGames();

    /// <summary>Absolute path to a locally-cached cover image for the app id, or null if none.
    /// Defaults to the landscape capsule (the title-bar switcher's shape).</summary>
    string? ResolveCoverArtPath(string appId);

    /// <summary>Absolute path to a locally-cached cover of the requested shape (portrait grid art for
    /// the Library home's cards, landscape capsule for the switcher), or null if none is cached.</summary>
    string? ResolveCoverArtPath(string appId, CoverShape shape);
}
