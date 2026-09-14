namespace ModManager.Tests;

/// <summary>
/// The lane a toggle takes is chosen in <c>ModToggle</c>, in Core, and nowhere else.
///
/// <para>It used to be chosen twice: once in the app's view-model and once, wrongly, in the agent tool,
/// which sent every mod through the scanner's folder move and reported "ok" on direct-inject games
/// while moving nothing. These scans fail the build if either surface starts moving mod files itself
/// again. The next new surface copies whichever call it finds first; this makes sure the only call it
/// can find is the router.</para>
/// </summary>
public class ModToggleCallSiteTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ModManager.App")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static List<string> FilesContaining(string project, string needle)
    {
        var root = Path.Combine(RepoRoot(), "src", project);
        Assert.True(Directory.Exists(root), $"missing project folder {root}");
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .Where(f => File.ReadAllText(f).Contains(needle))
            .Select(f => Path.GetRelativePath(RepoRoot(), f))
            .ToList();
    }

    [Theory]
    [InlineData("Scanner.SetAppendedRowEnabledAsync(")]
    [InlineData("Scanner.SetLoaderModEnabledAsync(")]
    [InlineData("DirectInject.Enable(")]
    [InlineData("DirectInject.Disable(")]
    [InlineData("ModEngine2Writer.SetEnabled(")]
    public void The_agent_tool_writes_only_through_the_router(string call)
    {
        var offenders = FilesContaining("ModManager.Mcp", call);
        Assert.True(offenders.Count == 0, $"Use ModToggle.SetEnabledAsync instead of {call} in: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("Scanner.SetAppendedRowEnabledAsync(")]
    [InlineData("DirectInject.Enable(")]
    [InlineData("DirectInject.Disable(")]
    [InlineData("DirectInject.EnableSingleFile(")]
    [InlineData("DirectInject.DisableSingleFile(")]
    public void The_app_does_not_move_mod_files_itself(string call)
    {
        // Scanner.SetLoaderModEnabledAsync is deliberately NOT in this list: the variant-family picker
        // switches between loader variants by name and has no lane to choose.
        var offenders = FilesContaining("ModManager.App", call);
        Assert.True(offenders.Count == 0, $"Route through ModToggle instead of {call} in: " + string.Join(", ", offenders));
    }
}
