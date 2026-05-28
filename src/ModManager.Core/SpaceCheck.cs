namespace ModManager.Core;

/// <summary>
/// Free-space pre-flight for archive / restore operations. The bytes overloads are fully testable
/// with no disk; <see cref="Require"/> reads DriveInfo (System.IO — Core-legal) in production.
/// </summary>
public static class SpaceCheck
{
    public sealed record Result(bool Ok, long RequiredBytes, long AvailableBytes, string VolumeRoot);

    /// <summary>Required headroom = payload + max(marginPct of payload, floorBytes).</summary>
    public static long RequiredWithHeadroom(long payloadBytes, double marginPct = 0.10, long floorBytes = 1L << 30)
        => payloadBytes + Math.Max((long)(payloadBytes * marginPct), floorBytes);

    /// <summary>Testable core: compare a required figure against a known available figure.</summary>
    public static Result Evaluate(string volumeRoot, long payloadBytes, long availableBytes,
                                  double marginPct = 0.10, long floorBytes = 1L << 30)
    {
        var required = RequiredWithHeadroom(payloadBytes, marginPct, floorBytes);
        return new Result(availableBytes >= required, required, availableBytes, volumeRoot);
    }

    /// <summary>Production entry: reads DriveInfo for the volume hosting <paramref name="anyPathOnVolume"/>.</summary>
    public static Result Require(string anyPathOnVolume, long payloadBytes,
                                 double marginPct = 0.10, long floorBytes = 1L << 30)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(anyPathOnVolume)) ?? anyPathOnVolume;
        var available = new DriveInfo(root).AvailableFreeSpace;
        return Evaluate(root, payloadBytes, available, marginPct, floorBytes);
    }
}
