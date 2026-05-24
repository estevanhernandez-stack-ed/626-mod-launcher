using System.Text.Json;
using System.Text.Json.Nodes;
using ModManager.Core;

namespace ModManager.Tests;

// Ports fs-atomic.test.js — small JSON state files must never be left half-written.
public class AtomicJsonTests
{
    private static string TmpDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fsatomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Writes_camelcase_property_names_for_cross_app_compat()
    {
        // The data files are shared with the Electron app, which uses camelCase. C# must match.
        var dir = TmpDir();
        var file = Path.Combine(dir, "meta.json");
        AtomicJson.WriteJsonAtomic(file, new ModMeta { Title = "X", CurseforgeId = 5 });

        var raw = File.ReadAllText(file);
        Assert.Contains("\"title\"", raw);
        Assert.Contains("\"curseforgeId\"", raw);
        Assert.DoesNotContain("\"Title\"", raw);
        Assert.DoesNotContain("\"CurseforgeId\"", raw);
    }

    [Fact]
    public void Writes_json_content_to_a_new_file()
    {
        var dir = TmpDir();
        var file = Path.Combine(dir, "games.json");
        AtomicJson.WriteJsonAtomic(file, new { version = 1, games = new[] { "a", "b" } });

        var node = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Equal(1, (int)node["version"]!);
        Assert.Equal(new[] { "a", "b" }, node["games"]!.AsArray().Select(n => (string)n!).ToArray());
    }

    [Fact]
    public void Fully_replaces_an_existing_file()
    {
        var dir = TmpDir();
        var file = Path.Combine(dir, "games.json");
        AtomicJson.WriteJsonAtomic(file, new { big = new string('x', 500) });
        AtomicJson.WriteJsonAtomic(file, new { small = 1 });

        var node = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Null(node["big"]);
        Assert.Equal(1, (int)node["small"]!);
    }

    [Fact]
    public void Leaves_no_temp_file_on_success()
    {
        var dir = TmpDir();
        AtomicJson.WriteJsonAtomic(Path.Combine(dir, "games.json"), new { ok = true });
        Assert.Equal(new[] { "games.json" }, Directory.GetFiles(dir).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void Failed_serialization_leaves_existing_file_intact()
    {
        var dir = TmpDir();
        var file = Path.Combine(dir, "games.json");
        AtomicJson.WriteJsonAtomic(file, new { version = 1, games = new[] { "keep-me" } });

        var circular = new Cyclic();
        circular.Self = circular; // System.Text.Json throws on a cycle
        Assert.ThrowsAny<JsonException>(() => AtomicJson.WriteJsonAtomic(file, circular));

        // P0-1 guarantee: the pre-existing file is byte-for-byte untouched, no orphaned temp.
        var node = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Equal(1, (int)node["version"]!);
        Assert.Equal(new[] { "keep-me" }, node["games"]!.AsArray().Select(n => (string)n!).ToArray());
        Assert.Equal(new[] { "games.json" }, Directory.GetFiles(dir).Select(Path.GetFileName).ToArray());
    }

    private sealed class Cyclic
    {
        public Cyclic? Self { get; set; }
    }
}
