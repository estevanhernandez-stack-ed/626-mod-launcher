using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModManager.Tests;

/// <summary>
/// Backlog E3. The smoke checklist as data, and the guard that keeps it honest.
///
/// <para><b>Why a test guards a docs file.</b> <c>pending.md</c> grew to 74 sections and 279 step lines
/// of prose across three months, with <c>merged YYYY-MM-DD</c> placeholders nobody filled. Read it as a
/// to-do list and you re-test things that were fine in May; read it as history and you ship believing
/// something was covered when it never was. Neither a person in a hurry nor an agent can tell which box
/// is which — Este: <i>"I've done most of that smoke list, I just haven't checked it off."</i></para>
///
/// <para>So the catalogue records what is verified, what is pending, and — the load-bearing part —
/// what no harness may ever claim. A run that executes the automatable part, reports all green, and
/// says nothing about the rest is the cherry-picked denominator wearing a different hat.</para>
///
/// <para>The tests that matter here are the two DRIFT ones: the catalogue and
/// <c>scripts/smoke-run.ps1</c> must name exactly the same cases in both directions. A harness case
/// missing from the catalogue is coverage nobody can audit; a catalogue case missing from the harness
/// is a promise nothing keeps.</para>
/// </summary>
public class SmokeCatalogueTests
{
    private static readonly string[] Coverages = { "harness", "agentable", "human", "untriaged" };
    private static readonly string[] Statuses = { "verified", "pending", "obsolete", "untriaged" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ModManager.App")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static JsonElement Catalogue()
    {
        var path = Path.Combine(RepoRoot(), "docs", "smoke-tests", "smoke.json");
        Assert.True(File.Exists(path), $"the smoke catalogue is missing: {path}");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    private static List<JsonElement> Cases() => Catalogue().GetProperty("cases").EnumerateArray().ToList();

    private static string Str(JsonElement c, string name)
        => c.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    private static string Harness() => File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "smoke-run.ps1"));

