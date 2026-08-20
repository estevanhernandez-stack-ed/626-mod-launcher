using System.Globalization;

namespace ModManager.Core;

/// <summary>One file Steam Cloud is tracking for a game.</summary>
/// <param name="Path">Relative, forward-slashed, as Steam stores it:
/// <c>Pal/Saved/SaveGames/&lt;steamid&gt;/&lt;world&gt;/Level.sav</c>.</param>
/// <param name="Bytes">Size Steam has recorded. Sums to the game's cloud quota usage.</param>
/// <param name="Sha1">Steam's hash of the synced copy.</param>
/// <param name="SyncState">Raw, undecoded. <b>Every entry observed so far reads 1</b>, so this type
/// deliberately does not claim to know what other values mean — see the class remarks.</param>
public sealed record SteamCloudFile(string Path, long Bytes, string Sha1, int SyncState);

/// <summary>
/// What Steam Cloud is holding for a game, read from its own cache.
///
/// <para><b>Why this exists.</b> The launcher had no cloud awareness at all, and it cost us twice in
/// one week on the same game. A Palworld world deleted from disk came back the next morning because
/// Steam still had it. Later, the only way to know an in-game delete had genuinely worked was to read
/// this file by hand. Both are questions the app should be able to answer for any Steam game without
/// knowing anything about the game.</para>
///
/// <para><c>remotecache.vdf</c> lives at
/// <c>&lt;steam&gt;/userdata/&lt;userid&gt;/&lt;appid&gt;/remotecache.vdf</c> and is plain text VDF: a
/// block per file with <c>size</c>, <c>sha</c>, timestamps and two state fields. It answers three
/// things nothing else can — <i>is this tracked</i> (so will a delete stick), <i>what would bring it
/// back</i>, and <i>what does a copy cost in quota</i>.</para>
///
/// <para><b>What it deliberately does not answer.</b> Every entry on the machine this was written
/// against reads <c>syncstate 1</c>, so there is no evidence for what any other value means. The raw
/// number is exposed and nothing here interprets it. A UI may say "Steam Cloud is tracking this"; it
/// may not say "this is fully uploaded", because we do not know that.</para>
///
/// <para>Pure: parsing takes a string. Locating the file takes a Steam root the App resolves.</para>
/// </summary>
public static class SteamCloudCache
{
    public const string FileName = "remotecache.vdf";

    /// <summary>Where Steam keeps a game's cloud cache for one signed-in account.</summary>
    public static string PathFor(string steamRoot, string steamUserId, string appId)
        => System.IO.Path.Combine(steamRoot, "userdata", steamUserId, appId, FileName);

    /// <summary>Read and parse, or an empty list. Never throws — an unreadable cache means we simply
    /// do not know, and every caller here degrades to the behaviour it had before cloud awareness.</summary>
    public static IReadOnlyList<SteamCloudFile> Read(string cachePath)
    {
        try
        {
            return File.Exists(cachePath) ? Parse(File.ReadAllText(cachePath)) : Array.Empty<SteamCloudFile>();
        }
        catch { return Array.Empty<SteamCloudFile>(); }
    }

