using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>A mod location resolved to absolute paths (+ mirrors), with primary flag.</summary>
public sealed record ModLocationCtx(string Name, string Label, string Abs, IReadOnlyList<string> Mirrors, bool Primary);

/// <summary>
/// A registry game entry resolved into an absolute working context: where mods live,
/// where the launcher's own data (disabled/profiles/classification/metadata) lives, and
/// how files group. Produced by Scanner.GameContext. Mirrors scanner.js gameContext.
/// </summary>
public sealed class GameContext
{
    public required GameEntry Game { get; init; }
    public required string GameRoot { get; init; }
    public required string DataDir { get; init; }
    public required string DisabledRoot { get; init; }
    public required string ProfilesDir { get; init; }
    public required string SavesDir { get; init; }
    public required string ClassificationPath { get; init; }
    public required string MetadataPath { get; init; }
    public required string LoadOrderPath { get; init; }
    public string? SaveDir { get; init; }
    public required IReadOnlyList<string> Exts { get; init; }
    public required Regex FileRe { get; init; }
    public required IReadOnlyList<ModLocationCtx> Locations { get; init; }
    public required string GroupingRule { get; init; }
    public required string ScanSubfolders { get; init; }
    public bool HasGame { get; init; }
}

/// <summary>An item intake skipped, with why (already installed / not a mod file / error).</summary>
public sealed record SkippedItem(string Name, string Reason);

/// <summary>The outcome of an intake: what was placed and what was skipped (with reasons).</summary>
public sealed class IntakeResult
{
    public List<string> Added { get; } = new();
    public List<SkippedItem> Skipped { get; } = new();
}

/// <summary>Result of a name-search metadata refresh.</summary>
public sealed record RefreshResult(int Matched, int Total, int? GameId);

/// <summary>Result of a fingerprint identification pass.</summary>
public sealed record IdentifyResult(int Matched);

/// <summary>Options for a metadata refresh (an explicit gameId overrides resolution).</summary>
public sealed class RefreshOptions
{
    public int? GameId { get; init; }
}
