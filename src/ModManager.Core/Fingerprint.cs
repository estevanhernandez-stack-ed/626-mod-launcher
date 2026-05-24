namespace ModManager.Core;

/// <summary>
/// CurseForge fingerprint matching. A CurseForge fingerprint is MurmurHash2 (x86, 32-bit,
/// seed 1) over the file with whitespace bytes (\t \n \r space) removed — the same algorithm
/// every CurseForge launcher uses to identify a local mod file. Pure (bytes in, number out).
/// Mirrors fingerprint-core.js. Golden pins: "mod-data" → 1807539333, "" → 1540447798.
/// </summary>
public static class Fingerprint
{
    /// <summary>Canonical MurmurHash2 (x86, 32-bit). All multiplies wrap in true 32-bit space.</summary>
    public static uint Murmur2(ReadOnlySpan<byte> data, uint seed)
    {
        const uint m = 0x5bd1e995;
        const int r = 24;
        int len = data.Length;
        uint h = seed ^ (uint)len;
        int i = 0;
        unchecked
        {
            while (len >= 4)
            {
                uint k = (uint)(data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24));
                k *= m;
                k ^= k >> r;
                k *= m;
                h *= m;
                h ^= k;
                i += 4; len -= 4;
            }
            // tail — intentional fallthrough, matching the C reference
            switch (len)
            {
                case 3: h ^= (uint)(data[i + 2] << 16); goto case 2;
                case 2: h ^= (uint)(data[i + 1] << 8); goto case 1;
                case 1: h ^= data[i]; h *= m; break;
            }
            h ^= h >> 13;
            h *= m;
            h ^= h >> 15;
        }
        return h;
    }

    /// <summary>Remove the four whitespace bytes CurseForge ignores, so a reformatted file still matches.</summary>
    public static byte[] StripWhitespace(ReadOnlySpan<byte> buf)
    {
        var out_ = new byte[buf.Length];
        int n = 0;
        foreach (var b in buf)
        {
            if (b is 9 or 10 or 13 or 32) continue;
            out_[n++] = b;
        }
        Array.Resize(ref out_, n);
        return out_;
    }

    public static uint CurseForgeFingerprint(ReadOnlySpan<byte> buf) => Murmur2(StripWhitespace(buf), 1);
}
