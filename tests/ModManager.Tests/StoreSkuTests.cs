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
    public void Configuration_Store_turns_Nexus_on_without_being_asked()
    {
        // The one line that stops CI shipping an empty box.
        var csproj = Csproj();

        Assert.Matches(
            new Regex(@"Condition=""'\$\(Configuration\)'\s*==\s*'Store'\s*AND\s*'\$\(StoreNexus\)'\s*==\s*''"""),
            csproj);
        Assert.Contains("<StoreNexus>true</StoreNexus>", csproj);
    }

    [Fact]
    public void The_Nexus_free_variant_is_still_reachable_on_purpose()
    {
        // -p:StoreNexus=false must keep working. The default changed; the capability did not go away,
        // and a flag that can only be turned on is not a flag.
        var csproj = Csproj();

        Assert.Contains("'$(StoreNexus)' == 'true'", csproj);
        Assert.Contains("STORE_NEXUS", csproj);
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
