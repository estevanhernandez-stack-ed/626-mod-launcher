using System.Text.RegularExpressions;

namespace ModManager.Tests;

/// <summary>
/// Every App method that writes into a save folder consults <c>SaveWritePolicy</c> before it does.
///
/// <para>The policy is a Core decision, but the write primitives take folders, not games, so it is
/// enforced where the App calls them, and the App layer cannot be tested headless. This holds the
/// enforcement instead: a new handler that calls a save-write primitive without the gate, or an old one
/// that loses it, fails here. Same idea as <see cref="BanRiskCallSiteTests"/>.</para>
/// </summary>
public class SaveWriteCallSiteTests
{
    // Calls that write into a game's save folder.
    private static readonly string[] Writes =
    {
        "SaveManager.Restore(",
        "SaveManager.RestoreType(",
        "SaveManager.RestoreWorld(",
        "SaveManager.DuplicateWorld(",
        "SaveManager.CloneToType(",
        "SaveBundle.Restore(",
        "SaveModInstaller.",
        "SaveModFlow.TryHandleDrops(",
        "ProfileRestore.Restore(",
        "PalworldWorldName.Write(",
        ".EditCharacter(",
    };

    // What counts as consulting the policy, somewhere in the method before the call.
    private static readonly string[] Gates = { "WritesRefused()", "SaveWritePolicy.", "writeRefusal" };

    // A member declaration at class-member indent: where a method body begins.
    private static readonly Regex MethodStart = new(@"^    (?:(?:public|private|internal|protected|static|async|override|sealed)\s+)+[^=;]*\(", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ModManager.App")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> AppSources()
    {
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "ModManager.App"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"));
    }

    // Writers that are themselves the service a gated caller goes through, not a call site.
    private static readonly string[] Exempt = { "SaveEditorService.cs", "HeldBackupsService.cs" };

    [Fact]
    public void Every_app_save_write_is_preceded_by_the_write_policy()
    {
        var offenders = new List<string>();
        var sites = 0;
        foreach (var file in AppSources().Where(f => !Exempt.Contains(Path.GetFileName(f))))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//")) continue;
                if (!Writes.Any(w => lines[i].Contains(w))) continue;
                sites++;

                var start = i;
                while (start > 0 && !MethodStart.IsMatch(lines[start])) start--;
                var body = string.Join('\n', lines[start..(i + 1)]);
                if (!Gates.Any(body.Contains))
                    offenders.Add($"{Path.GetRelativePath(RepoRoot(), file)}:{i + 1}");
            }
        }

        Assert.True(sites >= 10, $"found only {sites} save-write call sites; the scan has stopped seeing them");
        Assert.True(offenders.Count == 0,
            "Consult SaveWritePolicy (WritesRefused() in SavesDialog) before writing saves at: " + string.Join(", ", offenders));
    }
}
