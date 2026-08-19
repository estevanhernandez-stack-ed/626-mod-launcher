using System.Text.RegularExpressions;

namespace ModManager.Tests;

/// <summary>
/// Wave 10, item 7. <b>If two words name the same object, one of them is wrong.</b>
///
/// <para>The round table's rule was written about controls; this extends it to nouns, because the
/// launcher was using two words for one thing in three places and one word for two things in two more:</para>
///
/// <list type="bullet">
/// <item>A toolbar section headed <c>LOADOUT</c>, a <c>Profiles</c> button whose tooltip said
/// <i>"Saved loadouts"</i>, and a ProfilesDialog that used five labels for two words.</item>
/// <item>A Core type called <c>GameProfile</c> that had nothing to do with any of it — it records an
/// engine's save types. Invisible from the UI, which makes it worse, not better.</item>
/// <item><c>LIBRARY</c> naming two different things one click apart: your games on the home, and four
/// per-game actions in the game view.</item>
/// <item>And <c>LOADOUT</c> stopped being true in wave 6, when those three segments became a filter.</item>
/// </list>
///
/// <para><b>Why a test and not just an edit.</b> An edit fixes it once. Copy gets improved constantly
/// in this repo — that is the stated reason automation ids are not keyed on labels — so the word that
/// was retired comes back in six weeks unless something says no.</para>
///
/// <para><b>What this deliberately does NOT police:</b> <c>AutomationProperties.AutomationId</c> and
/// <c>x:Name</c>. <c>LoadoutAllSegment</c> keeps its name on purpose. Automation identity outlives
/// display copy — that is the whole point of <c>.claude/rules/automation-ids.md</c> — and renaming ids
/// to chase a copy change is the exact mistake the rule exists to prevent.</para>
/// </summary>
public class VocabularyTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ModManager.App")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    /// <summary>Strip the attributes that carry automation IDENTITY, which is deliberately frozen and
    /// must not be dragged along by a copy change.</summary>
    private static string DisplayTextOnly(string xaml)
    {
        // AutomationId and x:Name are exempt, and they are exempt TOGETHER on purpose: the
        // automation-ids rule says an id should match the x:Name rather than invent a second name for
        // one thing, so freezing one means freezing the other. LoadoutAllSegment is still called that
        // deliberately, next to a binding that now reads ShowAllBrush — identity outlives copy, and
        // renaming ids to chase a copy change is the exact mistake that rule exists to prevent.
        xaml = Regex.Replace(xaml, @"AutomationProperties\.AutomationId=""[^""]*""", "");
        xaml = Regex.Replace(xaml, @"x:Name=""[^""]*""", "");
        // Comments explain the history on purpose — "was headed LOADOUT" has to stay sayable.
        xaml = Regex.Replace(xaml, @"<!--.*?-->", "", RegexOptions.Singleline);
        return xaml;
    }

    [Fact]
    public void The_word_loadout_is_retired_from_everything_a_user_reads()
    {
        // It named two things at once: the MP/SP segments (a FILTER since wave 6 — it moves no files)
        // and a saved set of enabled mods (a profile). One of those had to give, and "profile" is what
        // every other mod manager calls the saved set.
        foreach (var file in new[] { "MainWindow.xaml", "ProfilesDialog.xaml" })
        {
            var text = DisplayTextOnly(Read("src", "ModManager.App", file));
            Assert.DoesNotContain("loadout", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Library_names_your_games_and_nothing_else()
    {
        // It was a heading on the library home AND a toolbar group in the game view, one click apart.
        // The game-view group is four per-game actions; it is MANAGE now.
        var main = DisplayTextOnly(Read("src", "ModManager.App", "MainWindow.xaml"));

        Assert.DoesNotContain("\"LIBRARY\"", main);
    }

    [Fact]
    public void Show_and_group_by_are_not_synonyms_sitting_at_two_ends_of_one_bar()
    {
        // SHOW decides which rows are listed; GROUP BY decides how they are stacked. The heading on
        // the right used to read VIEW, which says neither, and would have become a synonym for SHOW
        // the moment SHOW arrived.
        var main = Read("src", "ModManager.App", "MainWindow.xaml");

        Assert.Contains("Text=\"SHOW\"", main);
        Assert.Contains("Text=\"GROUP BY\"", main);
        Assert.DoesNotContain("Text=\"VIEW\"", main);
    }

    [Fact]
    public void Core_does_not_call_anything_else_a_profile()
    {
        // GameProfile recorded an engine's SAVE TYPES. Nothing to do with a saved set of enabled mods,
        // and invisible from the UI — which is worse, because nobody trips over it until they are
        // reading two files at once and one of them is lying.
        // Comments are exempt. Two files say "was GameProfile until wave 10" on purpose — a rename
        // whose reason is not written down gets undone by the next person who finds the old name in a
        // git log and assumes it read better.
        static string CodeOnly(string cs) =>
            Regex.Replace(cs, @"//.*?$", "", RegexOptions.Multiline);

        var core = Path.Combine(RepoRoot(), "src", "ModManager.Core");
        var offenders = Directory
            .EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(CodeOnly(File.ReadAllText(f)), @"\bGameProfiles?\b"))
            .Select(f => Path.GetRelativePath(core, f))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void A_profile_is_described_the_same_way_wherever_it_is_described()
    {
        // Five labels for two words was the original count. These are the two the user actually reads.
        var dialog = Read("src", "ModManager.App", "ProfilesDialog.xaml");
        var main = Read("src", "ModManager.App", "MainWindow.xaml");

        Assert.Contains("A profile saves which mods are on and which are off", dialog);
        Assert.Contains("Saved profiles", main);
    }

    // ---------------------------------------------------------------------------------------------
    // Item 8 — the accelerators exist. A shortcut is not discoverable from a UIA walk, so this is the
    // only place that can hold the app to having them.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("F", "OnFilterAccelerator")]
    [InlineData("R", "OnRefreshAccelerator")]
    [InlineData("O", "OnAddModsAccelerator")]
    [InlineData("P", "OnProfilesAccelerator")]
    [InlineData("Number1", "OnShowAllAccelerator")]
    [InlineData("Number2", "OnShowMpAccelerator")]
    [InlineData("Number3", "OnShowSpAccelerator")]
    public void Each_control_accelerator_is_bound(string key, string handler)
    {
        var main = Read("src", "ModManager.App", "MainWindow.xaml");

        Assert.Contains($"Modifiers=\"Control\" Key=\"{key}\" Invoked=\"{handler}\"", main);
    }

    [Fact]
    public void Ctrl_comma_opens_settings_and_is_wired_in_code_for_a_reason()
    {
        // The comma key is VK_OEM_COMMA (188). The VirtualKey enum has no named member for it and the
        // XAML compiler will not take the number, so this one is built in the constructor. Pinned
        // because "it is not in the XAML with the others" reads like an oversight otherwise.
        var cs = Read("src", "ModManager.App", "MainWindow.xaml.cs");

        Assert.Contains("(Windows.System.VirtualKey)188", cs);
        Assert.Contains("OnSettingsAccelerator", cs);
    }

    [Theory]
    [InlineData("Ctrl+O")]
    [InlineData("Ctrl+P")]
    [InlineData("Ctrl+1")]
    [InlineData("Ctrl+2")]
    [InlineData("Ctrl+3")]
    public void Every_new_shortcut_is_named_where_its_control_is(string combo)
    {
        // An accelerator nobody can discover is a shortcut for the person who wrote it. The app hides
        // the automatic accelerator tooltip (KeyboardAcceleratorPlacementMode="Hidden"), so the key
        // has to be said in the control's own tooltip or it is said nowhere.
        var main = Read("src", "ModManager.App", "MainWindow.xaml");

        Assert.Contains(combo, main);
    }
}
