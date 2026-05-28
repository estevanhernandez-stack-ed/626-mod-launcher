namespace ModManager.Core.RestorePoints;

/// <summary>Fully-hydrated input to <see cref="OffBoardingSheet.Render"/>. The App builds this from
/// GameEntry.LaunchTargets + LaunchScan + DirectInject.Detect + FrameworkRegistry + metadata. The
/// renderer touches NO filesystem and carries NO Nexus account/key — only mod source URLs.</summary>
public sealed record OffBoardingReport(
    string GameName,
    string RestorePointPath,
    IReadOnlyList<string> LaunchLines,
    IReadOnlyList<string> Frameworks,
    IReadOnlyList<OffBoardingModLine> Mods,
    IReadOnlyList<OffBoardingOwnedMod> OwnedMods);

public sealed record OffBoardingOwnedMod(string Name, string ManagedBy);

public sealed record OffBoardingModLine(
    string Name,
    string? SourceUrl,
    string? SourceConfidence,   // "manual" | "fingerprint" | "md5" | "nameSearch" | null
    string? InstalledDate);     // pre-formatted yyyy-MM-dd or null
