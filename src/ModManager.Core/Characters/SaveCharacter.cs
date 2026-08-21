namespace ModManager.Core.Characters;

/// <summary>
/// One character inside a game's saves, in the terms a panel can show.
///
/// <para><b>Listing is not editing, and conflating them cost this surface its usefulness.</b> The
/// characters section was built around the FromSoft save editor, so a game we can READ but would never
/// WRITE fell through to <i>"this game's save format isn't itemized yet"</i> — which is false for
/// Cyberpunk 2077, whose every save sits beside sixty fields of plain JSON and a screenshot.</para>
///
/// <para>So a character is something we can describe. Whether we can change it is a separate fact,
/// carried here as <see cref="Editable"/>, and true for far fewer games.</para>
/// </summary>
/// <param name="Id">Stable handle within the game — a slot index, or the save's folder name.</param>
/// <param name="Name">What the player calls them.</param>
/// <param name="Kind">The one-word identity: a class, an origin, a life path. Empty when unknown.</param>
/// <param name="Progress">Whatever this game measures progress in, already formatted.</param>
/// <param name="LastPlayed">Local time, or null when the save does not record one.</param>
/// <param name="ThumbnailPath">A screenshot the game saved beside the character, when one exists.</param>
/// <param name="MadeWithMods">Whether the game itself recorded that mods were loaded. Null when the
/// game does not say — which is not the same as "no", and must never be shown as one.</param>
/// <param name="Editable">Whether a writer exists for this format. Reading is the common case.</param>
public sealed record SaveCharacter(
    string Id,
    string Name,
    string Kind,
    string Progress,
    DateTime? LastPlayed,
    string? ThumbnailPath = null,
    bool? MadeWithMods = null,
    bool Editable = false)
{
    /// <summary>The identity line: who they are, and what kind. Never blank.</summary>
    public string Headline => string.IsNullOrWhiteSpace(Kind) ? Name : $"{Name}  ·  {Kind}";

    /// <summary>Progress and when, joined only where both exist.</summary>
    public string Detail
    {
        get
        {
            var when = LastPlayed is { } t ? t.ToString("yyyy-MM-dd HH:mm") : "";
            if (Progress.Length == 0) return when;
            return when.Length == 0 ? Progress : $"{Progress}  ·  {when}";
        }
    }
}
