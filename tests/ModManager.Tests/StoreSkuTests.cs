using System.Text.RegularExpressions;

namespace ModManager.Tests;

/// <summary>
/// The Store SKU ships Nexus compiled in, and nothing may quietly undo that.
///
/// <para><b>What went wrong.</b> Nexus-in-package was opt-in — <c>-p:StoreNexus=true</c> on top of
/// <c>Configuration=Store</c> — so that a plain <c>-c Store</c> kept producing the already-certified
/// Nexus-free package. Sound at the time. But <c>release-msstore.yml</c> builds <c>-c Store</c> and
/// never passed the flag, so <b>every package CI produced had no Nexus in it</b> while everyone
/// involved believed otherwise. An opt-in that the only automated caller does not opt into is a
/// default with extra steps.</para>
///
/// <para><b>Why the seal did not catch it.</b> <c>check-store-seal.ps1</c> only asserted what must be
/// ABSENT. A check that only asserts absence passes a build with nothing in it — absence-only is how
/// an empty box gets certified. It asserts the compiled-in Nexus is present now too.</para>
///
/// <para>These tests are the cheap half of that guard: the seal needs a Windows build to run, this
/// needs nothing, and it fails the moment someone reverts the default in the csproj.</para>
/// </summary>
public class StoreSkuTests
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

    private static string Csproj() => Read("src", "ModManager.App", "ModManager.App.csproj");

    [Fact]
    public void Nexus_is_compiled_into_EVERY_configuration_now()
    {
        // The delivery split was never Microsoft's rule - it was Nexus's. Their integration could not
        // ship until they approved us as a partner, and the downloaded plugin kept it off a certified
        // package meanwhile. The approval landed, so both SKUs compile it in and the two can no longer
        // sit on different Nexus versions.
        var csproj = Csproj();

        // The Compile item that pulls the plugin sources in carries NO Configuration condition.
        var item = new Regex(@"<ItemGroup(?<cond>[^>]*)>\s*<Compile Include=""[^""]*ModManager\.Plugin\.Nexus");
        var m = item.Match(csproj);
        Assert.True(m.Success, "the Nexus sources are no longer compiled in at all");
        Assert.DoesNotContain("Condition", m.Groups["cond"].Value);
    }

    [Fact]
    public void There_is_no_switch_for_building_without_Nexus()
    {
        // Deliberately gone. This file's own history is the argument: StoreNexus was opt-in, the only
        // automated caller never opted in, and every package CI produced had no Nexus in it while
        // everyone believed otherwise. A Nexus-free variant that nobody builds is that trap re-laid.
        // Restoring it is a small job on the day, and would need a resubmission anyway.
        var csproj = Csproj();

        // Asserted against the MSBuild FORMS, not the word: the comment explaining why the switch is
        // gone is worth keeping, and a test that forbids naming a thing forbids explaining it too.
        Assert.DoesNotContain("$(StoreNexus)", csproj);
        Assert.DoesNotContain("<StoreNexus>", csproj);
        Assert.DoesNotContain("STORE_NEXUS", csproj);
    }

    [Fact]
    public void The_seal_checks_what_must_be_present_and_not_only_what_must_be_absent()
    {
        var seal = Read("scripts", "check-store-seal.ps1");

        // Absence half — the two things a sealed Store binary may never contain.
        Assert.Contains("PluginFeedSource", seal);
        Assert.Contains("AntiCheatState", seal);

        // Presence half — the half that was missing, and the reason this shipped wrong.
        Assert.Contains("ModManager.Plugin.Nexus", seal);
        Assert.Contains("AllowNoNexus", seal);
    }

    [Fact]
    public void Store_CI_does_not_rely_on_a_flag_a_human_has_to_remember()
    {
        // If someone re-adds an explicit -p:StoreNexus=true here it is harmless, but the DEFAULT is
        // what has to hold: the failure was a workflow that simply never passed it.
        var ci = Read(".github", "workflows", "release-msstore.yml");

        Assert.Contains("-c Store", ci);
        Assert.Contains("check-store-seal.ps1 -SkipBuild", ci);
    }

    [Fact]
    public void Both_SKUs_are_still_built_because_only_one_of_them_can_carry_the_EAC_toggle()
    {
        // Este's call: Store for everyone, FULL for EAC titles, both channels shipping. The EAC-disable
        // mechanism is #if FULL in Core and the Store seal ENFORCES its absence, so the Store package
        // cannot carry it — dropping the FULL flavor would delete the only in-app way to mod an
        // anti-cheat title, which is a capability decision and not a packaging one.
        Assert.Contains("FULL", Csproj());

        var workflows = Directory.GetFiles(Path.Combine(RepoRoot(), ".github", "workflows"), "*.yml")
            .Select(Path.GetFileName)
            .ToList();

        Assert.Contains("release.yml", workflows);
        Assert.Contains("release-msstore.yml", workflows);
    }
}
