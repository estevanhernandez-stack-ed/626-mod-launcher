using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// When a launch should offer a save from another mode (spec 2026-11-07-save-type-offer-design). Found on the
/// live vanilla smoke for PR #407: every character lived in .co2 or .err, vanilla read an empty .sl2, and it
/// looked like 626 had lost the saves.
/// </summary>
public class SaveOfferTests
{
    private static readonly SaveType Sl2 = new(".sl2", "Vanilla", "vanilla");
    private static readonly SaveType Co2 = new(".co2", "Seamless Co-op", "coop");
    private static readonly SaveType Err = new(".err", "Reforged", "overhaul");
    private static readonly SaveType Mod = new(".mod", "Alt Saves", "overhaul");
    private static readonly IReadOnlyList<SaveType> Types = new[] { Sl2, Co2, Err, Mod };
    private static readonly DateTime Jan6 = new(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Sep3 = new(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

    private static Func<string, SaveFileFacts> F(params SaveFileFacts[] files)
        => name => files.FirstOrDefault(f => f.FileName == name) ?? new SaveFileFacts(name, false, 0, default);

    private static SaveOfferPlan? Eval(string? target, Func<string, SaveFileFacts> f, string[]? dismissed = null,
        bool refused = false, SaveLayout layout = SaveLayout.TypedFiles)
        => SaveOffer.Evaluate(Types, layout, target, f, "ER0000", dismissed ?? Array.Empty<string>(), refused);

    private static readonly SaveFileFacts EmptySl2 = new("ER0000.sl2", true, 0, Sep3);
    private static readonly SaveFileFacts Co2With3 = new("ER0000.co2", true, 3, Sep3, "Tarnished", 112);
    private static readonly SaveFileFacts ErrWith1 = new("ER0000.err", true, 1, Jan6, "Rogier", 80);

    [Fact]
    public void Offers_co2_into_an_empty_sl2_with_a_warning()
    {
        var p = Eval(".sl2", F(EmptySl2, Co2With3))!;
        Assert.Equal(".sl2", p.Target.Extension);
        Assert.Equal("ER0000.sl2", p.TargetFileName);
        Assert.True(p.TargetExists);
        var s = Assert.Single(p.Sources);
        Assert.Equal(".co2", s.Type.Extension);
        Assert.Equal(SaveOfferWarning.Warn, s.Warning);
    }

    [Fact]
    public void Offers_when_the_target_does_not_exist()
        => Assert.False(Eval(".sl2", F(Co2With3))!.TargetExists);

    [Fact]
    public void No_offer_when_the_target_has_a_character()
        => Assert.Null(Eval(".sl2", F(EmptySl2 with { Characters = 1 }, Co2With3)));

    [Fact]
    public void No_offer_when_no_source_has_characters()
        => Assert.Null(Eval(".sl2", F(EmptySl2, Co2With3 with { Characters = 0 })));

    [Fact]
    public void An_unreadable_target_means_no_offer()
        => Assert.Null(Eval(".sl2", F(EmptySl2 with { Characters = null }, Co2With3)));

    [Fact]
    public void An_unreadable_source_is_never_offered()
        => Assert.Equal(new[] { ".err" },
            Eval(".sl2", F(EmptySl2, Co2With3 with { Characters = null }, ErrWith1))!.Sources.Select(s => s.Type.Extension));

    [Fact]
    public void Newest_source_is_first_and_an_overhaul_needs_acknowledgment()
    {
        var p = Eval(".sl2", F(EmptySl2, ErrWith1, Co2With3))!;
        Assert.Equal(new[] { ".co2", ".err" }, p.Sources.Select(s => s.Type.Extension));
        Assert.Equal(SaveOfferWarning.Acknowledge, p.Sources[1].Warning);
    }

    [Fact]
    public void Into_a_modded_type_carries_no_warning()
    {
        var p = Eval(".err", F(new SaveFileFacts("ER0000.sl2", true, 2, Sep3), new SaveFileFacts("ER0000.err", true, 0, Jan6)))!;
        Assert.All(p.Sources, s => Assert.Equal(SaveOfferWarning.None, s.Warning));
    }

    [Fact]
    public void Dismissed_refused_unknown_target_and_worlds_layout_mean_no_offer()
    {
        Assert.Null(Eval(".sl2", F(EmptySl2, Co2With3), dismissed: new[] { ".sl2" }));
        Assert.Null(Eval(".sl2", F(EmptySl2, Co2With3), dismissed: new[] { ".SL2" }));
        Assert.Null(Eval(".sl2", F(EmptySl2, Co2With3), refused: true));
        Assert.Null(Eval(null, F(EmptySl2, Co2With3)));
        Assert.Null(Eval(".xyz", F(EmptySl2, Co2With3)));
        Assert.Null(Eval(".sl2", F(EmptySl2, Co2With3), layout: SaveLayout.Worlds));
    }

    [Fact]
    public void Dismissal_is_per_target_type()
        => Assert.NotNull(Eval(".co2", F(new SaveFileFacts("ER0000.sl2", true, 2, Sep3)), dismissed: new[] { ".sl2" }));

    [Fact]
    public void Only_the_base_name_plus_each_declared_extension_is_read()
    {
        var asked = new List<string>();
        Eval(".sl2", n => { asked.Add(n); return new SaveFileFacts(n, false, 0, default); });
        Assert.Equal(new[] { "ER0000.sl2", "ER0000.co2", "ER0000.err", "ER0000.mod" }, asked);
    }

    [Fact]
    public void A_target_with_characters_stops_before_reading_any_source()
    {
        var asked = new List<string>();
        Eval(".sl2", n => { asked.Add(n); return new SaveFileFacts(n, true, n.EndsWith(".sl2") ? 1 : 3, Sep3); });
        Assert.Equal(new[] { "ER0000.sl2" }, asked);
    }

    [Fact]
    public void An_unknown_kind_reads_as_the_most_cautious()
        => Assert.Equal(SaveOfferWarning.Acknowledge, SaveOffer.WarningFor(new SaveType(".new", "New", "somethingNew"), Sl2));

    [Fact]
    public void A_missing_kind_reads_as_the_most_cautious()
        => Assert.Equal(SaveOfferWarning.Acknowledge, SaveOffer.WarningFor(new SaveType(".new", "New"), Sl2));

    [Fact]
    public void Target_extension_by_launch()
    {
        var steam = new LaunchTarget("Steam", "steam", "steam://rungameid/1245620");
        Assert.Equal(".sl2", SaveOffer.TargetExtension(LaunchMode.Vanilla, steam, null, Types));
        Assert.Equal(".co2", SaveOffer.TargetExtension(LaunchMode.Modded,
            new LaunchTarget("Seamless", "exe", @"C:\g\Game\ersc_launcher.exe"), null, Types));
        Assert.Equal(".err", SaveOffer.TargetExtension(LaunchMode.Modded,
            new LaunchTarget("Reforged", "exe", @"D:\ERR\internals\launcher\ReforgedLauncher.exe"),
            new ReforgedLaunchInfo(@"D:\ERR", false, ".err"), Types));
        Assert.Null(SaveOffer.TargetExtension(LaunchMode.Modded, new LaunchTarget("Alt", "exe", @"D:\ERR\x.bat"),
            new ReforgedLaunchInfo(@"D:\ERR", true, ".err"), Types));
        Assert.Null(SaveOffer.TargetExtension(LaunchMode.Modded, new LaunchTarget("Ambiguous", "exe", @"D:\ERR\x.bat"),
            new ReforgedLaunchInfo(@"D:\ERR", false, null), Types));
        Assert.Null(SaveOffer.TargetExtension(LaunchMode.Modded,
            new LaunchTarget("ME2", "exe", @"C:\g\modengine2_launcher.exe"), null, Types));
        Assert.Null(SaveOffer.TargetExtension(LaunchMode.Modded, steam, null, Types));
    }

    [Fact]
    public void A_reforged_extension_the_game_does_not_declare_is_not_a_target()
        => Assert.Null(SaveOffer.TargetExtension(LaunchMode.Modded, new LaunchTarget("R", "exe", @"D:\ERR\a.bat"),
            new ReforgedLaunchInfo(@"D:\ERR", false, ".rfg"), Types));
}
