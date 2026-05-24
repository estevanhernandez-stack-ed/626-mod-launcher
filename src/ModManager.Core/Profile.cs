using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Pure profile-name validation. A profile name becomes a filename (<c>&lt;name&gt;.json</c>)
/// inside the profiles directory, so it must not escape that directory or trip Windows
/// filename pitfalls. Reject (with a clear message) rather than silently sanitize, so two
/// labels can't collapse onto the same file. Mirrors profile-core.js.
/// </summary>
public static partial class Profile
{
    [GeneratedRegex(@"^(con|prn|aux|nul|com[1-9]|lpt[1-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsReservedRe();

    [GeneratedRegex(@"[\\/]")]
    private static partial Regex SlashRe();

    [GeneratedRegex("[<>:\"|?*\\u0000-\\u001f]")]
    private static partial Regex InvalidCharRe();

    [GeneratedRegex(@"[. ]$")]
    private static partial Regex TrailingDotSpaceRe();

    public static string SafeProfileName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) throw new ArgumentException("Profile name is required.");
        if (trimmed.Length > 100) throw new ArgumentException("Profile name is too long (max 100 characters).");
        if (trimmed is "." or "..") throw new ArgumentException("Invalid profile name.");
        if (SlashRe().IsMatch(trimmed)) throw new ArgumentException("Profile name can't contain slashes.");
        if (InvalidCharRe().IsMatch(trimmed)) throw new ArgumentException("Profile name contains invalid characters.");
        if (TrailingDotSpaceRe().IsMatch(trimmed)) throw new ArgumentException("Profile name can't end with a space or a dot.");
        if (WindowsReservedRe().IsMatch(trimmed)) throw new ArgumentException("That name is reserved by Windows — pick another.");
        return trimmed;
    }
}
