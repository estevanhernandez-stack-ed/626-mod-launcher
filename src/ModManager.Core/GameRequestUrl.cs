namespace ModManager.Core;

/// <summary>Builds a prefilled GitHub issue URL against the 626-game-manifest feed repo's
/// game-request.yml issue form. Pure + escaped — the App just opens the returned URL. The engine
/// dropdown is a REQUIRED form field whose options are fixed strings; a prefill value that isn't an
/// exact option is silently dropped, so we map our engine key to the option verbatim (default
/// "Not sure"). Keep <see cref="EngineOption"/> in sync with .github/ISSUE_TEMPLATE/game-request.yml
/// in the feed repo.</summary>
public static class GameRequestUrl
{
    private const string Base =
        "https://github.com/estevanhernandez-stack-ed/626-game-manifest/issues/new";

    // Exact dropdown option strings from game-request.yml (em-dash = U+2014). Keep in sync.
    private static string EngineOption(string? engineKey) => engineKey switch
    {
        "ue-pak"      => "ue-pak (Unreal .pak)",
        "bethesda"    => "bethesda (Creation Engine — esp/esl/bsa)",
        "bepinex"     => "bepinex (Unity — BepInEx)",
        "melonloader" => "melonloader (Unity — MelonLoader)",
        "smapi"       => "smapi (Stardew)",
        "source"      => "source (Source engine — vpk)",
        "fromsoft"    => "fromsoft (Souls / Mod Engine)",
        "minecraft"   => "minecraft (jar mods)",
        "custom"      => "custom / other",
        _             => "Not sure",
    };

    public static string Build(string name, string? steamAppId, string? engineKey, string? notes)
    {
        var q = new List<string>
        {
            "template=game-request.yml",
            "title=" + Esc("[game] " + name),
            "name=" + Esc(name),
            "engine=" + Esc(EngineOption(engineKey)),   // required field — always set
        };
        if (!string.IsNullOrWhiteSpace(steamAppId)) q.Add("steam-app-id=" + Esc(steamAppId));
        if (!string.IsNullOrWhiteSpace(notes)) q.Add("notes=" + Esc(notes));
        return Base + "?" + string.Join("&", q);
    }

    private static string Esc(string s) => Uri.EscapeDataString(s);
}
