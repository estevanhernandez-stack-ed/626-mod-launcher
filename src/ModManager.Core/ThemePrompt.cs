namespace ModManager.Core;

/// <summary>
/// Builds the prompt a user hands to any LLM to author a theme. The model returns JSON on the
/// 15-color contract, which the launcher validates (<see cref="Themes.NormalizeTheme"/>) and
/// imports. Not agentic — the app just crafts the ask and validates the answer.
/// </summary>
public static class ThemePrompt
{
    public static string Build(string? vibe)
    {
        var v = string.IsNullOrWhiteSpace(vibe) ? "a striking, cohesive dark UI theme" : vibe.Trim();
        return
            "You are designing a color theme for a dark-mode desktop app (a game mod launcher).\n" +
            $"Theme vibe: {v}\n\n" +
            "Return ONLY a single JSON object — no prose, no markdown fences. Every color is a hex\n" +
            "string like \"#1a2b3c\". Include all of these fields:\n" +
            "  name (a short theme name),\n" +
            "  bg, glass, glass_on_mica, title_bg, border,\n" +
            "  text, text_secondary, text_dim, text_muted,\n" +
            "  accent, bar_bg, footer_bg, pace_marker, sparkline,\n" +
            "  success, warning, danger, info,\n" +
            "  tag_secondary, tag_client_only, tag_vortex, tag_folder\n" +
            "and an \"accent_bloom\" object: { \"blur\": <px number>, \"alpha\": <0..1 number> }.\n\n" +
            "Rules: dark backgrounds, high-contrast readable text, one cohesive accent. Valid JSON only.";
    }
}