    /// <summary>
    /// Parse the VDF. Malformed input yields whatever was well-formed, never an exception: a
    /// half-written cache should degrade, not take a save panel down with it.
    ///
    /// <para>Tokenised rather than pattern-matched, because the format is nested and a regex is not.
    /// The first attempt matched <c>"&lt;appid&gt;"</c> followed by a brace — the ROOT block — as a
    /// file entry, swallowed the real entries inside it, and reported one file called <c>1623730</c>
    /// with the first file's size. It looked plausible and was completely wrong.</para>
    /// </summary>
    public static IReadOnlyList<SteamCloudFile> Parse(string? vdf)
    {
        if (string.IsNullOrWhiteSpace(vdf)) return Array.Empty<SteamCloudFile>();

        var tokens = Tokenize(vdf!);
        var files = new List<SteamCloudFile>();
        var i = 0;

        // Skip the root: "<appid>" {  — everything real is one level in.
        if (i + 1 < tokens.Count && tokens[i].Kind == TokenKind.Text && tokens[i + 1].Kind == TokenKind.Open)
            i += 2;

        while (i < tokens.Count)
        {
            var t = tokens[i];
            if (t.Kind != TokenKind.Text) { i++; continue; }

            // "key" "value"  -> a scalar (ChangeNumber, OSType). Not a file.
            if (i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Text) { i += 2; continue; }

            // "path" { ... }  -> a candidate file record.
            if (i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Open)
            {
                var path = t.Value;
                i += 2;
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var depth = 1;
                while (i < tokens.Count && depth > 0)
                {
                    var b = tokens[i];
                    if (b.Kind == TokenKind.Open) { depth++; i++; }
                    else if (b.Kind == TokenKind.Close) { depth--; i++; }
                    else if (depth == 1 && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Text)
                    {
                        fields[b.Value] = tokens[i + 1].Value;
                        i += 2;
                    }
                    else i++;
                }

                // A block with no size is not a file record - it is some other nested structure, and a
                // zero-byte phantom would read as "tracked" when nothing is.
                if (fields.TryGetValue("size", out var sizeText))
                {
                    files.Add(new SteamCloudFile(
                        path.Replace('\\', '/'),
                        long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var by) ? by : 0,
                        fields.TryGetValue("sha", out var sha) ? sha : "",
                        fields.TryGetValue("syncstate", out var ss)
                            && int.TryParse(ss, NumberStyles.Integer, CultureInfo.InvariantCulture, out var st) ? st : 0));
                }
                continue;
            }
            i++;
        }
        return files;
    }

    private enum TokenKind { Text, Open, Close }
    private readonly record struct Token(TokenKind Kind, string Value);

    private static List<Token> Tokenize(string s)
    {
        var list = new List<Token>();
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '{') list.Add(new Token(TokenKind.Open, "{"));
            else if (c == '}') list.Add(new Token(TokenKind.Close, "}"));
            else if (c == '"')
            {
                var end = s.IndexOf('"', i + 1);
                if (end < 0) break;                       // unterminated: keep what we have
                list.Add(new Token(TokenKind.Text, s[(i + 1)..end]));
                i = end;
            }
        }
        return list;
    }

    /// <summary>
    /// The tracked files belonging to one save unit, matched on its folder name.
    ///
    /// <para>Matched by path SEGMENT rather than by prefix on purpose. The cache stores paths relative
    /// to a Steam root constant we do not decode, so the caller's absolute local path and Steam's
    /// relative one share only their tail. A world folder's name — a GUID, a slot name — is the part
    /// both agree on.</para>
    ///
    /// <para>A null or empty segment means the whole game: every tracked file.</para>
    /// </summary>
    public static IReadOnlyList<SteamCloudFile> Under(IReadOnlyList<SteamCloudFile>? files, string? folderSegment)
    {
        if (files is null || files.Count == 0) return Array.Empty<SteamCloudFile>();
        var seg = (folderSegment ?? "").Replace('\\', '/').Trim('/');
        if (seg.Length == 0) return files;

        return files.Where(f => ("/" + f.Path + "/").Contains("/" + seg + "/", StringComparison.OrdinalIgnoreCase))
                    .ToList();
    }
}

/// <summary>
/// What Steam Cloud holds for one save unit, in the terms the panel actually needs.
/// </summary>
/// <param name="FileCount">How many tracked files. Zero means Steam is not holding this.</param>
/// <param name="Bytes">What it occupies in the account's cloud quota — the cost of duplicating it.</param>
public sealed record CloudCoverage(int FileCount, long Bytes)
{
    public static readonly CloudCoverage None = new(0, 0);

    /// <summary><b>The load-bearing question.</b> A folder deleted from disk while Steam still holds
    /// it comes back on the next launch, so a delete only sticks if the game itself performs it.</summary>
    public bool IsTracked => FileCount > 0;

    public static CloudCoverage For(IReadOnlyList<SteamCloudFile>? files)
        => files is null || files.Count == 0
            ? None
            : new CloudCoverage(files.Count, files.Sum(f => f.Bytes));

    public static CloudCoverage For(IReadOnlyList<SteamCloudFile>? all, string? folderSegment)
        => For(SteamCloudCache.Under(all, folderSegment));
}
