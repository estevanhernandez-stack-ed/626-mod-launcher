namespace ModManager.Core;

/// <summary>Curated info for a UE4SS framework mod that ships with the loader (not a downloaded mod).</summary>
public sealed record Ue4ssBuiltin(string Title, string Description, string DocsUrl);

/// <summary>
/// Bundled catalog of the mods that ship inside UE4SS itself (its default Mods folder). These have
/// no CurseForge/Nexus page, so we describe them from the UE4SS docs rather than leave them bare.
/// Keyed by folder name, case-insensitive. Pure data — no IO.
/// </summary>
public static class Ue4ssBuiltins
{
    private const string Docs = "https://docs.ue4ss.com";

    private static readonly Dictionary<string, Ue4ssBuiltin> Catalog = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BPModLoaderMod"] = new("Blueprint Mod Loader",
            "Loads Blueprint (LogicMods) pak mods. Required for blueprint mods to load. Ships with UE4SS.", Docs),
        ["BPML_GenericFunctions"] = new("BP ModLoader Generic Functions",
            "Helper library used by the Blueprint Mod Loader. Ships with UE4SS.", Docs),
        ["ConsoleEnablerMod"] = new("Console Enabler",
            "Enables the in-game Unreal console. Ships with UE4SS.", Docs),
        ["ConsoleCommandsMod"] = new("Console Commands",
            "Adds extra UE4SS console commands. Ships with UE4SS.", Docs),
        ["CheatManagerEnablerMod"] = new("Cheat Manager Enabler",
            "Enables Unreal's CheatManager so cheat commands work. Ships with UE4SS.", Docs),
        ["LineTraceMod"] = new("Line Trace",
            "Debug tool: line-traces to identify the object under your crosshair. Ships with UE4SS.", Docs),
        ["SplitScreenMod"] = new("Split Screen",
            "Enables local split-screen support. Ships with UE4SS.", Docs),
        ["Keybinds"] = new("Keybinds",
            "UE4SS's built-in keybind registration. Ships with UE4SS.", Docs),
        ["ActorDumperMod"] = new("Actor Dumper",
            "Debug tool: dumps actor data. Ships with UE4SS.", Docs),
        ["jsbLuaProfilerMod"] = new("Lua Profiler",
            "Profiles Lua mod performance. Ships with UE4SS.", Docs),
        ["shared"] = new("Shared (UE4SS internal)",
            "Shared Lua library imported by other UE4SS mods — not a standalone mod.", Docs),
    };

    public static bool IsBuiltin(string name) => !string.IsNullOrEmpty(name) && Catalog.ContainsKey(name);

    public static Ue4ssBuiltin? Lookup(string name)
        => !string.IsNullOrEmpty(name) && Catalog.TryGetValue(name, out var b) ? b : null;
}
