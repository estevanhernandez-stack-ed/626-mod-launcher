namespace ModManager.Core;

/// <summary>
/// The two endorse-write directions. Maps to the path segment Nexus expects:
/// <c>endorse</c> or <c>abstain</c>. Abstain is the reversal of an endorse — there is no
/// destructive operation in either direction.
/// </summary>
public enum EndorseAction
{
    Endorse,
    Abstain,
}

/// <summary>
/// The result of an endorse/abstain write.
/// <list type="bullet">
/// <item><see cref="Status"/> — the Nexus status from a 2xx response (<c>Endorsed</c> /
/// <c>Abstained</c> / <c>Undecided</c>); null when refused.</item>
/// <item><see cref="Message"/> — on a refusal, the human-readable reason (a known code mapped to
/// friendly text, or the raw API message passed through).</item>
/// <item><see cref="Refused"/> — true when Nexus declined the write for a precondition reason
/// (not downloaded, too soon, etc.). A refusal is never a throw — it degrades to a status line.</item>
/// </list>
/// </summary>
public sealed record EndorseOutcome(string? Status, string? Message, bool Refused);

/// <summary>
/// Pure refusal-message mapping for the endorse write. Nexus's exact refusal-code set is not
/// public, so we map the two codes the client knows to friendlier text and pass anything else
/// through verbatim — the API's own human message is shown when we don't recognize the code.
/// </summary>
public static class NexusEndorse
{
    /// <summary>
    /// Map a Nexus refusal code/message to user-facing text. The two known precondition codes get
    /// friendly copy; any other value is returned unchanged so the API's own wording surfaces.
    /// </summary>
    public static string FriendlyRefusal(string code) => code switch
    {
        "NOT_DOWNLOADED_MOD" => "You need to download this mod before you can endorse it.",
        "TOO_SOON_AFTER_DOWNLOAD" => "You'll need to wait a little while after downloading before you can endorse this mod.",
        _ => code,
    };
}
