namespace ModManager.Core.Transport;

/// <summary>
/// Finds authentication tokens sitting among a game's save files.
///
/// <para><b>Why.</b> Cyberpunk 2077's save root contains <c>user.gls</c>, which holds a CD Projekt Red
/// account refresh token — issuer <c>accounts.cdprojektred.com</c>, scopes including
/// <c>aud_game_saves</c>, expiring in 2035. A bundle meant to be moved between machines or handed to
/// someone for support cannot carry that. This is a class of problem, not one file: any game with
/// account integration can drop a token beside its saves.</para>
///
/// <para><b>Deliberately narrow.</b> It looks for a JWT and nothing else. A keyword sweep for
/// "password" across the same save folders hit Palworld's <c>Level.sav</c> and Windrose's RocksDB —
/// both the GAME'S OWN DATA (a world's server password, an account record), which is save state the
/// user would be furious to lose. Excluding those would be a bug wearing a security badge.</para>
///
/// <para>Size-capped because the point is a token, and tokens are small. A 32 MB world file is not
/// scanned, which also keeps bundling a large save from turning into a full content scan.</para>
/// </summary>
public static class CredentialScan
{
    /// <summary>Files larger than this are not scanned. Tokens are small; save data is not.</summary>
    public const long MaxScanBytes = 1_000_000;

    /// <summary>The reason string recorded in a bundle's manifest for an excluded file.</summary>
    public const string Reason = "credential";

    /// <summary>
    /// Whether these bytes contain something JWT-shaped: three base64url segments, the first two
    /// starting <c>eyJ</c> — the encoding of <c>{"</c>, which is how every JSON-header token begins.
    /// </summary>
    public static bool LooksLikeCredential(ReadOnlySpan<byte> content)
    {
        // "eyJ" appears at the start of the header AND the payload, so requiring two of them with a
        // dot between is what separates a real token from the letters happening to occur in a blob.
        var first = IndexOfEyJ(content, 0);
        if (first < 0) return false;

        var dot = IndexOf(content, (byte)'.', first);
        if (dot < 0) return false;

        var second = IndexOfEyJ(content, dot);
        if (second != dot + 1) return false;                 // payload must follow the dot immediately

        var secondDot = IndexOf(content, (byte)'.', second);
        return secondDot > 0 && secondDot < content.Length - 1;   // and a signature after it
    }

    /// <summary>Whether a file on disk should be kept out of an outbound bundle. Never throws — an
    /// unreadable file is not excluded, because we cannot claim it holds a secret.</summary>
    public static bool IsCredentialFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MaxScanBytes) return false;
            return LooksLikeCredential(File.ReadAllBytes(path));
        }
        catch { return false; }
    }

    private static int IndexOfEyJ(ReadOnlySpan<byte> s, int from)
    {
        for (var i = Math.Max(from, 0); i + 2 < s.Length; i++)
            if (s[i] == (byte)'e' && s[i + 1] == (byte)'y' && s[i + 2] == (byte)'J') return i;
        return -1;
    }

    private static int IndexOf(ReadOnlySpan<byte> s, byte b, int from)
    {
        for (var i = Math.Max(from, 0); i < s.Length; i++) if (s[i] == b) return i;
        return -1;
    }
}
