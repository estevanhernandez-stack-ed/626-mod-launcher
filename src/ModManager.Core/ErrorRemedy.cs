namespace ModManager.Core;

/// <summary>
/// Maps exceptions to user-facing copy that says what happened AND what to do next
/// (vibe-glow F-024). The App routes StatusText failure paths through this instead of
/// surfacing raw .NET exception messages. Pure — no I/O, no UI.
/// </summary>
public static class ErrorRemedy
{
    /// <summary>Describe a failure with an optional leading action context
    /// (e.g. "Couldn't toggle Faster Ships").</summary>
    public static string Describe(Exception e, string? action = null)
    {
        var body = Body(e);
        return string.IsNullOrWhiteSpace(action) ? body : $"{action} — {body}";
    }

    private static string Body(Exception e) => e switch
    {
        IOException io when Mentions(io, "being used by another process") =>
            "a file is in use — close the game (and any mod tools), then try again.",
        IOException io when Mentions(io, "not enough space") =>
            "the disk is full — free some space, then try again.",
        UnauthorizedAccessException =>
            "Windows denied permission to a file. If the game folder needs elevation, run the launcher as administrator once, or check the folder isn't read-only.",
        FileNotFoundException or DirectoryNotFoundException =>
            "a file the launcher expected is missing — the game or another tool may have moved it. Hit Refresh to re-scan.",
        _ => $"{e.Message.TrimEnd()} If it keeps happening, try again after a Refresh.",
    };

    private static bool Mentions(Exception e, string fragment)
        => e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
