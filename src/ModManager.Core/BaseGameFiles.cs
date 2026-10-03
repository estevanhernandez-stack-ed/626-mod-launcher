using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// The one rule for "is this FILE the game's own". A row with any such file is marked
/// <see cref="Mod.IsBase"/> by the scan, and nothing 626 does turns it off: bulk operations skip it,
/// an explicit toggle or uninstall refuses in words (<see cref="BaseGameFileException"/>). Turning one
/// back ON is always allowed, so a base file an older build moved to holding can come home.
///
/// <para><b>Bethesda (Creation Engine).</b> The game lists its own masters, Creation Club content and
/// archives in <c>Data</c> beside the mods, under the same extensions. Three signals, all by name:
/// the base masters every Creation Engine game ships (<see cref="IsBethesdaBaseMaster"/>), every file
/// the game's own Creation Club list in the game root names (<c>Skyrim.ccc</c>, <c>Fallout4.ccc</c>,
/// <c>Starfield.ccc</c>), and the <c>.bsa</c>/<c>.ba2</c> archives that belong to either
/// (<c>Skyrim - Textures0.bsa</c>, <c>ccBGSSSE001-Fish.bsa</c>).</para>
///
/// <para><b>Unreal.</b> A location that IS the game's <c>Content/Paks</c> (the paks-root form, or a
/// files-form location pointed at it) mixes base paks with mods. <see cref="IsBaseGameArchive"/> is the
/// one predicate, shared with Safe Clear. It is deliberately narrow: a broad <c>*-Windows*.pak</c> rule
/// would swallow real mods such as <c>BetterHUD-WindowsNoEditor.pak</c> (Safe Clear round 6).</para>
/// </summary>
public static class BaseGameFiles
{
    // Base masters across every Creation Engine game. Case-insensitive.
    private static readonly HashSet<string> BethesdaMasters = new(StringComparer.OrdinalIgnoreCase)
    {
        // Skyrim (LE / SE / AE)
        "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm",
        // Fallout 4
        "Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm", "DLCworkshop02.esm",
        "DLCworkshop03.esm", "DLCNukaWorld.esm", "DLCUltraHighResolution.esm",
        // Starfield (SFBGS*.esm is matched by pattern below)
        "Starfield.esm", "Constellation.esm", "OldMars.esm", "BlueprintShips-Starfield.esm", "ShatteredSpace.esm",
        // Oblivion, Fallout 3, Fallout: New Vegas
        "Oblivion.esm", "Fallout3.esm", "FalloutNV.esm",
        // Fallout 3 DLC
        "Anchorage.esm", "ThePitt.esm", "BrokenSteel.esm", "PointLookout.esm", "Zeta.esm",
        // Fallout: New Vegas DLC and the pre-order packs
        "DeadMoney.esm", "HonestHearts.esm", "OldWorldBlues.esm", "LonesomeRoad.esm", "GunRunnersArsenal.esm",
        "ClassicPack.esm", "MercenaryPack.esm", "TribalPack.esm", "CaravanPack.esm",
    };

