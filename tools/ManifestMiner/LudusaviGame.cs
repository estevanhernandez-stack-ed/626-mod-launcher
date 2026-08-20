namespace ManifestMiner;

/// <summary>
/// One save-path declaration from Ludusavi, with the two facts that decide whether it is the path we
/// want: what it holds, and where it applies.
///
/// <para><b>Both were being thrown away.</b> The parser deserialized <c>tags</c> and then took
/// <c>Files.Keys</c>, and never looked at <c>when</c> at all — so the shipped hint was simply the
/// first key in a YAML map. Across 148 hints that produced 24 non-Windows paths, 38 config
/// directories and 12 game-install-relative ones, including Palworld's, which pointed at the
/// Microsoft Store container rather than the Steam folder the app actually reads.</para>
/// </summary>
/// <param name="Path">The raw Ludusavi path, placeholders intact (<c>&lt;winAppData&gt;/…</c>).</param>
/// <param name="Tags">Ludusavi's own classification. The entire vocabulary is <c>save</c> and
/// <c>config</c>; an untagged path is neither, and is not a save.</param>
/// <param name="Os">Operating systems this path applies to. Empty means "all".</param>
/// <param name="Stores">Storefronts this path applies to. Empty means "all". This is what separates
/// Palworld's Microsoft Store container from its Steam folder.</param>
public sealed record LudusaviSavePath(
    string Path,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Os,
    IReadOnlyList<string> Stores);

public sealed record LudusaviGame(string Name)
{
    public string? SteamAppId { get; init; }
    public IReadOnlyList<string> InstallDirs { get; init; } = Array.Empty<string>();

    /// <summary>Every path Ludusavi declares, save and config alike, with their qualifiers.</summary>
    public IReadOnlyList<LudusaviSavePath> SaveEntries { get; init; } = Array.Empty<LudusaviSavePath>();
}
