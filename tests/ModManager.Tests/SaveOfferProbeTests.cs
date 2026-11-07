using ModManager.Core;
using ModManager.Core.SaveEditor.FromSoft;

namespace ModManager.Tests;

/// <summary>The save-type offer's disk side: what each save holds, read target first, never throwing.</summary>
public class SaveOfferProbeTests : IDisposable
{
    private readonly string _dir = TestSupport.TempDir("mmb-saveoffer-");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static CharacterSlot Slot(int i, string name, int level)
        => new(i, name, "Vagabond", level, 0, 10, 10, 10, 10, 10, 10, 10, 10, "76561197969211145");

    private static readonly LaunchTarget Steam = new("Steam", "steam", "steam://rungameid/1245620");

    private static GameEntry EldenRing(string? engine = "fromsoft")
        => new() { Id = "elden-ring", GameName = "ELDEN RING", Engine = engine, SteamAppId = "1245620" };

    private void Write(string name) => File.WriteAllText(Path.Combine(_dir, name), name);

    // Fake reader keyed by file name: the real one needs a 28 MB BND4 save.
    private static Func<string, IReadOnlyList<CharacterSlot>> Reader(Dictionary<string, CharacterSlot[]> byName)
        => path => byName.TryGetValue(Path.GetFileName(path), out var s) ? s : throw new InvalidDataException("not a save");

    [Fact]
    public void A_missing_file_reads_as_absent_with_no_characters()
    {
        var f = SaveOfferProbe.Read(_dir, "ER0000.sl2", _ => throw new Exception("never called"));
        Assert.False(f.Exists);
        Assert.Equal(0, f.Characters);
    }

    [Fact]
    public void A_reader_failure_reads_as_unknown()
    {
        Write("ER0000.sl2");
        Assert.Null(SaveOfferProbe.Read(_dir, "ER0000.sl2", _ => throw new InvalidDataException()).Characters);
    }

    [Fact]
    public void The_first_character_by_slot_carries_its_name_and_level()
    {
        Write("ER0000.co2");
        var f = SaveOfferProbe.Read(_dir, "ER0000.co2", _ => new[] { Slot(2, "Second", 40), Slot(0, "Tarnished", 112) });
        Assert.Equal(2, f.Characters);
        Assert.Equal("Tarnished", f.FirstCharacter);
        Assert.Equal(112, f.FirstLevel);
    }

    [Fact]
    public void No_save_folder_means_no_offer()
    {
        var reader = Reader(new());
        Assert.Null(SaveOfferProbe.For(EldenRing(), null, LaunchMode.Vanilla, Steam, reader));
        Assert.Null(SaveOfferProbe.For(EldenRing(), Path.Combine(_dir, "nope"), LaunchMode.Vanilla, Steam, reader));
    }

    [Fact]
    public void An_empty_sl2_beside_a_co2_with_characters_offers_the_co2()
    {
        Write("ER0000.sl2");
        Write("ER0000.co2");
        var reader = Reader(new() { ["ER0000.sl2"] = Array.Empty<CharacterSlot>(), ["ER0000.co2"] = new[] { Slot(0, "Tarnished", 112) } });
        var plan = SaveOfferProbe.For(EldenRing(), _dir, LaunchMode.Vanilla, Steam, reader)!;
        Assert.Equal(".sl2", plan.Target.Extension);
        Assert.Equal(".co2", plan.Sources[0].Type.Extension);
        Assert.Equal("Tarnished", plan.Sources[0].File.FirstCharacter);
    }

    [Fact]
    public void Backup_copies_beside_the_saves_are_never_sources()
    {
        // Este's folder carries "ER0000 - Copy.co2.bak" and ER0000.co2.bak: neither is base + a declared extension.
        Write("ER0000.sl2");
        Write("ER0000.co2.bak");
        Write("ER0000 - Copy.co2.bak");
        var reader = Reader(new() { ["ER0000.sl2"] = Array.Empty<CharacterSlot>() });
        Assert.Null(SaveOfferProbe.For(EldenRing(), _dir, LaunchMode.Vanilla, Steam, reader));
    }

    [Fact]
    public void A_dismissed_target_is_not_offered()
    {
        Write("ER0000.co2");
        var game = EldenRing();
        game.SaveOfferDismissed = new[] { ".sl2" };
        var reader = Reader(new() { ["ER0000.co2"] = new[] { Slot(0, "Tarnished", 112) } });
        Assert.Null(SaveOfferProbe.For(game, _dir, LaunchMode.Vanilla, Steam, reader));
    }

    [Fact]
    public void A_non_fromsoft_engine_never_offers()
    {
        Write("ER0000.co2");
        var reader = Reader(new() { ["ER0000.co2"] = new[] { Slot(0, "Tarnished", 112) } });
        Assert.Null(SaveOfferProbe.For(EldenRing("ue-pak"), _dir, LaunchMode.Vanilla, Steam, reader));
    }
}
