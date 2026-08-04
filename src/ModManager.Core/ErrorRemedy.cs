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
        // Win32 error codes discriminate first — exception MESSAGES are localized to the OS
        // language, so English fragments only serve as a fallback for non-Win32 IOExceptions.
        IOException io when Win32(io) is 32 or 33 || Mentions(io, "being used by another process") =>
            "a file is in use — close the game (and any mod tools), then try again.",
        IOException io when Win32(io) is 112 or 39 || Mentions(io, "not enough space") =>
            "the disk is full — free some space, then try again.",
        UnauthorizedAccessException =>
            "Windows denied permission to a file. If the game folder needs elevation, run the launcher as administrator once, or check the folder isn't read-only.",
        FileNotFoundException or DirectoryNotFoundException =>
            "a file the launcher expected is missing — the game or another tool may have moved it. Hit Refresh to re-scan.",
        _ => $"{Sentence(e.Message)} If it keeps happening, try again after a Refresh.",
    };

    // ERROR_SHARING_VIOLATION 32 / ERROR_LOCK_VIOLATION 33 / ERROR_HANDLE_DISK_FULL 39 /
    // ERROR_DISK_FULL 112, carried as 0x8007xxxx on Win32-sourced exceptions.
    private static int Win32(Exception e)
        => (e.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000) ? e.HResult & 0xFFFF : 0;

    private static string Sentence(string s)
    {
        var t = s.TrimEnd();
        return t.Length > 0 && t[^1] is '.' or '!' or '?' ? t : t + ".";
    }

    private static bool Mentions(Exception e, string fragment)
        => e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
