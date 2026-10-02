namespace ModManager.Core;

/// <summary>
/// Tests only: counts the two expensive reads a toggle can make, a full mod listing
/// (<c>Scanner.BuildModList</c>) and a read of every extra tree (<see cref="ModTrees.Build"/>), so a test
/// can assert how many a bulk operation pays without timing anything. Null by default, and a null
/// <see cref="Current"/> costs one field read. Thread-static for the same reason as the scanner's other
/// test hooks: the toggle runs synchronously on the calling thread, so one test's count never sees
/// another test running in parallel.
/// </summary>
internal sealed class ScanCostProbe
{
    [ThreadStatic] internal static ScanCostProbe? Current;

    internal int ModListBuilds;
    internal int TreeBuilds;

    internal static void CountModList()
    {
        if (Current is { } p) p.ModListBuilds++;
    }

    internal static void CountTrees()
    {
        if (Current is { } p) p.TreeBuilds++;
    }
}
