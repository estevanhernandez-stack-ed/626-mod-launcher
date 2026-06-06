using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Pure engine presets + registry-entry assembly for the Add Game wizard. A preset seeds
/// mod-folder layout / extensions / grouping per engine; the wizard lets the user tweak
/// before saving. Mirrors engine-presets.js.
/// </summary>
public static partial class EnginePresets
{
    public static IReadOnlyDictionary<string, EnginePreset> Presets { get; } = new Dictionary<string, EnginePreset>
    {
        ["ue-pak"] = new("Unreal Engine 4/5 (.pak)", new[] { "pak", "ucas", "utoc" }, "strip_underscore_p_suffix", "Content/Paks/~mods",
            "Mods are pak/ucas/utoc under <Project>/Content/Paks/~mods. Edit the path if your project folder differs (e.g. R5/Content/Paks/~mods)."),
        ["bethesda"] = new("Bethesda (Creation Engine)", new[] { "esp", "esl", "esm", "bsa" }, "filename_no_ext", "Data",
            "Skyrim/Fallout/Starfield mods live in the Data folder."),
        ["minecraft"] = new("Minecraft (Forge/Fabric)", new[] { "jar" }, "filename_no_ext", "mods",
            "Jar mods in the mods folder."),
        ["bepinex"] = new("BepInEx (Unity)", new[] { "dll" }, "filename_no_ext", "BepInEx/plugins",
            "Valheim/Lethal Company etc. — DLLs in BepInEx/plugins."),
        ["smapi"] = new("SMAPI (Stardew Valley)", Array.Empty<string>(), "by_folder", "Mods",
            "Each mod is a folder containing manifest.json under Mods/."),
        ["source"] = new("Source / Source 2", new[] { "vpk" }, "filename_no_ext", "addons",
            "VPK addons under <game>/addons."),
        ["melonloader"] = new("MelonLoader (Unity)", new[] { "dll" }, "filename_no_ext", "Mods",
            "DLL mods in the Mods folder."),
        ["fromsoft"] = new("FromSoftware (Mod Engine 2)", Array.Empty<string>(), "by_folder", "mod",
            "Elden Ring / Dark Souls / Sekiro / Armored Core. Each mod is a folder under 'mod', loaded by Mod Engine 2."),
        ["custom"] = new("Custom (set manually)", new[] { "pak" }, "filename_no_ext", "mods",
            "Set the extensions, grouping, and mod folder yourself."),
    };

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlnumRe();

    [GeneratedRegex(@"^-+|-+$")]
    private static partial Regex EdgeDashRe();

    public static string Slugify(string? name)
    {
        var s = EdgeDashRe().Replace(NonAlnumRe().Replace((name ?? "").ToLowerInvariant(), "-"), "");
        return s.Length > 0 ? s : "game";
    }

    /// <summary>Ensure the id is unique against existing ids (append -2, -3, ...).</summary>
    public static string UniqueId(string baseId, IEnumerable<string>? existingIds)
    {
        var set = new HashSet<string>(existingIds ?? Enumerable.Empty<string>());
        if (!set.Contains(baseId)) return baseId;
        var n = 2;
        while (set.Contains(baseId + "-" + n)) n++;
        return baseId + "-" + n;
    }

    public static GameEntry BuildGameEntry(GameInput input, IEnumerable<string>? existingIds)
    {
        var preset = (input.Engine is not null && Presets.TryGetValue(input.Engine, out var p)) ? p : Presets["custom"];
        var id = UniqueId(Slugify(input.Id ?? input.Name), existingIds);
        var name = string.IsNullOrEmpty(input.Name) ? "Game" : input.Name;
        var engine = string.IsNullOrEmpty(input.Engine) ? "custom" : input.Engine;

        // Auto-detect the ue-pak mod layout (loader ~mods vs loader-less paks-root) when the user didn't
        // set an explicit ModPath and we have a gameRoot to inspect. Falls back to the static preset path.
        ModLocation modLocation;
        if (string.IsNullOrEmpty(input.ModPath)
            && string.Equals(engine, "ue-pak", StringComparison.OrdinalIgnoreCase)
            && DetectUePakModLocation(input.GameRoot ?? "") is { } detected)
        {
            modLocation = detected;
        }
        else
        {
            modLocation = new ModLocation("mods", "mods", input.ModPath ?? preset.ModPath);
        }

        var entry = new GameEntry
        {
            Id = id,
            GameName = name,
            Engine = engine,
            WindowTitle = input.WindowTitle ?? (name + " Mod Launcher"),
            GameRoot = input.GameRoot ?? "",
            FileExtensions = input.FileExtensions ?? preset.FileExtensions,
            GroupingRule = input.GroupingRule ?? preset.GroupingRule,
            ModLocations = new[] { modLocation },
        };
        if (!string.IsNullOrEmpty(input.SteamAppId))
        {
            entry.SteamAppId = input.SteamAppId;
            entry.LaunchUrl = "steam://rungameid/" + input.SteamAppId;
        }
        if (!string.IsNullOrEmpty(input.LaunchExe)) entry.LaunchExe = input.LaunchExe;
        if (!string.IsNullOrEmpty(input.RequiredLauncher)) entry.RequiredLauncher = input.RequiredLauncher;
        if (input.CurseforgeGameId is not null) entry.CurseforgeGameId = input.CurseforgeGameId;
        if (!string.IsNullOrEmpty(input.SaveModPath)) entry.SaveModPath = input.SaveModPath;
        if (input.SaveModForbidden is { Count: > 0 }) entry.SaveModForbidden = input.SaveModForbidden;
        // Explicit domain (AI profile / manual) wins; otherwise resolve from the Steam app id so
        // Steam-auto-added + quick-pick games still get a Nexus domain for md5 metadata identify.
        entry.NexusGameDomain = !string.IsNullOrEmpty(input.NexusGameDomain)
            ? input.NexusGameDomain
            : NexusDomains.ByAppId(input.SteamAppId);
        return entry;
    }

    // ue-pak add-time detection: find <Project>/Content/Paks under gameRoot and decide the mod layout.
    // Loader present (a ~mods or LogicMods subfolder exists) -> keep the ~mods convention (Form null).
    // Loader-less (no such subfolder) -> manage Content/Paks directly with the base-game-filtering
    // paks-root form. Returns null when no <Project>/Content/Paks is found on disk (caller falls back
    // to the static preset path).
    private static ModLocation? DetectUePakModLocation(string gameRoot)
    {
        if (string.IsNullOrEmpty(gameRoot)) return null;
        string[] projectDirs;
        try { projectDirs = Directory.GetDirectories(gameRoot); }
        catch { return null; }

        foreach (var projDir in projectDirs)
        {
            var paks = Path.Combine(projDir, "Content", "Paks");
            if (!Directory.Exists(paks)) continue;
            var project = Path.GetFileName(projDir);
            var modsConvention = Directory.Exists(Path.Combine(paks, "~mods"))
                                 || Directory.Exists(Path.Combine(paks, "LogicMods"));
            return modsConvention
                ? new ModLocation("mods", "mods", $"{project}/Content/Paks/~mods")
                : new ModLocation("mods", "Paks", $"{project}/Content/Paks") { Form = "paks-root" };
        }
        return null; // no <Project>/Content/Paks on disk
    }
}
