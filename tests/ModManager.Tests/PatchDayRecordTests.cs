using ModManager.Core;
using ModManager.Core.Persistence;

namespace ModManager.Tests;

public class PatchDayRecordTests
{
    [Fact]
    public void PatchDay_round_trips_as_camelCase()
    {
        var dir = TestSupport.TempDir("regstore-patchday-");
        var detected = new DateTime(2026, 10, 16, 8, 30, 0, DateTimeKind.Utc);
        var record = new PatchDayRecord
        {
            BuildId = "20260001",
            DetectedUtc = detected,
            RestorePointId = "rp-1",
            RestorePointError = "disk full",
            Choice = PatchDayChoice.Modded,
            SnoozedUntilChange = true,
        };
        var reg = new GameRegistry { Games = { new GameEntry { Id = "g1", GameName = "G1", PatchDay = record } } };

        RegistryStore.Save(dir, reg);

        var json = File.ReadAllText(RegistryStore.PathFor(dir));
        foreach (var key in new[] { "patchDay", "buildId", "detectedUtc", "restorePointId", "restorePointError", "choice", "snoozedUntilChange" })
            Assert.Contains($"\"{key}\"", json);
        foreach (var key in new[] { "PatchDay", "BuildId", "DetectedUtc", "RestorePointId", "RestorePointError", "Choice", "SnoozedUntilChange" })
            Assert.DoesNotContain($"\"{key}\"", json);
        Assert.Contains("\"choice\": \"modded\"", json);
        Assert.DoesNotContain("\"Modded\"", json);

        var back = RegistryStore.Load(dir).Games[0].PatchDay;
        Assert.NotNull(back);
        Assert.Equal("20260001", back!.BuildId);
        Assert.Equal(detected, back.DetectedUtc);
        Assert.Equal("rp-1", back.RestorePointId);
        Assert.Equal("disk full", back.RestorePointError);
        Assert.Equal(PatchDayChoice.Modded, back.Choice);
        Assert.True(back.SnoozedUntilChange);
    }

    [Theory]
    [InlineData(PatchDayChoice.None, "none")]
    [InlineData(PatchDayChoice.Vanilla, "vanilla")]
    public void Choice_serializes_as_a_camelCase_string(PatchDayChoice choice, string expected)
    {
        var dir = TestSupport.TempDir("regstore-patchday-choice-");
        var reg = new GameRegistry { Games = { new GameEntry { Id = "g1", GameName = "G1", PatchDay = new PatchDayRecord { BuildId = "1", Choice = choice } } } };

        RegistryStore.Save(dir, reg);

        Assert.Contains($"\"choice\": \"{expected}\"", File.ReadAllText(RegistryStore.PathFor(dir)));
        Assert.Equal(choice, RegistryStore.Load(dir).Games[0].PatchDay!.Choice);
    }

    [Fact]
    public void An_entry_without_the_record_has_no_patchDay_key_and_loads_null()
    {
        var dir = TestSupport.TempDir("regstore-patchday-none-");
        var reg = new GameRegistry { Games = { new GameEntry { Id = "g1", GameName = "G1" } } };

        RegistryStore.Save(dir, reg);

        var json = File.ReadAllText(RegistryStore.PathFor(dir));
        Assert.DoesNotContain("patchDay", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(RegistryStore.Load(dir).Games[0].PatchDay);
    }

    [Fact]
    public void Clone_carries_the_record()
    {
        var record = new PatchDayRecord { BuildId = "42", DetectedUtc = DateTime.UtcNow, Choice = PatchDayChoice.Vanilla };
        var original = new GameEntry { Id = "g1", GameName = "G1", PatchDay = record };

        var clone = original.CloneShallow();

        Assert.Equal(record, clone.PatchDay);
    }

    [Theory]
    [InlineData("\"somethingNew\"")]
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("{ \"x\": [1, 2] }")]
    public void An_unknown_or_non_string_choice_reads_as_None_and_keeps_the_registry(string rawChoice)
    {
        var dir = TestSupport.TempDir("regstore-patchday-unknown-");
        File.WriteAllText(RegistryStore.PathFor(dir),
            "{ \"games\": [ { \"id\": \"g1\", \"gameName\": \"G1\", \"patchDay\": { \"buildId\": \"9\", \"choice\": " + rawChoice + ", \"snoozedUntilChange\": true } } ] }");

        var reg = RegistryStore.Load(dir);

        var game = Assert.Single(reg.Games);
        Assert.Equal("g1", game.Id);
        Assert.Equal(PatchDayChoice.None, game.PatchDay!.Choice);
        Assert.Equal("9", game.PatchDay.BuildId);
        Assert.True(game.PatchDay.SnoozedUntilChange);
    }
}
