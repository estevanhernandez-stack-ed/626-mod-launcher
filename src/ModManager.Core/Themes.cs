using System.Text.Json;

namespace ModManager.Core;

/// <summary>Accent-glow descriptor: blur radius (px) + alpha (0 = no glow).</summary>
public sealed record AccentBloom(double Blur, double Alpha);

/// <summary>An unnormalized theme as it lives in the builtins / user JSON (token bag + bloom).</summary>
public sealed class RawTheme
{
    public Dictionary<string, string> Tokens { get; init; } = new();
    public AccentBloom? AccentBloom { get; init; }
}

/// <summary>A validated theme: required fields present, optional defaults merged, id attached.</summary>
public sealed class Theme
{
    // Defaulted (not 'required') so the WinUI XAML type-info generator can construct the type
    // for binding; NormalizeTheme always sets all three.
    public string Id { get; init; } = "";
    public IReadOnlyDictionary<string, string> Tokens { get; init; } = new Dictionary<string, string>();
    public AccentBloom AccentBloom { get; init; } = new(4, 0);
    public string Name => Tokens.TryGetValue("name", out var n) ? n : "";
    public string this[string key] => Tokens[key];
}

/// <summary>
/// Theme engine for the mod launcher — pure data + helpers. 15 required color fields
/// (Sanduhr contract) + optional status/tag tokens + accent bloom. Mirrors themes.js.
/// </summary>
public static class Themes
{
    public static IReadOnlyList<string> RequiredFields { get; } = new[]
    {
        "name", "bg", "glass", "glass_on_mica", "title_bg", "border",
        "text", "text_secondary", "text_dim", "text_muted",
        "accent", "bar_bg", "footer_bg", "pace_marker", "sparkline",
    };

    private static readonly IReadOnlyDictionary<string, string> OptionalDefaults = new Dictionary<string, string>
    {
        ["success"] = "#3a8b4a",
        ["warning"] = "#d9a441",
        ["danger"] = "#c0503a",
        ["info"] = "#58a6d8",
    };

    private static readonly AccentBloom DefaultBloom = new(4, 0.0);

    private static RawTheme T(
        string name, string bg, string glass, string glassOnMica, string titleBg, string border,
        string text, string textSecondary, string textDim, string textMuted,
        string accent, string barBg, string footerBg, string paceMarker, string sparkline,
        string success, string warning, string danger, string info,
        string tagSecondary, string tagClientOnly, string tagVortex, string tagFolder,
        double bloomBlur, double bloomAlpha) => new()
        {
            Tokens = new Dictionary<string, string>
            {
                ["name"] = name, ["bg"] = bg, ["glass"] = glass, ["glass_on_mica"] = glassOnMica,
                ["title_bg"] = titleBg, ["border"] = border,
                ["text"] = text, ["text_secondary"] = textSecondary, ["text_dim"] = textDim, ["text_muted"] = textMuted,
                ["accent"] = accent, ["bar_bg"] = barBg, ["footer_bg"] = footerBg, ["pace_marker"] = paceMarker, ["sparkline"] = sparkline,
                ["success"] = success, ["warning"] = warning, ["danger"] = danger, ["info"] = info,
                ["tag_secondary"] = tagSecondary, ["tag_client_only"] = tagClientOnly, ["tag_vortex"] = tagVortex, ["tag_folder"] = tagFolder,
            },
            AccentBloom = new AccentBloom(bloomBlur, bloomAlpha),
        };

