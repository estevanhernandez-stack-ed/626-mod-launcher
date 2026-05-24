namespace ModManager.Core;

/// <summary>
/// Pure URL guard: is this a plain http(s) URL safe to hand to the OS "open link"?
/// Blocks file:, javascript:, steam:, and anything malformed — so a metadata link
/// can't turn into arbitrary-scheme execution. Mirrors url-core.js.
/// </summary>
public static class SafeUrl
{
    public static bool IsHttpUrl(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return false;
        return u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps;
    }
}
