using System.Text;

namespace ModManager.Core;

/// <summary>
/// The folder a mod is held in while it is turned off, under <c>disabled</c> and <c>disabled-trees</c>.
///
/// <para>Windows opens a path after normalising it: a trailing dot or space is stripped, so <c>Foo.</c> and
/// <c>Foo </c> open <c>Foo</c>, and <c>CON</c>, <c>NUL</c>, <c>COM1</c> and the rest (with or without an
/// extension) open a device. Mod names come from file names (<c>Foo _P.pak</c> is <c>Foo </c>,
/// <c>Foo..archive</c> is <c>Foo.</c>, <c>CON_P.pak</c> is <c>CON</c>), so a holding folder named after the mod
/// could be another mod's, or no folder at all.</para>
///
/// <para>An ordinary name is its own folder, unchanged, so every folder an earlier build made still works
/// with no migration. A name Windows would not keep as written gets <c>~626~</c> plus the lowercase hex of its
/// UTF-8 bytes: <c>Foo.</c> is held in <c>~626~466f6f2e</c>. The encoding is reversible
/// (<see cref="ModName"/>), never collides with an ordinary name (those never start with the prefix, which is
/// why a name that does is encoded too), and two names never share a folder.</para>
/// </summary>
internal static class HoldingName
{
    /// <summary>What every encoded folder name starts with.</summary>
    public const string Prefix = "~626~";

    /// <summary>The longest name kept as its own folder.</summary>
    public const int MaxPlainLength = 200;

    private static readonly HashSet<string> DeviceNames = BuildDeviceNames();

    private static HashSet<string> BuildDeviceNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL" };
        foreach (var port in new[] { "COM", "LPT" })
        {
            for (var i = 0; i <= 9; i++) set.Add(port + i);
            // Windows treats the superscript digits as port numbers too.
            foreach (var sup in new[] { '¹', '²', '³' }) set.Add(port + sup);
        }
        return set;
    }

    /// <summary>The holding folder for <paramref name="modName"/>: the name itself when Windows keeps it as
    /// one folder exactly as written, otherwise the <see cref="Prefix"/> encoding. Never empty.</summary>
    public static string Folder(string modName)
        => IsPlain(modName) ? modName : Prefix + Convert.ToHexStringLower(Wtf8Encode(modName));

    /// <summary>
    /// The mod a holding folder belongs to. A <see cref="Prefix"/> name followed by the lowercase hex of a
    /// valid UTF-8 name decodes to that name; any other folder name is the mod's name as it is (an ordinary
    /// name, or a folder an earlier build made). Null for a malformed encoding, so a listing skips the folder
    /// rather than guessing whose it is.
    /// </summary>
    public static string? ModName(string folderName)
    {
        if (!folderName.StartsWith(Prefix, StringComparison.Ordinal)) return folderName;
        var hex = folderName.AsSpan(Prefix.Length);
        if (hex.Length == 0 || hex.Length % 2 != 0) return null;
        foreach (var ch in hex)
            if (!(ch is >= '0' and <= '9' or >= 'a' and <= 'f')) return null;
        var bytes = Convert.FromHexString(hex);
        var name = Wtf8Decode(bytes);
        // Only the one spelling Folder writes: an overlong or otherwise non-canonical byte sequence is not a
        // name 626 encoded.
        if (name is null || !Wtf8Encode(name).AsSpan().SequenceEqual(bytes)) return null;
        return name;
    }

    // True when Windows keeps the name as one folder exactly as written, and it can't be mistaken for an
    // encoded folder.
    private static bool IsPlain(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxPlainLength) return false;
        if (name is "." or "..") return false;
        if (name.EndsWith('.') || name.EndsWith(' ')) return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        if (name.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        // A device name is matched on the part before the first dot, spaces before that dot ignored, as
        // Windows matches it: "con.txt" and "CON .txt" are both the console.
        var dot = name.IndexOf('.');
        var stem = (dot < 0 ? name : name[..dot]).TrimEnd(' ');
        return !DeviceNames.Contains(stem);
    }

    // UTF-8, extended to a lone surrogate (WTF-8): a Windows file name can hold one, and the encoding must
    // still round-trip it rather than turn it into U+FFFD, which would put two names in one folder. For a
    // well-formed name this is exactly UTF-8.
    private static byte[] Wtf8Encode(string s)
    {
        var bytes = new List<byte>(s.Length * 3);
        for (var i = 0; i < s.Length; i++)
        {
            int cp = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                cp = char.ConvertToUtf32(s[i], s[++i]);

            if (cp < 0x80) bytes.Add((byte)cp);
            else if (cp < 0x800)
            {
                bytes.Add((byte)(0xC0 | (cp >> 6)));
                bytes.Add((byte)(0x80 | (cp & 0x3F)));
            }
            else if (cp < 0x10000)
            {
                bytes.Add((byte)(0xE0 | (cp >> 12)));
                bytes.Add((byte)(0x80 | ((cp >> 6) & 0x3F)));
                bytes.Add((byte)(0x80 | (cp & 0x3F)));
            }
            else
            {
                bytes.Add((byte)(0xF0 | (cp >> 18)));
                bytes.Add((byte)(0x80 | ((cp >> 12) & 0x3F)));
                bytes.Add((byte)(0x80 | ((cp >> 6) & 0x3F)));
                bytes.Add((byte)(0x80 | (cp & 0x3F)));
            }
        }
        return bytes.ToArray();
    }

    // The inverse of Wtf8Encode, structurally: null for a truncated or invalid sequence. ModName re-encodes
    // the result to reject every spelling Wtf8Encode would not have written.
    private static string? Wtf8Decode(byte[] b)
    {
        var sb = new StringBuilder(b.Length);
        for (var i = 0; i < b.Length;)
        {
            int lead = b[i], need, cp;
            if (lead < 0x80) { need = 0; cp = lead; }
            else if (lead is >= 0xC2 and <= 0xDF) { need = 1; cp = lead & 0x1F; }
            else if (lead is >= 0xE0 and <= 0xEF) { need = 2; cp = lead & 0x0F; }
            else if (lead is >= 0xF0 and <= 0xF4) { need = 3; cp = lead & 0x07; }
            else return null;
            if (i + need >= b.Length) return null;   // truncated
            for (var k = 1; k <= need; k++)
            {
                var c = b[i + k];
                if ((c & 0xC0) != 0x80) return null;
                cp = (cp << 6) | (c & 0x3F);
            }
            if (cp > 0x10FFFF) return null;
            if (cp >= 0x10000) sb.Append(char.ConvertFromUtf32(cp));
            else sb.Append((char)cp);
            i += need + 1;
        }
        return sb.ToString();
    }
}
