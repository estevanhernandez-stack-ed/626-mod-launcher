using System.Text.RegularExpressions;

namespace ManifestMiner;

/// <summary>Extracts mined facts from a MO2 basic_games game_*.py via targeted regex. Not a Python
/// parser — it reads the common `Attr = value` class-attribute forms. Files that don't yield a Steam
/// id return null (can't key them onto the backbone).</summary>
public static partial class Mo2GameParser
{
    [GeneratedRegex(@"GameName\s*=\s*""([^""]+)""")] private static partial Regex NameRe();
    [GeneratedRegex(@"GameNexusName\s*=\s*""([^""]+)""")] private static partial Regex NexusRe();
    [GeneratedRegex(@"GameDataPath\s*=\s*(?:r)?""([^""]*)""")] private static partial Regex DataPathRe();
    // GameSteamId may be a single int or a list: GameSteamId = 892970  |  GameSteamId = [892970, 896660]
    [GeneratedRegex(@"GameSteamId\s*=\s*(\[[^\]]*\]|\d+)")] private static partial Regex SteamRe();
    [GeneratedRegex(@"\d+")] private static partial Regex DigitsRe();

    public static Mo2Game? Parse(string pythonText)
    {
        var nameM = NameRe().Match(pythonText);
        if (!nameM.Success) return null;

        var steamM = SteamRe().Match(pythonText);
        if (!steamM.Success) return null;
        var steamIds = DigitsRe().Matches(steamM.Groups[1].Value).Select(m => m.Value).ToList();
        if (steamIds.Count == 0) return null;

        var dataM = DataPathRe().Match(pythonText);
        var nexusM = NexusRe().Match(pythonText);

        return new Mo2Game(nameM.Groups[1].Value)
        {
            SteamIds = steamIds,
            DataPath = dataM.Success ? dataM.Groups[1].Value : null,
            NexusName = nexusM.Success ? nexusM.Groups[1].Value : null,
        };
    }
}
