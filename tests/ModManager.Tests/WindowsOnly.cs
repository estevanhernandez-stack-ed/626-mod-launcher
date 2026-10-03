namespace ModManager.Tests;

/// <summary>
/// The ONE place the test project decides a test can't run on this host. xUnit 2.9 has no runtime skip, so
/// the decision is made when the attribute is constructed and lands in <see cref="FactAttribute.Skip"/>: the
/// test then reports as skipped, with the reason, rather than failing or passing vacuously. The decision is a
/// pure function of its inputs (<see cref="SkipReason"/>) so the skip path itself is tested on Windows too.
///
/// <para>Use <see cref="WindowsFactAttribute"/> / <see cref="WindowsTheoryAttribute"/> for anything that needs
/// junctions (<c>mklink /J</c>), UNC or <c>\\?\</c> paths, Windows special folders or drive letters. Set
/// <c>NeedsWritableSystemDriveRoot</c> when the test builds its fixture at the system drive's root (outside
/// the user profile), so a locked-down machine skips it instead of failing.</para>
/// </summary>
public static class WindowsOnly
{
    public const string NotWindows = "Windows only: junctions, UNC and \\\\?\\ paths, special folders and drive letters.";
    public const string RootNotWritable = "Needs a writable system-drive root (a fixture outside the user profile); this machine's isn't.";

    /// <summary>Why a test can't run here, or null when it can.</summary>
    public static string? SkipReason(bool isWindows, bool needsWritableRoot, Func<bool> rootWritable)
    {
        if (!isWindows) return NotWindows;
        if (needsWritableRoot && !rootWritable()) return RootNotWritable;
        return null;
    }

    /// <summary>The system drive's root on Windows (<c>C:\</c>), else null. Never builds a relative "C:\" path.</summary>
    public static string? SystemDriveRoot()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var sys = Environment.SystemDirectory;
        if (string.IsNullOrEmpty(sys)) return null;
        var root = Path.GetPathRoot(sys);
        return string.IsNullOrEmpty(root) ? null : root;
    }

    private static readonly Lazy<bool> RootWritableOnce = new(() =>
    {
        if (SystemDriveRoot() is not { } root) return false;
        var probe = Path.Combine(root, "626-write-probe-" + Guid.NewGuid().ToString("n")[..8]);
        try
        {
            Directory.CreateDirectory(probe);
            Directory.Delete(probe);
            return true;
        }
        catch { return false; }
    });

    /// <summary>True when a folder can be made (and removed) at the system drive's root. Probed once.</summary>
    public static bool SystemDriveRootWritable => RootWritableOnce.Value;

    internal static string? CurrentSkip(bool needsWritableRoot)
        => SkipReason(OperatingSystem.IsWindows(), needsWritableRoot, () => SystemDriveRootWritable);
}

/// <summary>A <see cref="FactAttribute"/> that skips off Windows (and, if asked, without a writable system-drive root).</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { Skip = WindowsOnly.CurrentSkip(needsWritableRoot: false); }

    public bool NeedsWritableSystemDriveRoot
    {
        get => _needs;
        set { _needs = value; Skip = WindowsOnly.CurrentSkip(value); }
    }
    private bool _needs;
}

/// <summary>A <see cref="TheoryAttribute"/> that skips off Windows (and, if asked, without a writable system-drive root).</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute() { Skip = WindowsOnly.CurrentSkip(needsWritableRoot: false); }

    public bool NeedsWritableSystemDriveRoot
    {
        get => _needs;
        set { _needs = value; Skip = WindowsOnly.CurrentSkip(value); }
    }
    private bool _needs;
}

public class WindowsOnlyTests
{
    [Fact]
    public void Off_Windows_every_Windows_only_test_skips_with_the_reason()
        => Assert.Equal(WindowsOnly.NotWindows, WindowsOnly.SkipReason(isWindows: false, needsWritableRoot: false, () => true));

    [Fact]
    public void Off_Windows_the_root_check_is_never_even_made()
        => Assert.Equal(WindowsOnly.NotWindows,
            WindowsOnly.SkipReason(isWindows: false, needsWritableRoot: true, () => throw new InvalidOperationException("probed off Windows")));

    [Fact]
    public void On_Windows_without_a_writable_root_a_root_test_skips_and_others_run()
    {
        Assert.Equal(WindowsOnly.RootNotWritable, WindowsOnly.SkipReason(isWindows: true, needsWritableRoot: true, () => false));
        Assert.Null(WindowsOnly.SkipReason(isWindows: true, needsWritableRoot: false, () => false));
    }

    [Fact]
    public void On_Windows_with_a_writable_root_nothing_skips()
        => Assert.Null(WindowsOnly.SkipReason(isWindows: true, needsWritableRoot: true, () => true));

    [Fact]
    public void The_attributes_carry_the_decision_for_this_host()
    {
        var expected = OperatingSystem.IsWindows() ? null : WindowsOnly.NotWindows;
        Assert.Equal(expected, new WindowsFactAttribute().Skip);
        Assert.Equal(expected, new WindowsTheoryAttribute().Skip);
    }

    [Fact]
    public void The_system_drive_root_is_never_a_relative_path()
    {
        var root = WindowsOnly.SystemDriveRoot();
        if (OperatingSystem.IsWindows()) Assert.True(Path.IsPathFullyQualified(root!));
        else Assert.Null(root);
    }
}
