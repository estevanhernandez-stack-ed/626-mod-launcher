namespace ModManager.Core;

/// <summary>One installed game discovered from a store library. Store-agnostic so GOG/Epic/Xbox
/// adapters can return the same shape later (see IStoreLibrary). <see cref="BuildId"/> is the store's
/// installed-version stamp (Steam's appmanifest buildid) — used by the Phase 2 "game updated under
/// your mods" check; null when the store doesn't expose one.</summary>
public sealed record InstalledGame(string StoreKind, string AppId, string Name, string InstallDir)
{
    public string? BuildId { get; init; }

    /// <summary>Steam's last-played unix timestamp (seconds), or null. Read live for recently-played
    /// ordering; never persisted (behavioral data stays off disk).</summary>
    public string? LastPlayed { get; init; }
}
