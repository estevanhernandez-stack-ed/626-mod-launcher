using System.Text.RegularExpressions;
using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// The ban-risk warning may not claim a MECHANISM the manifest never told us.
///
/// <para>Every flagged game got the same sentence — <i>"This game uses anti-cheat"</i> — in five
/// places: the strip chip, the library badge's tooltip and screen-reader text, the enable-gate
/// dialog, and the refusal an agent gets. The manifest carries a LEVEL (low / medium / high) and no
/// reason, so that sentence was an invention.</para>
///
/// <para>Palworld is the counter-example that made it visible. Pocketpair's own mod guideline bans
/// mods on official servers under threat of suspension — a real, high risk — and the game ships no
/// anti-cheat at all. Telling that player their game "uses anti-cheat" hands them a reason to
/// disbelieve the one warning in this app that can cost them an account.</para>
///
/// <para>So the copy says what we actually know: the risk, not its enforcement.</para>
/// </summary>
public class BanRiskCopyTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ModManager.App")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void The_chip_states_the_risk_without_naming_a_mechanism()
    {
        var chip = GameStateStrip.For(new GameStateConditions { BanRisk = true })[0];

        Assert.Contains("can get your account banned", chip.Detail);
        Assert.DoesNotContain("anti-cheat", chip.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_user_facing_string_anywhere_claims_the_game_uses_anti_cheat()
    {
        // Five places said it. A test is the only thing that keeps the sixth from being written -
        // "anti-cheat" is the obvious word to reach for when describing a ban risk, and it is only
        // correct for some of these games.
        var roots = new[] { "src/ModManager.App", "src/ModManager.Core" };
        var offenders = new List<string>();

        foreach (var rel in roots)
        {
            var root = Path.Combine(RepoRoot(), rel.Replace('/', Path.DirectorySeparatorChar));
            foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                if (!f.EndsWith(".cs") && !f.EndsWith(".xaml")) continue;
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

                var text = File.ReadAllText(f);
                // Comments explain the history on purpose; only the strings are policed.
                text = Regex.Replace(text, @"//.*?$", "", RegexOptions.Multiline);
                text = Regex.Replace(text, @"<!--.*?-->", "", RegexOptions.Singleline);
                text = Regex.Replace(text, @"///.*?$", "", RegexOptions.Multiline);

                if (Regex.IsMatch(text, @"uses anti-cheat", RegexOptions.IgnoreCase))
                    offenders.Add(Path.GetRelativePath(RepoRoot(), f));
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_warning_still_says_the_thing_that_matters()
    {
        // Removing a false claim must not remove the true one. The consequence - an account, lost -
        // is the whole reason this chip sits first and cannot be dismissed.
        var chip = GameStateStrip.For(new GameStateConditions { BanRisk = true })[0];

        Assert.Equal("ban-risk", chip.Id);
        Assert.False(chip.Dismissible);
        Assert.Contains("online", chip.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
