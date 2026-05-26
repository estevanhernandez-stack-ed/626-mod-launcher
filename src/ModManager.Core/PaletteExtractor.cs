namespace ModManager.Core;

/// <summary>One dominant color extracted from an image. Frequency is the number of source pixels
/// that ended up in this cluster (so callers can weight by visual prominence).</summary>
public sealed record PaletteColor(byte R, byte G, byte B, int Frequency);

/// <summary>
/// Classical k-means color quantization in RGB space — pure data in / data out, no WinRT, no UI.
/// Deterministic: seeds clusters by brightness-rank percentiles so the same input yields the same
/// palette every run (tests stay stable).
/// </summary>
public static class PaletteExtractor
{
    /// <summary>Extract up to <paramref name="k"/> dominant colors from RGBA pixel data. Fully
    /// transparent pixels are excluded (so transparent-PNG backgrounds don't drag the palette).
    /// Returns colors sorted by brightness ascending (darkest first) — matches the slot-mapper's
    /// expectation. Throws <see cref="ArgumentException"/> on empty input.</summary>
    public static IReadOnlyList<PaletteColor> Extract(byte[] rgbaPixels, int width, int height, int k)
    {
        if (rgbaPixels is null || rgbaPixels.Length == 0 || width <= 0 || height <= 0)
            throw new ArgumentException("Empty or invalid pixel buffer.");
        if (k < 1) k = 1;

        // Collect opaque pixels.
        var opaque = new List<(byte R, byte G, byte B)>(width * height);
        for (var i = 0; i + 3 < rgbaPixels.Length; i += 4)
        {
            if (rgbaPixels[i + 3] == 0) continue; // fully transparent
            opaque.Add((rgbaPixels[i], rgbaPixels[i + 1], rgbaPixels[i + 2]));
        }
        if (opaque.Count == 0) return Array.Empty<PaletteColor>();

        // k can't exceed the distinct opaque pixel count.
        var distinct = opaque.Distinct().Count();
        var effectiveK = Math.Min(k, distinct);

        // Deterministic seeding: sort by brightness, pick evenly-spaced percentile points.
        var sortedByBrightness = opaque
            .OrderBy(p => 0.299 * p.R + 0.587 * p.G + 0.114 * p.B)
            .ToList();
        var centers = new List<(double R, double G, double B)>(effectiveK);
        for (var i = 0; i < effectiveK; i++)
        {
            var idx = (int)((double)i / Math.Max(1, effectiveK - 1) * (sortedByBrightness.Count - 1));
            if (effectiveK == 1) idx = sortedByBrightness.Count / 2;
            var p = sortedByBrightness[idx];
            centers.Add((p.R, p.G, p.B));
        }

        // K-means iterations. Stop when no center moves >0.5 or after 20 iterations.
        const int maxIterations = 20;
        var assignments = new int[opaque.Count];
        for (var iter = 0; iter < maxIterations; iter++)
        {
            // Assign each pixel to its nearest center.
            for (var pi = 0; pi < opaque.Count; pi++)
            {
                var p = opaque[pi];
                var bestIdx = 0;
                var bestDist = double.MaxValue;
                for (var ci = 0; ci < centers.Count; ci++)
                {
                    var c = centers[ci];
                    var dr = p.R - c.R;
                    var dg = p.G - c.G;
                    var db = p.B - c.B;
                    var d = dr * dr + dg * dg + db * db;
                    if (d < bestDist) { bestDist = d; bestIdx = ci; }
                }
                assignments[pi] = bestIdx;
            }
            // Recompute centers as the mean of assigned pixels.
            var sums = new (double R, double G, double B, int N)[centers.Count];
            for (var pi = 0; pi < opaque.Count; pi++)
            {
                var p = opaque[pi];
                var ci = assignments[pi];
                sums[ci] = (sums[ci].R + p.R, sums[ci].G + p.G, sums[ci].B + p.B, sums[ci].N + 1);
            }
            var maxMove = 0.0;
            for (var ci = 0; ci < centers.Count; ci++)
            {
                if (sums[ci].N == 0) continue; // dead cluster — leave it where it was
                var newR = sums[ci].R / sums[ci].N;
                var newG = sums[ci].G / sums[ci].N;
                var newB = sums[ci].B / sums[ci].N;
                var moveR = newR - centers[ci].R;
                var moveG = newG - centers[ci].G;
                var moveB = newB - centers[ci].B;
                var move = Math.Sqrt(moveR * moveR + moveG * moveG + moveB * moveB);
                if (move > maxMove) maxMove = move;
                centers[ci] = (newR, newG, newB);
            }
            if (maxMove < 0.5) break;
        }

        // Frequency by cluster.
        var freq = new int[centers.Count];
        foreach (var a in assignments) freq[a]++;

        // Drop any zero-frequency clusters, then sort by brightness ascending.
        var result = new List<PaletteColor>();
        for (var ci = 0; ci < centers.Count; ci++)
        {
            if (freq[ci] == 0) continue;
            var c = centers[ci];
            result.Add(new PaletteColor(
                (byte)Math.Clamp(Math.Round(c.R), 0, 255),
                (byte)Math.Clamp(Math.Round(c.G), 0, 255),
                (byte)Math.Clamp(Math.Round(c.B), 0, 255),
                freq[ci]));
        }
        return result
            .OrderBy(p => 0.299 * p.R + 0.587 * p.G + 0.114 * p.B)
            .ToList();
    }
}