    public static IReadOnlyDictionary<string, RawTheme> BuiltinThemes { get; } = new Dictionary<string, RawTheme>
    {
        ["obsidian"] = T("Obsidian", "#0d0d0d", "#1c1c1c", "#1a1a1c", "#161616", "#333333",
            "#e8e4dc", "#b8b4ac", "#777777", "#555555",
            "#6c63ff", "#2a2a2a", "#111111", "#ff6b6b", "#6c63ff",
            "#5fb87a", "#e0b252", "#e06b6b", "#7c8cff",
            "#e8c068", "#80c0e0", "#e09080", "#a0e0a0", 4, 0.35),
        ["aurora"] = T("Aurora", "#0a0f1a", "#161e30", "#141d2e", "#0f172a", "#334155",
            "#e2e8f0", "#94a3b8", "#64748b", "#475569",
            "#38bdf8", "#1e293b", "#0c1220", "#f472b6", "#38bdf8",
            "#34d399", "#fbbf24", "#fb7185", "#38bdf8",
            "#38bdf8", "#7dd3fc", "#fb7185", "#34d399", 6, 0.55),
        ["ember"] = T("Ember", "#1a0a0a", "#261414", "#211010", "#1f0e0e", "#442222",
            "#f5e6e0", "#d4a89c", "#8b6b60", "#6b4b40",
            "#f97316", "#2d1a1a", "#150808", "#fbbf24", "#f97316",
            "#84cc16", "#fbbf24", "#ef4444", "#fb923c",
            "#fbbf24", "#fdba74", "#ef4444", "#84cc16", 6, 0.55),
        ["mint"] = T("Mint", "#0a1a14", "#122a1e", "#0e2419", "#0c1f14", "#22543d",
            "#e0f5ec", "#9cd4b8", "#5a9a78", "#3a7a58",
            "#34d399", "#163020", "#081510", "#f472b6", "#34d399",
            "#34d399", "#fbbf24", "#f472b6", "#5eead4",
            "#34d399", "#5eead4", "#f472b6", "#34d399", 4, 0.35),
        ["matrix"] = T("Matrix", "#020a02", "#0a140a", "#0a140a", "#040d04", "#0f2a0f",
            "#00ff41", "#00cc33", "#00802b", "#005a1e",
            "#00ff41", "#0a1a0a", "#020802", "#ff0040", "#00ff41",
            "#00ff41", "#ccff00", "#ff0040", "#00cc33",
            "#00cc33", "#00cc33", "#ff0040", "#00ff41", 4, 0.60),
        ["blueprint"] = T("Blueprint", "#1a1625", "#2a2438", "#241f30", "#15121e", "#3a324d",
            "#e0e2f5", "#a2a6cc", "#6a6e99", "#484b70",
            "#4dffc4", "#1f1a2e", "#100d16", "#ff4d88", "#4dffc4",
            "#4dffc4", "#ffd24d", "#ff4d88", "#4da6ff",
            "#4dffc4", "#4da6ff", "#ff4d88", "#4dffc4", 8, 0.65),
        ["626-labs"] = T("626 Labs", "#0f182b", "#1a2540", "#131d31", "#131a2a", "#2a3a5c",
            "#e8f2ff", "#a8c2d9", "#6a849e", "#4a5f7a",
            "#3bb4d9", "#1a2540", "#0a121f", "#e13aa0", "#3bb4d9",
            "#3bd98f", "#d9a441", "#e13a5a", "#58c7e6",
            "#3bb4d9", "#58c7e6", "#e13a5a", "#3bd98f", 6, 0.60),
    };

    private static string HexToRgba(string hex, double alpha)
    {
        var h = hex.Replace("#", "");
        var n = h.Length == 3 ? string.Concat(h.Select(c => $"{c}{c}")) : h;
        var r = Convert.ToInt32(n.Substring(0, 2), 16);
        var g = Convert.ToInt32(n.Substring(2, 2), 16);
        var b = Convert.ToInt32(n.Substring(4, 2), 16);
        return $"rgba({r}, {g}, {b}, {alpha})";
    }

