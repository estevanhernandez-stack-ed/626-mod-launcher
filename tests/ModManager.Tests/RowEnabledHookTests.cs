namespace ModManager.Tests;

/// <summary>
/// The mod-row view-models set <c>Enabled</c> through the property in their constructors (C7: a partial
/// property has no backing field to write past). That is only harmless while nothing hooks the change:
/// a future <c>OnEnabledChanged</c> that writes a toggle to disk would fire for every enabled row on
/// every reload. This scan fails the build before that can ship, and says where to look.
/// </summary>
public class RowEnabledHookTests
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
    [InlineData("ModRowViewModel.cs")]
    [InlineData("VariantOptionVM.cs")]
    public void A_row_that_sets_Enabled_in_its_constructor_has_no_change_hook(string file)
    {
        var path = Path.Combine(RepoRoot(), "src", "ModManager.App", "ViewModels", file);
        var text = File.ReadAllText(path);

        Assert.Contains("Enabled = ", text);   // the constructor write this guard exists for
        Assert.False(text.Contains("OnEnabledChanged") || text.Contains("OnEnabledChanging"),
            $"{file} sets Enabled in its constructor, so a change hook there runs for every row on every "
            + "reload. Seed the value without the hook (a guard flag) before adding one.");
    }
}
