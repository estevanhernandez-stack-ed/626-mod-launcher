namespace ModManager.Core.Frameworks;

/// <summary>
/// A framework the launcher knows how to install. Parallel to <see cref="Tools.KnownTool"/>
/// — same shape (id + display + engine + author + get-url + zip-signature hints) but for
/// frameworks (UE4SS, BepInEx, Elden Mod Loader, ME2, etc.) instead of tools. Day-one entry
/// is Elden Mod Loader; adding a new framework is a one-record addition to the catalog.
///
/// Pure data — detection logic lives in <see cref="Classify"/>; install logic lives in
/// <see cref="FrameworkInstaller"/>.
/// </summary>
public sealed record KnownFramework(
    string FrameworkId,
    string DisplayName,
    string Engine,
    string? SteamAppId,
    string GetUrl,
    string Author,
    IReadOnlyList<string> ZipFilenameHints,
    IReadOnlyList<string> ZipSignatureFiles,
    string InstallRoot,
    IReadOnlyList<string> ForbiddenPaths)
{
    /// <summary>
    /// Day-one catalog. Each entry is intake-installable: we know exactly what files to
    /// drop where, with reversibility tracked separately. Frameworks we DETECT but don't
    /// INSTALL (UE4SS, BepInEx, etc.) stay in <see cref="FrameworkDeps.Catalog"/>.
    /// </summary>
    public static IReadOnlyList<KnownFramework> Catalog { get; } = new[]
    {
        new KnownFramework(
            FrameworkId: "elden-mod-loader",
            DisplayName: "Elden Mod Loader",
            Engine: "fromsoft",
            SteamAppId: "1245620",
            GetUrl: "https://www.nexusmods.com/eldenring/mods/117",
            Author: "TechieW",
            ZipFilenameHints: new[] { "elden", "mod", "loader" },
            // Signature files that prove a zip is ELM. mod_loader_config.ini is unique to ELM
            // (dinput8.dll alone is shared by many DLL proxies); both must be present.
            ZipSignatureFiles: new[] { "dinput8.dll", "mod_loader_config.ini" },
            // FromSoft games (ER / Sekiro / DS3) put the exe under <gameRoot>/Game/. ELM's
            // dinput8.dll proxy chain-loads ONLY when next to the exe — so install must land
            // in Game/, not at the ship root. PlayFolder resolves <gameRoot>/Game when it
            // exists, else gameRoot (which keeps non-Game-subfolder layouts working).
            InstallRoot: "PlayFolder",
            // ER's game executable must never be overwritten by a framework install.
            ForbiddenPaths: new[] { "eldenring.exe", "start_protected_game.exe" }),

        new KnownFramework(
            FrameworkId: "ue4ss",
            DisplayName: "UE4SS",
            Engine: "ue-pak",
            // No SteamAppId: UE4SS is a generic Unreal loader that works across many ue-pak games
            // (Windrose, Palworld, Hogwarts, ...) — unlike Elden Mod Loader, which is ER-only and pins
            // its app id. Null app id makes Classify match on engine alone.
            SteamAppId: null,
            GetUrl: "https://github.com/UE4SS-RE/RE-UE4SS/releases",
            Author: "RE-UE4SS team",
            // Proxy DLL is an advisory HINT, not a signature: UE4SS ships the loader as dwmapi.dll OR
            // xinput1_3.dll OR d3d11.dll depending on release/game, so requiring one would miss variants.
            ZipFilenameHints: new[] { "ue4ss", "zdev", "dwmapi", "xinput1_3", "d3d11" },
            // The UE4SS-unique pair — present in every release regardless of which proxy it ships.
            ZipSignatureFiles: new[] { "UE4SS.dll", "UE4SS-settings.ini" },
            // Installs into <gameRoot>/<projectSubfolder>/Binaries/Win64 (e.g. R5/Binaries/Win64) — the
            // proxy DLL must sit next to the game exe there to chain-load. Resolved by the project-aware
            // arm of FrameworkInstaller.ResolveInstallRoot (needs the game's mod-location paths).
            InstallRoot: "UeProjectBinariesWin64",
            // The game exe lives in that same Binaries/Win64 folder; its name is per-game
            // (<Project>-Win64-Shipping.exe), so a leading-* suffix glob protects it across games.
            // PathGate scopes the * match to top-level entries, so a same-suffix file nested in the
            // payload (ue4ss/Mods/...) is not refused.
            ForbiddenPaths: new[] { "*-Shipping.exe" }),
    };

    /// <summary>
    /// Classifier result: which catalog entry matched (or null), and whether the dropped
    /// zip "looks like" an unrecognized framework that warrants a feedback nudge.
    /// </summary>
    public sealed record ClassifyResult(KnownFramework? Match, bool LooksLikeFramework);

    /// <summary>
    /// Run the dropped zip's entry names through the catalog. Returns the first match (by
    /// signature-files-all-present) scoped to the active engine + Steam App ID. If no match
    /// but the zip has a DLL proxy at its root for a FromSoft game, flag LooksLikeFramework
    /// so the App can show a feedback nudge before falling through to mod intake.
    ///
    /// Pure: no IO. Caller pre-reads the archive entry names.
    /// </summary>
    public static ClassifyResult Classify(
        IEnumerable<string> zipEntryNames, string engine, string? steamAppId)
    {
        var entries = (zipEntryNames ?? Enumerable.Empty<string>())
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n.Replace('\\', '/'))
            .ToList();
        if (entries.Count == 0)
            return new ClassifyResult(null, false);

        var basenamesLower = entries
            .Select(n => System.IO.Path.GetFileName(n).ToLowerInvariant())
            .Where(n => n.Length > 0)
            .ToHashSet();

        foreach (var f in Catalog)
        {
            if (!string.Equals(f.Engine, engine, StringComparison.Ordinal)) continue;
            if (f.SteamAppId is not null
                && !string.Equals(f.SteamAppId, steamAppId, StringComparison.Ordinal)) continue;

            // Signature-files-ALL-present is the must. Filename-hints are advisory only;
            // future heuristics may use them but the current matcher trusts ZipSignatureFiles.
            bool allSigsPresent = f.ZipSignatureFiles.All(s =>
                basenamesLower.Contains(s.ToLowerInvariant()));
            if (allSigsPresent)
                return new ClassifyResult(f, false);
        }

        // No catalog hit. Looks-like heuristic: a FromSoft drop with a DLL proxy at the zip
        // root (no path segments — basename only) is probably a framework we don't know yet.
        bool looksLike = string.Equals(engine, "fromsoft", StringComparison.Ordinal)
            && entries.Any(e =>
                !e.Contains('/')
                && (string.Equals(System.IO.Path.GetFileName(e), "dinput8.dll", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(System.IO.Path.GetFileName(e), "version.dll", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(System.IO.Path.GetFileName(e), "winhttp.dll", StringComparison.OrdinalIgnoreCase)));

        return new ClassifyResult(null, looksLike);
    }
}