    // Starfield's own update masters (SFBGS003.esm, SFBGS004.esm, ...).
    private static readonly Regex StarfieldUpdateMaster = new(@"^SFBGS[^.\\/]*\.esm$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Fallout 3 and New Vegas name their base archives "Fallout - Meshes.bsa", after no master at all.
    private static readonly string[] ArchiveOnlyStems = { "Fallout" };

    /// <summary>The Creation Club lists a Bethesda game keeps in its root: one filename per line.</summary>
    public static readonly IReadOnlyList<string> CreationClubLists = new[] { "Skyrim.ccc", "Fallout4.ccc", "Starfield.ccc" };

    private static readonly string[] PluginExts = { ".esm", ".esp", ".esl" };
    private static readonly string[] ArchiveExts = { ".bsa", ".ba2" };

    /// <summary>True for a base master every copy of a Creation Engine game ships. Name only.</summary>
    public static bool IsBethesdaBaseMaster(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var name = Path.GetFileName(fileName);
        return BethesdaMasters.Contains(name) || StarfieldUpdateMaster.IsMatch(name);
    }

    /// <summary>
    /// Every filename the game's own Creation Club lists in <paramref name="gameRoot"/> name. Whichever
    /// lists exist are read; a missing or unreadable one counts as empty, so the static masters still apply.
    /// </summary>
    public static IReadOnlySet<string> ReadCreationClub(string? gameRoot)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(gameRoot)) return set;
        foreach (var list in CreationClubLists)
        {
            try
            {
                var path = Path.Combine(gameRoot, list);
                if (!File.Exists(path)) continue;
                foreach (var line in File.ReadAllLines(path))
                {
                    var name = line.Trim();
                    if (name.Length > 0 && name.IndexOfAny(new[] { '/', '\\' }) < 0) set.Add(name);
                }
            }
            catch { /* unreadable: no entries from this list */ }
        }
        return set;
    }

    /// <summary>
    /// True when a file in a Bethesda <c>Data</c> folder is the game's own: a base master, a file the
    /// Creation Club list names, or a <c>.bsa</c>/<c>.ba2</c> whose stem is a base plugin's stem or starts
    /// with <c>"&lt;base stem&gt; - "</c>.
    /// </summary>
    public static bool IsBethesdaBaseFile(string? fileName, IReadOnlySet<string> creationClub)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var name = Path.GetFileName(fileName);
        var ext = Path.GetExtension(name);
        if (PluginExts.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return IsBethesdaBaseMaster(name) || creationClub.Contains(name);
        if (!ArchiveExts.Contains(ext, StringComparer.OrdinalIgnoreCase)) return false;
        if (creationClub.Contains(name)) return true;

        var stem = Path.GetFileNameWithoutExtension(name);
        var dash = stem.IndexOf(" - ", StringComparison.Ordinal);
        var owner = dash > 0 ? stem[..dash] : stem;
        if (dash > 0 && ArchiveOnlyStems.Contains(owner, StringComparer.OrdinalIgnoreCase)) return true;
        foreach (var pe in PluginExts)
            if (IsBethesdaBaseMaster(owner + pe) || creationClub.Contains(owner + pe)) return true;
        return false;
    }

    /// <summary>
    /// A pak/ucas/utoc that is the base game's own: <see cref="PakClassifier.IsBaseGamePak"/> on its pak
    /// name, or a UE5 <c>global.ucas</c> / <c>global.utoc</c>. The one implementation, shared by the scan,
    /// the toggle guard and Safe Clear.
    /// </summary>
    public static bool IsBaseGameArchive(string? fileName, long size)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var name = Path.GetFileName(fileName);
        var ext = Path.GetExtension(name);
        if (!(ext.Equals(".pak", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ucas", StringComparison.OrdinalIgnoreCase)
              || ext.Equals(".utoc", StringComparison.OrdinalIgnoreCase)))
            return false;
        if (Path.GetFileNameWithoutExtension(name).Equals("global", StringComparison.OrdinalIgnoreCase)) return true;
        return PakClassifier.IsBaseGamePak(Path.ChangeExtension(name, ".pak"), size);
    }

    /// <summary>True when a location is the game's own <c>Content/Paks</c> folder, where base paks and mods
    /// share one directory: the paks-root form, or a files-form location whose folder is <c>Content/Paks</c>.
    /// A dedicated mod folder (<c>~mods</c>, <c>LogicMods</c>) is not.</summary>
    public static bool IsSharedPaksFolder(ModLocationCtx loc)
    {
        if (loc.Form == "paks-root") return true;
        if (loc.Form != "files") return false;
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(loc.Abs));
            var paks = Path.GetFileName(full);
            var content = Path.GetFileName(Path.GetDirectoryName(full) ?? "");
            return paks.Equals("Paks", StringComparison.OrdinalIgnoreCase)
                   && content.Equals("Content", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>The refusal an explicit turn-off gets.</summary>
    public static string TurnOffRefusal(string file) => $"{file} is part of the game, so 626 won't turn it off.";

    /// <summary>The refusal an uninstall gets.</summary>
    public static string RemoveRefusal(string file) => $"{file} is part of the game, so 626 won't remove it.";

    /// <summary>
    /// The per-scan judge for one game: reads the game's Creation Club lists at most once, and answers
    /// per file. Create one per listing or per guarded operation.
    /// </summary>
    public sealed class Judge
    {
        private readonly string? _gameRoot;
        private readonly bool _bethesda;
        private IReadOnlySet<string>? _creationClub;

        public Judge(GameContext c) : this(c.Game.Engine, c.GameRoot) { }

        /// <param name="engine">The game's engine id; only <c>bethesda</c> turns on the Creation Engine rules.
        /// Null judges Unreal paks folders alone.</param>
        /// <param name="gameRoot">Where the game's Creation Club lists live.</param>
        public Judge(string? engine, string? gameRoot)
        {
            _gameRoot = gameRoot;
            _bethesda = string.Equals(engine, "bethesda", StringComparison.OrdinalIgnoreCase);
        }

        private IReadOnlySet<string> CreationClub => _creationClub ??= ReadCreationClub(_gameRoot);

        /// <summary>True when this location can hold the game's own files beside mods at all. A false
        /// answer means no file there is ever base, so a caller can skip sizing.</summary>
        public bool Applies(ModLocationCtx loc) => _bethesda || IsSharedPaksFolder(loc);

        /// <summary>True when the location needs a file's size to judge it (an Unreal paks folder).</summary>
        public static bool NeedsSize(ModLocationCtx loc) => IsSharedPaksFolder(loc);

        /// <summary>Is <paramref name="file"/> (relative to <paramref name="loc"/>) the game's own?
        /// <paramref name="size"/> is only read for an Unreal paks folder.</summary>
        public bool IsBase(ModLocationCtx loc, string file, long size)
        {
            var name = Path.GetFileName(file);
            if (_bethesda && IsBethesdaBaseFile(name, CreationClub)) return true;
            if (IsSharedPaksFolder(loc))
            {
                if (IsBaseGameArchive(name, size)) return true;
                // The paks-root guard has always refused any file past the size ceiling, whatever its extension.
                if (loc.Form == "paks-root" && PakClassifier.IsBaseGamePak(name, size)) return true;
            }
            return false;
        }
    }
}

/// <summary>
/// An explicit request to turn off or remove a file that is part of the game. The message is the
/// user-facing sentence, so callers show it as it is.
/// </summary>
public sealed class BaseGameFileException : InvalidOperationException
{
    public BaseGameFileException(string file, bool remove)
        : base(remove ? BaseGameFiles.RemoveRefusal(file) : BaseGameFiles.TurnOffRefusal(file))
    {
        File = file;
    }

    /// <summary>The game's file the request reached.</summary>
    public string File { get; }
}
