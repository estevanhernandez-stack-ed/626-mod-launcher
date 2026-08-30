using System.IO;
using ModManager.Core;
using ModManager.Core.Transport;

namespace ModManager.App.Services;

/// <summary>
/// Gathers what a profile archive needs, from every registered game.
///
/// <para>App-side because it walks the registry and runs the scanner; <see cref="ProfileArchive"/>
/// stays pure and takes the result. That split is what lets the archive be tested without a machine
/// full of games.</para>
///
/// <para><b>Every mod path is led by the NAME of the location it came from.</b> A game can keep mods
/// in more than one place — Windrose keeps two — and a flat list loses which is which, so two
/// same-named mods in different locations collide in the archive and one silently wins.</para>
///
/// <para><b>Mods are collected as the scanner's file list, never a folder.</b> Mods sit intermixed
/// with game content — measuring the mod folders gave 159 GB, mostly Palworld's base-game paks and
/// Death Stranding's entire data directory, against a real answer of 3.5 GB. A folder-level copy would
/// haul the game, and a folder-level restore would overwrite it.</para>
/// </summary>
public sealed class ProfileArchiveBuilder
{
    private readonly LauncherService _svc;

    public ProfileArchiveBuilder(LauncherService svc) => _svc = svc;

    /// <summary>
    /// Build the source list. Reads only — no game folder or save is touched, here or downstream.
    /// </summary>
    /// <param name="onProgress">Called per game with its name, so a 4 GB gather does not look hung.</param>
    public IReadOnlyList<ProfileGameSource> Gather(Action<string>? onProgress = null)
    {
        var sources = new List<ProfileGameSource>();

        foreach (var game in _svc.LoadRegistry().Games.OrderBy(g => g.Id, StringComparer.Ordinal))
        {
            onProgress?.Invoke(game.GameName ?? game.Id);

            GameContext ctx;
            try { ctx = Scanner.GameContext(game); }
            catch { continue; }   // a game whose context will not resolve is skipped, not fatal

            var (modFiles, mods) = CollectMods(ctx);

            sources.Add(new ProfileGameSource(
                new BundleGame(game.Id, game.SteamAppId, game.GameName),
                Directory.Exists(game.SaveDir ?? "") ? game.SaveDir : null,
                modFiles,
                mods,
                Directory.Exists(ctx.DataDir) ? ctx.DataDir : null)
            {
                ModLocations = ctx.Locations.Select(l => l.Name).ToList(),
            });
        }

        return sources;
    }

    private static (IReadOnlyList<BundlePlanFile> Files, IReadOnlyList<BundleMod> Mods) CollectMods(GameContext ctx)
    {
        var files = new List<BundlePlanFile>();
        var mods = new List<BundleMod>();

        IReadOnlyList<Mod> found;
        Dictionary<string, ModMeta> meta;
        try
        {
            found = Scanner.ListClassified(ctx);
            meta = Scanner.LoadMetadata(ctx);
        }
        catch { return (files, mods); }

        foreach (var m in found)
        {
            // The Nexus id lives on the sidecar ModMeta, not on Mod, and it is what lets the far end
            // offer a LINK for a missing mod rather than just naming it. Keyed Base-then-Name, the
            // same rule Metadata.MergeMetadata uses - a second rule here would drift from that one.
            ModMeta? e = null;
            if (!string.IsNullOrEmpty(m.Base)) meta.TryGetValue(m.Base, out e);
            if (e is null) meta.TryGetValue(m.Name, out e);

            mods.Add(new BundleMod(m.Name, m.Version, e?.NexusModId, m.Enabled));
            try
            {
                var loc = Scanner.LocByName(m.Location, ctx);
                var asFolder = Path.Combine(loc.Abs, m.Name);

                if (Directory.Exists(asFolder))
                {
                    foreach (var f in Directory.EnumerateFiles(asFolder, "*", SearchOption.AllDirectories))
                        files.Add(new BundlePlanFile(f,
                            loc.Name + "/" + Path.Combine(m.Name, Path.GetRelativePath(asFolder, f)).Replace('\\', '/')));
                }
                else
                {
                    // Loose-file mods: the scanner already knows exactly which files are this mod's,
                    // which is the whole reason we do not copy the folder they sit in.
                    foreach (var rel in m.Files)
                    {
                        var p = Path.Combine(loc.Abs, rel);
                        if (File.Exists(p))
                            files.Add(new BundlePlanFile(p, loc.Name + "/" + rel.Replace('\\', '/')));
                    }
                }
            }
            catch { /* one unreadable mod must not cost the archive the other 193 */ }
        }

        return (files, mods);
    }
}
