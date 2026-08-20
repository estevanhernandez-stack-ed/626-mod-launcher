using System.Text;

namespace ModManager.Core;

/// <summary>Where a world's own name sits inside <c>LevelMeta.sav</c>, and how much room it has.</summary>
/// <param name="Offset">Byte offset of the first character.</param>
/// <param name="BudgetBytes">How many BYTES a replacement may occupy. Not characters — see
/// <see cref="PalworldWorldName"/>.</param>
/// <param name="Name">The name as it reads today, with the padding trimmed off the end.</param>
public sealed record WorldNameSite(int Offset, int BudgetBytes, string Name);

/// <summary>
/// The name Palworld itself shows for a world, read and written in place.
///
/// <para><b>This file is 2 KB and its tail is plaintext.</b> An earlier design said the container was
/// opaque and dropped the features that depended on it; that was a scan run badly, not a fact. The
/// property list reads <c>WorldName / StrProperty / &lt;the name&gt;</c>, every string shaped
/// <c>&lt;length byte&gt;&lt;bytes&gt;&lt;NUL&gt;</c>, and the name occurs exactly once.</para>
///
/// <para><b>The budget is the current name's byte length, and that is not a design choice.</b> A
/// longer name changes the decompressed size, which makes the header's own uncompressed-length field a
/// lie, which means owning the compressor. <c>PlM1</c> is not zlib — deflate fails at every plausible
/// offset — and the raw literals visible in the payload point at Oodle. That dependency buys longer
/// strings and costs a corrupt save on a game that patches monthly, so a rename fits the bytes already
/// there and pads the rest with spaces. Palworld's world list is left-aligned, so the padding has
/// nowhere to show.</para>
///
/// <para><b>A name that does not fill its budget may not stay readable.</b> Observed on the real
/// game: a six-byte name padded to seventeen was re-saved by Palworld overnight and came back three
/// bytes shorter - the codec turned the eleven-space run into a back-reference, so the name region
/// stopped being literal bytes. The world still shows the right name in-game; we simply cannot read
/// or rewrite it again. A name that fills its budget EXACTLY introduces no run and is durable - the
/// world called ItjustEst Islands has survived months of re-saves.</para>
///
/// <para>So a rename always records the launcher's own label too. The label is the durable display
/// record; the bytes in the save are the part Palworld reads. Losing the second must never cost the
/// user the first.</para>
///
/// <para><b>Bytes, not characters.</b> UTF-8: an accented letter costs two and an emoji four. Every
/// measurement here is <see cref="Encoding.UTF8"/>, and any UI counting characters instead would be
/// lying to everyone who does not type ASCII.</para>
///
/// <para>Verified on a real install: a same-length write loads, and Palworld re-saves the world
/// carrying the new name — it adopts what we write rather than tolerating it. See
/// <c>docs/superpowers/specs/2026-08-19-the-world-name-is-readable-design.md</c>.</para>
/// </summary>
public static class PalworldWorldName
{
    public const string MetaFileName = "LevelMeta.sav";

    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("WorldName\0");
    private static readonly byte[] StrProp = Encoding.ASCII.GetBytes("StrProperty\0");

    /// <summary>
    /// Whether this world has a save of its own at all - as opposed to having one whose name we cannot
    /// currently locate. Two different answers that <see cref="Read"/> returns null for alike, and the
    /// user deserves a different sentence for each.
    /// </summary>
    public static bool HasOwnSave(string worldDir)
    {
        try { return File.Exists(Path.Combine(worldDir, MetaFileName)); }
        catch { return false; }
    }

    /// <summary>How many bytes a name costs on disk. The number the budget is measured against.</summary>
    public static int ByteLength(string? name) => Encoding.UTF8.GetByteCount(name ?? "");

    /// <summary>Whether a name fits, measured the same way the write measures it.</summary>
    public static bool Fits(string? name, int budgetBytes) => ByteLength((name ?? "").Trim()) <= budgetBytes;

