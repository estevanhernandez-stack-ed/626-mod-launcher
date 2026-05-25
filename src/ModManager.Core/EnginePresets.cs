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
        var entry = new GameEntry
        {
            Id = id,
            GameName = name,
            Engine = string.IsNullOrEmpty(input.Engine) ? "custom" : input.Engine,
            WindowTitle = input.WindowTitle ?? (name + " Mod Launcher"),
            GameRoot = input.GameRoot ?? "",
            FileExtensions = input.FileExtensions ?? preset.FileExtensions,
            GroupingRule = input.GroupingRule ?? preset.GroupingRule,
            ModLocations = new[] { new ModLocation("mods", "mods", input.ModPath ?? preset.ModPath) },
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
        return entry;
    }
}