    private static HashSet<string> IdsIn(string pattern)
        => Regex.Matches(Harness(), pattern, RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    // ---------------------------------------------------------------- shape

    [Fact]
    public void The_catalogue_parses_and_is_not_empty()
    {
        // Guards the guard: a catalogue that failed to load would green every test below.
        Assert.Equal(1, Catalogue().GetProperty("schemaVersion").GetInt32());
        Assert.True(Cases().Count > 50, $"only {Cases().Count} cases — did the file get truncated?");
    }

    [Fact]
    public void Every_case_has_a_unique_kebab_id()
    {
        var ids = Cases().Select(c => Str(c, "id")).ToList();

        Assert.All(ids, id => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", id));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_case_declares_a_known_coverage_and_status()
    {
        foreach (var c in Cases())
        {
            Assert.Contains(Str(c, "coverage"), Coverages);
            Assert.Contains(Str(c, "status"), Statuses);
        }
    }

    // ---------------------------------------------------------------- drift

    [Fact]
    public void Every_harness_case_is_in_the_catalogue()
    {
        // Coverage nobody can audit. A case that only exists in the script is invisible to anyone
        // reading what this project claims to verify.
        var inScript = IdsIn(@"^Case\s+'([^']+)'");
        var inCatalogue = Cases().Where(c => Str(c, "coverage") == "harness").Select(c => Str(c, "id")).ToHashSet();

        Assert.Empty(inScript.Except(inCatalogue));
    }

    [Fact]
    public void Every_catalogue_harness_case_is_in_the_harness()
    {
        // The other direction, and the more dangerous one: a catalogue entry claiming harness coverage
        // that no script runs is a promise nothing keeps, and it reads as green from the outside.
        var inScript = IdsIn(@"^Case\s+'([^']+)'");
        var inCatalogue = Cases().Where(c => Str(c, "coverage") == "harness").Select(c => Str(c, "id")).ToHashSet();

        Assert.Empty(inCatalogue.Except(inScript));
    }

    [Fact]
    public void The_harness_reads_its_human_only_bucket_from_the_catalogue()
    {
        // The human list used to be twelve hard-coded lines in the script, which meant the awkward
        // cases could be trimmed by editing the thing that reports on them. Now the catalogue owns that
        // bucket and the script enumerates it — so shortening the gap requires changing the record.
        var script = Harness();

        Assert.Contains("smoke.json", script);
        Assert.Matches(@"coverage\s+-eq\s+'human'", script);
        Assert.DoesNotMatch(@"(?m)^HumanOnly\s+'", script);
    }

    [Fact]
    public void The_run_reports_against_the_catalogue_total_not_its_own_denominator()
    {
        // "20 verified" means nothing without the number it is out of. A harness that reports a
        // percentage of the cases it picked for itself is the cherry-picked denominator this whole
        // entry exists to refuse.
        var script = Harness();

        Assert.Contains("catalogue cases were executed", script);
        Assert.Contains("awaiting triage", script);
    }

    // ---------------------------------------------------------------- honesty

    [Fact]
    public void A_human_only_case_says_why_it_needs_a_human()
    {
        // "Requires a human" without a reason rots into "we never got round to it", and the two are
        // different facts. Este's goal is MOST of the list run by an agent — the remainder has to stay
        // named, or the gap stops being legible the moment someone new reads it.
        foreach (var c in Cases().Where(c => Str(c, "coverage") == "human"))
            Assert.False(string.IsNullOrWhiteSpace(Str(c, "humanReason")), $"{Str(c, "id")} has no humanReason");
    }

    [Fact]
    public void Nothing_claims_verified_without_saying_when_and_by_whom()
    {
        // The whole reason this file exists. "Verified" with no provenance is exactly the unchecked-box
        // ambiguity moved into a nicer format.
        foreach (var c in Cases().Where(c => Str(c, "status") == "verified"))
        {
            var id = Str(c, "id");
            Assert.True(c.TryGetProperty("lastVerified", out var lv) && lv.ValueKind == JsonValueKind.Object,
                $"{id} is verified with no lastVerified");
            foreach (var field in new[] { "release", "date", "by" })
                Assert.False(string.IsNullOrWhiteSpace(Str(lv, field)), $"{id}.lastVerified.{field} is empty");
        }
    }

    [Fact]
    public void An_executable_case_says_what_it_does_and_what_must_be_observable()
    {
        // A harness case is the spec the script implements. Without an expectation in words, the only
        // definition of what it checks is the assertion itself, and then the test can never be wrong.
        foreach (var c in Cases().Where(c => Str(c, "coverage") == "harness"))
        {
            var id = Str(c, "id");
            Assert.False(string.IsNullOrWhiteSpace(Str(c, "action")), $"{id} has no action");
            Assert.False(string.IsNullOrWhiteSpace(Str(c, "expect")), $"{id} has no expect");
        }
    }

    [Fact]
    public void An_untriaged_case_still_carries_its_steps_and_where_it_came_from()
    {
        // Untriaged means "nobody has walked it with Este yet", not "empty". A placeholder with no
        // steps would quietly drop coverage on the floor while still counting in the total.
        foreach (var c in Cases().Where(c => Str(c, "coverage") == "untriaged"))
        {
            var id = Str(c, "id");
            Assert.True(c.GetProperty("steps").GetArrayLength() > 0, $"{id} carries no steps");
            Assert.Contains("pending.md", Str(c, "source"));
        }
    }

    [Fact]
    public void The_gap_is_never_empty_and_never_silent()
    {
        // If this ever goes to zero it is far likelier that someone deleted the awkward entries than
        // that a harness learned to launch a game and play it.
        var human = Cases().Count(c => Str(c, "coverage") == "human");

        Assert.True(human > 0, "no human-only cases — that claim needs more evidence than a green suite");
    }

    [Fact]
    public void The_ban_risk_gate_is_permanently_human_only()
    {
        // The one law with no dev bypass. An agent must be able to REACH the gate and confirm it
        // refuses, and must never be able to SATISFY it — an agent that can tick the acknowledgement
        // has deleted the law rather than honoured it. Pinned here so no future coverage sweep can
        // quietly reclassify it as automatable.
        var gate = Assert.Single(Cases(), c => Str(c, "id") == "ban-risk-ack-gate");

        Assert.Equal("human", Str(gate, "coverage"));
        Assert.Contains("never satisfy", Str(gate, "humanReason"), StringComparison.OrdinalIgnoreCase);
    }
}
