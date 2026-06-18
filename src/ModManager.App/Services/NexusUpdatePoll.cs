using System.IO;
using ModManager.Core;
using ModManager.Core.Plugins;
using ModManager.Plugins.Abstractions;

namespace ModManager.App.Services;

/// <summary>
/// The debounced Nexus auto-check, modeled on <see cref="UpdateChecker"/>. On game load — when the
/// auto-check setting is on, a Nexus <see cref="IModSource"/> plugin is loaded, Nexus is connected, the
/// game has a domain, and it's been more than 24h since the last poll for this game — it sweeps the
/// installed library's Nexus-identified mods <em>by mod id</em> (no archive required), refreshes live
/// stats + captures the upstream version (which drives the UPDATE chip), persists, and stamps the poll
/// time.
///
/// <para><b>Routes through the loaded plugin's <see cref="IModSource"/></b> (resolved from the shared
/// <see cref="ModSourceRegistry"/>) — not Core's <c>NexusClient</c>. Each candidate gets one per-mod
/// <see cref="IModSource.FetchMetadataAsync"/>; the DTO is folded onto the persisted <c>ModMeta</c> via
/// <see cref="SourceMetadataMapper.Apply"/>, which carries <c>Endorsed: null</c> and so never wipes the
/// user's heart. When no plugin is loaded (STORE flavor / zero-plugins) the source is null and the
/// auto-check is a clean no-op.</para>
///
/// <para>Comfort, not load-bearing: every failure (offline, 429, bad data) is swallowed and logged
/// via <see cref="AppDiagnostics"/>. The auto-check can never break a working session — exactly like
/// the manifest feed. The per-game stamp lives at
/// <c>%LOCALAPPDATA%\ModManagerBuilder\last-nexus-poll-&lt;gameId&gt;.txt</c> (mirrors the
/// update-check stamp).</para>
/// </summary>
public sealed class NexusUpdatePoll
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromHours(24);

    // Small inter-call delay between per-mod fetches — well under the Nexus burst ceiling. Mirrors the
    // manual "Refresh Nexus stats" sweep's throttle.
    private static readonly TimeSpan InterCallDelay = TimeSpan.FromMilliseconds(120);

    private static string StampPath(string gameId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModManagerBuilder",
        $"last-nexus-poll-{Sanitize(gameId)}.txt");

    // Game ids are slugs already, but keep the stamp filename safe regardless of what's in the id.
    private static string Sanitize(string id)
    {
        var chars = id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    /// <summary>
    /// Run the debounced auto-check for the active game, routed through the loaded Nexus
    /// <paramref name="source"/>. Returns true when something was refreshed and written (so the caller
    /// can reload rows to surface UPDATE chips); false when the check was skipped (no source / setting
    /// off / not connected / no domain / not due) or nothing changed. Never throws.
    /// </summary>
    public async Task<bool> MaybePollAsync(GameContext ctx, IModSource? source, NexusService nexus, AppSettingsService settings)
    {
        try
        {
            if (source is null) return false;           // STORE flavor / no plugin loaded — clean no-op
            if (!settings.AutoCheckModUpdates) return false;
            if (!nexus.IsConnected) return false;

            var domain = NexusDomains.Effective(ctx.Game);
            if (string.IsNullOrWhiteSpace(domain)) return false;

            var stampPath = StampPath(ctx.Game.Id);
            var last = NexusPollStamp.Read(stampPath);
            var now = DateTime.UtcNow;
            if (!NexusPollStamp.ShouldPoll(last, now, DebounceWindow)) return false;

            // Sweep every Nexus-identified mod by id (no bulk updated.json window — the IModSource
            // contract is per-mod; the per-mod fetch carries the upstream version that drives the chip).
            var byKey = Scanner.LoadMetadata(ctx);
            var identified = byKey.Where(kv => NexusRefresh.ResolveModId(kv.Value) is not null).ToList();

            var writes = new List<(string ModKey, ModMeta Meta)>();
            bool first = true;
            foreach (var (key, meta) in identified)
            {
                var modId = NexusRefresh.ResolveModId(meta)!.Value; // non-null: identified filter above

                if (!first) await Task.Delay(InterCallDelay);
                first = false;

                var modRef = new SourceModRef(SourceId: source.Id, GameDomain: domain!, ModId: modId, Version: meta.Version ?? "");
                var dto = await source.FetchMetadataAsync(modRef);
                if (dto is null) continue;

                // Fold live stats + the upstream version onto the persisted meta. Endorsed/Available are
                // null-safe (never clobber); NexusLatestVersion drives Mod.UpdateAvailable after reload.
                SourceMetadataMapper.Apply(meta, dto);
                writes.Add((key, meta));
            }

            bool wroteAnything = false;
            if (writes.Count > 0)
            {
                Scanner.WriteManyMeta(ctx, writes);
                wroteAnything = true;
            }

            NexusPollStamp.Write(stampPath, now);
            return wroteAnything;
        }
        catch (Exception ex)
        {
            // Comfort, not load-bearing — never surface a crash from the auto-check.
            AppDiagnostics.Log("nexus-auto-check", ex);
            return false;
        }
    }
}