    /// <summary>
    /// The world's own name, or null when there is not one to read.
    ///
    /// <para>Null is an ordinary answer, not a failure: a world you JOINED holds only
    /// <c>LocalData.sav</c> — the world itself is on the host's machine — so there is no name in it.
    /// Callers fall back to the user's label and then to an ordinal.</para>
    ///
    /// <para>Never throws. A missing, truncated, locked or unrecognised file is null.</para>
    /// </summary>
    public static WorldNameSite? Read(string worldDir)
    {
        try
        {
            var path = Path.Combine(worldDir, MetaFileName);
            return File.Exists(path) ? Locate(File.ReadAllBytes(path)) : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Rename in place, padding to the exact budget so every length field in the file stays true.
    ///
    /// <para>Snapshot-first then atomic temp-and-rename, per the file-op laws. The snapshot is the 2 KB
    /// meta file alone rather than a zip of the whole world: it is the only file touched, and a rename
    /// is cheap enough that people will do it more than once. It lands beside that world's snapshots,
    /// where <see cref="SaveManager.ListSnapshots"/> cannot see it — that globs <c>*.zip</c>.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no name here to replace, or the new one
    /// does not fit.</exception>
    public static void Write(string worldDir, string newName, string? snapshotDir = null)
    {
        var path = Path.Combine(worldDir, MetaFileName);
        if (!File.Exists(path))
            throw new InvalidOperationException(
                "This world has no LevelMeta.sav, so Palworld has no name to show for it. That is normal " +
                "for a world somebody else hosts.");

        var raw = File.ReadAllBytes(path);
        var site = Locate(raw)
            ?? throw new InvalidOperationException(
                "Could not find the world name in this save. Palworld may have changed its format — " +
                "nothing was written.");

        var clean = (newName ?? "").Trim();
        if (clean.Length == 0)
            throw new InvalidOperationException("A world name cannot be blank.");
        if (clean.Any(char.IsControl))
            throw new InvalidOperationException("A world name cannot contain control characters.");

        var bytes = Encoding.UTF8.GetBytes(clean);
        if (bytes.Length > site.BudgetBytes)
            throw new InvalidOperationException(
                $"That name is {bytes.Length} bytes and this world has room for {site.BudgetBytes}. " +
                "Palworld stores the name in a fixed space inside the save, so a new one has to fit it.");

        var next = (byte[])raw.Clone();
        for (var i = 0; i < site.BudgetBytes; i++) next[site.Offset + i] = (byte)' ';
        Array.Copy(bytes, 0, next, site.Offset, bytes.Length);

        if (!string.IsNullOrEmpty(snapshotDir))
        {
            Directory.CreateDirectory(snapshotDir);
            File.Copy(path, Path.Combine(snapshotDir,
                $"before-rename-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.{MetaFileName}"), overwrite: false);
        }

        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, next);
        File.Move(tmp, path, overwrite: true);
    }

    private static WorldNameSite? Locate(byte[] raw)
    {
        var i = IndexOf(raw, Marker, 0);
        if (i < 0) return null;

        // StrProperty follows immediately in every save seen. Bounding the search stops a stray
        // WorldName elsewhere in the file from pairing with an unrelated string much later.
        var j = IndexOf(raw, StrProp, i);
        if (j < 0 || j - i > 24) return null;

        j += StrProp.Length;                 // raw[j] is the property size, raw[j + 1] the string length
        if (j + 1 >= raw.Length) return null;

        int strLen = raw[j + 1];             // includes the trailing NUL
        var start = j + 2;
        if (strLen < 1 || start + strLen > raw.Length) return null;
        if (raw[start + strLen - 1] != 0) return null;

        var budget = strLen - 1;
        return new WorldNameSite(start, budget,
            Encoding.UTF8.GetString(raw, start, budget).TrimEnd(' '));
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        for (var i = from; i <= haystack.Length - needle.Length; i++)
        {
            var k = 0;
            while (k < needle.Length && haystack[i + k] == needle[k]) k++;
            if (k == needle.Length) return i;
        }
        return -1;
    }
}
