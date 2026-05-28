using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.App.Services;

/// <summary>App adapter over the headless RestorePointOrchestrator: supplies %APPDATA% paths, the DPAPI
/// nexus gate, the Process probe, and the LauncherService-backed game provider; renders + writes the
/// off-boarding sheet into each game folder after a clear (best-effort, snapshot-on-collision).</summary>
public sealed class RestorePointService
{
    private readonly RestorePointOrchestrator _orch;
    private readonly LauncherService _launcher;
    private readonly NexusService _nexus;

    public RestorePointService(LauncherService launcher, NexusService nexus)
    {
        _launcher = launcher;
        _nexus = nexus;
        var dataRoot = LauncherService.DataRoot;
        var rpRoot = Path.Combine(dataRoot, "restore-points");
        var version = typeof(RestorePointService).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        _orch = new RestorePointOrchestrator(dataRoot, rpRoot, version,
            new LauncherGameProvider(launcher), new NexusGate(nexus), new GameProcessProbe());
    }

    /// <summary>Whether the user currently has a Nexus API key stored. Exposed so the Safe Clear
    /// dialog can conditionally show the "keep my Nexus key" toggle.</summary>
    public bool NexusConnected => _nexus.IsConnected;

    private static string Ts() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

    public async Task<SafeClearResult> SafeClearAsync(SafeClearOptions opts, CancellationToken ct = default)
    {
        var result = await _orch.SafeClearAsync(opts, Ts(), ct);
        if (result.Ok && opts.CreateRestorePoint && result.RestorePointTimestamp is not null)
            WriteOffBoardingSheets(result.RestorePointTimestamp);
        return result;
    }

    public Task<RestoreResult> RestoreAsync(string timestamp, CancellationToken ct = default)
        => _orch.RestoreAsync(timestamp, ct);

    public IReadOnlyList<RestorePointInfo> ListRestorePoints() => _orch.ListRestorePoints();
    public void DeleteRestorePoint(string ts) => _orch.DeleteRestorePoint(ts);
    public InterruptedClear? DetectInterruptedClear() => _orch.DetectInterruptedClear();
    public void DiscardPartial(string ts) => _orch.DiscardPartial(ts);

    private void WriteOffBoardingSheets(string timestamp)
    {
        var rpDir = Path.Combine(LauncherService.DataRoot, "restore-points", timestamp);
        var manifest = RestorePointManifestStore.Read(rpDir);
        if (manifest is null) return;
        foreach (var ga in manifest.Games)
        {
            if (ga.OffboardingSheetGameFolderPath is null) continue;
            try
            {
                var report = OffBoardingHydrator.Hydrate(ga, rpDir);   // self-contained — no games.json / LaunchScan
                var text = OffBoardingSheet.Render(report);
                var sheetPath = ga.OffboardingSheetGameFolderPath;
                if (File.Exists(sheetPath))
                {
                    var batch = ReplacedStore.NewBatch(Path.Combine(rpDir, "games", ga.Id, "replaced-sheet"));
                    var rel = Path.GetFileName(sheetPath);
                    ReplacedStore.Backup(sheetPath, rel, batch);
                    ReplacedStore.WriteManifest(batch, new[] { new ReplacedStore.ReplacedEntry(sheetPath, rel, DateTime.UtcNow) });
                }
                AtomicJson.WriteTextAtomic(sheetPath, text);
            }
            catch { /* best-effort: the authoritative sheet is already in the restore point */ }
        }
    }

    private sealed class NexusGate(NexusService nexus) : INexusGate
    {
        public bool IsConnected => nexus.IsConnected;
        public void DeleteStoredKey() => nexus.Disconnect();
    }

    private sealed class LauncherGameProvider(LauncherService launcher) : IGameProvider
    {
        public IReadOnlyList<GameEntry> Games => launcher.LoadRegistry().Games;
        public GameContext ContextFor(GameEntry game) => Scanner.GameContext(game);
        public void Reload() => launcher.NotifyRegistryChanged();
    }
}