    /// <summary>Parse a theme JSON object (string color tokens + optional accent_bloom) into a RawTheme.</summary>
    public static RawTheme ParseRawTheme(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var tokens = new Dictionary<string, string>();
        AccentBloom? bloom = null;
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("accent_bloom") && prop.Value.ValueKind == JsonValueKind.Object)
                {
                    var blur = prop.Value.TryGetProperty("blur", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetDouble() : 4;
                    var alpha = prop.Value.TryGetProperty("alpha", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDouble() : 0;
                    bloom = new AccentBloom(blur, alpha);
                }
                else if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    tokens[prop.Name] = prop.Value.GetString()!;
                }
            }
        }
        return new RawTheme { Tokens = tokens, AccentBloom = bloom };
    }

    /// <summary>Validate required fields, merge optional defaults, attach id, default tag tokens. Null if invalid.</summary>
    public static Theme? NormalizeTheme(string id, RawTheme? data)
    {
        if (data is null) return null;
        foreach (var f in RequiredFields)
        {
            if (!data.Tokens.TryGetValue(f, out var v) || v is null) return null;
        }

        var tokens = new Dictionary<string, string>(OptionalDefaults);
        foreach (var kv in data.Tokens) tokens[kv.Key] = kv.Value;

        string Fallback(string key, string fallbackToken) =>
            data.Tokens.TryGetValue(key, out var raw) && raw is not null ? raw : tokens[fallbackToken];

        tokens["tag_secondary"] = Fallback("tag_secondary", "accent");
        tokens["tag_client_only"] = Fallback("tag_client_only", "info");
        tokens["tag_vortex"] = Fallback("tag_vortex", "danger");
        tokens["tag_folder"] = Fallback("tag_folder", "success");

        return new Theme { Id = id, Tokens = tokens, AccentBloom = data.AccentBloom ?? DefaultBloom };
    }

    /// <summary>Map a normalized theme to the CSS custom properties the renderer consumes.</summary>
    public static IReadOnlyDictionary<string, string> ThemeToCssVars(Theme t)
    {
        var bloom = t.AccentBloom.Alpha > 0
            ? $"0 0 {t.AccentBloom.Blur}px {HexToRgba(t["accent"], t.AccentBloom.Alpha)}"
            : "none";
        return new Dictionary<string, string>
        {
            ["--bg"] = t["bg"], ["--panel"] = t["glass"], ["--panel-2"] = t["glass_on_mica"],
            ["--title-bg"] = t["title_bg"], ["--border"] = t["border"], ["--footer-bg"] = t["footer_bg"], ["--bar-bg"] = t["bar_bg"],
            ["--ink"] = t["text"], ["--ink-soft"] = t["text_secondary"], ["--ink-dim"] = t["text_dim"], ["--ink-muted"] = t["text_muted"],
            ["--accent"] = t["accent"], ["--hot"] = t["pace_marker"], ["--spark"] = t["sparkline"],
            ["--success"] = t["success"], ["--warning"] = t["warning"], ["--danger"] = t["danger"], ["--info"] = t["info"],
            ["--tag-secondary"] = t["tag_secondary"], ["--tag-client"] = t["tag_client_only"],
            ["--tag-vortex"] = t["tag_vortex"], ["--tag-folder"] = t["tag_folder"],
            ["--accent-bloom"] = bloom,
        };
    }

    /// <summary>Built-ins merged with user/agent themes; user wins on id collision.</summary>
    public static IReadOnlyList<Theme> BuildThemeList(
        IReadOnlyDictionary<string, RawTheme> builtins,
        IEnumerable<(string Id, RawTheme Data)> userThemes)
    {
        var byId = new Dictionary<string, Theme>();
        foreach (var (id, raw) in builtins.Select(kv => (kv.Key, kv.Value)))
        {
            var t = NormalizeTheme(id, raw);
            if (t is not null) byId[id] = t;
        }
        foreach (var (id, data) in userThemes)
        {
            var t = NormalizeTheme(id, data);
            if (t is not null) byId[id] = t; // later wins
        }
        return byId.Values.ToList();
    }
}
