namespace ModManager.Tests;

/// <summary>
/// Every surface outside Core asks <c>BanRiskCatalog.Effective(game)</c>. <c>ByAppId</c> alone reads
/// None for any game without a Steam id (EA app, Xbox, a folder), which switched the ban-risk law off
/// for exactly those games. The next new surface copies whichever call it finds first, so this test
/// makes sure the only call it can find is the right one.
/// </summary>
public class BanRiskCallSiteTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ModManager.App")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Theory]
    [InlineData("ModManager.App")]
    [InlineData("ModManager.Mcp")]
    public void No_surface_outside_Core_resolves_ban_risk_by_Steam_id_alone(string project)
    {
        var root = Path.Combine(RepoRoot(), "src", project);
        var sep = Path.DirectorySeparatorChar;
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            // No trailing "(" - a bare method-group reference (no invocation) is just as much a
            // Steam-id-only read as a call, and the guard should catch both.
            .Where(f => File.ReadAllText(f).Contains("BanRiskCatalog.ByAppId"))
            .Select(f => Path.GetRelativePath(RepoRoot(), f))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Use BanRiskCatalog.Effective(game) instead of ByAppId in: " + string.Join(", ", offenders));
    }
}
