using ModManager.Core;

namespace ModManager.Tests;

// MP-safety core: infer multiplayer risk from a mod's class, let a real user override win,
// and persist per-mod overrides tolerantly (corrupt/unknown values never poison the map).
public class MpCompatTests
{
    private static string TmpDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "mmb-mpc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    // ---- Infer -------------------------------------------------------------

    [Fact]
    public void Infer_gameplay_is_risky()
        => Assert.Equal(MpRisk.Risky, MpCompat.Infer("gameplay"));

    [Theory]
    [InlineData("graphics")]
    [InlineData("display")]
    [InlineData("upscaler")]
    [InlineData("co-op")]
    public void Infer_visual_and_coop_classes_are_safe(string modClass)
        => Assert.Equal(MpRisk.Safe, MpCompat.Infer(modClass));

    [Fact]
    public void Infer_is_case_insensitive()
        => Assert.Equal(MpRisk.Risky, MpCompat.Infer("GamePlay"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("both")]
    [InlineData("dll")]
    [InlineData("tweak")]
    [InlineData("sp")]
    [InlineData("mp")]
    public void Infer_unrecognized_is_unknown(string? modClass)
        => Assert.Equal(MpRisk.Unknown, MpCompat.Infer(modClass));

    // ---- InferFromText (readme / description) ------------------------------
    // The honest default is "MP?". The one place we promote past it without guessing is when
    // the author states compatibility outright in the readme/description. Explicit only.

    [Theory]
    [InlineData("This mod is multiplayer safe.")]
    [InlineData("Works in multiplayer with friends.")]
    [InlineData("Co-op compatible, tested on a dedicated server.")]
    [InlineData("Fully server-side, safe for multiplayer.")]
    public void InferFromText_explicit_mp_safe_is_safe(string text)
        => Assert.Equal(MpRisk.Safe, MpCompat.InferFromText(text));

    [Theory]
    [InlineData("Single player only.")]
    [InlineData("This is singleplayer only, will not work in multiplayer.")]
    [InlineData("Multiplayer not supported.")]
    public void InferFromText_explicit_sp_only_is_sponly(string text)
        => Assert.Equal(MpRisk.SpOnly, MpCompat.InferFromText(text));

    [Theory]
    [InlineData("Using this online may get you banned.")]
    [InlineData("Triggers Easy Anti-Cheat, do not use in multiplayer.")]
    [InlineData("This will get you banned on official servers.")]
    public void InferFromText_ban_or_anticheat_warnings_are_risky(string text)
        => Assert.Equal(MpRisk.Risky, MpCompat.InferFromText(text));

    [Fact]
    public void InferFromText_is_case_insensitive()
        => Assert.Equal(MpRisk.Safe, MpCompat.InferFromText("MULTIPLAYER SAFE"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Adds faster ships and more loot.")]
    [InlineData("Hang the banner over the abandoned urban district.")] // no bare "ban" false-positive
    public void InferFromText_no_explicit_claim_is_unknown(string? text)
        => Assert.Equal(MpRisk.Unknown, MpCompat.InferFromText(text));

    // ---- InferAll (readme outranks class) ----------------------------------

    [Fact]
    public void InferAll_readme_safe_beats_class_risky()
        => Assert.Equal(MpRisk.Safe, MpCompat.InferAll("gameplay", "This mod is multiplayer safe."));

    [Fact]
    public void InferAll_readme_sponly_beats_class_safe()
        => Assert.Equal(MpRisk.SpOnly, MpCompat.InferAll("graphics", "Single player only."));

    [Fact]
    public void InferAll_falls_back_to_class_when_text_silent()
        => Assert.Equal(MpRisk.Risky, MpCompat.InferAll("gameplay", "Adds faster ships."));

    [Fact]
    public void InferAll_unknown_when_both_silent()
        => Assert.Equal(MpRisk.Unknown, MpCompat.InferAll("both", "Adds faster ships."));

    // ---- Effective ---------------------------------------------------------

    [Fact]
    public void Effective_safe_override_beats_inferred_risky()
        => Assert.Equal(MpRisk.Safe, MpCompat.Effective(MpRisk.Risky, MpRisk.Safe));

    [Fact]
    public void Effective_risky_override_beats_inferred_safe()
        => Assert.Equal(MpRisk.Risky, MpCompat.Effective(MpRisk.Safe, MpRisk.Risky));

    [Fact]
    public void Effective_sponly_override_wins()
        => Assert.Equal(MpRisk.SpOnly, MpCompat.Effective(MpRisk.Safe, MpRisk.SpOnly));

    [Fact]
    public void Effective_null_override_falls_back_to_inferred()
        => Assert.Equal(MpRisk.Risky, MpCompat.Effective(MpRisk.Risky, null));

    [Fact]
    public void Effective_unknown_override_falls_back_to_inferred()
        => Assert.Equal(MpRisk.Safe, MpCompat.Effective(MpRisk.Safe, MpRisk.Unknown));

    // ---- MpCompatStore -----------------------------------------------------

    [Fact]
    public void SetOverride_then_Load_returns_the_value()
    {
        var dir = TmpDir();
        try
        {
            MpCompatStore.SetOverride(dir, "coolmod", MpRisk.Risky);
            var map = MpCompatStore.Load(dir);
            Assert.Equal(MpRisk.Risky, map["coolmod"]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SetOverride_null_removes_the_key()
    {
        var dir = TmpDir();
        try
        {
            MpCompatStore.SetOverride(dir, "coolmod", MpRisk.Risky);
            MpCompatStore.SetOverride(dir, "coolmod", null);
            Assert.DoesNotContain("coolmod", MpCompatStore.Load(dir).Keys);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SetOverride_unknown_removes_the_key()
    {
        var dir = TmpDir();
        try
        {
            MpCompatStore.SetOverride(dir, "coolmod", MpRisk.Risky);
            MpCompatStore.SetOverride(dir, "coolmod", MpRisk.Unknown);
            Assert.DoesNotContain("coolmod", MpCompatStore.Load(dir).Keys);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_missing_file_is_empty()
    {
        var dir = TmpDir();
        try { Assert.Empty(MpCompatStore.Load(dir)); }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_invalid_json_is_empty()
    {
        var dir = TmpDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "mp-compat.json"), "{ not valid json ]");
            Assert.Empty(MpCompatStore.Load(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_skips_bogus_values_keeps_valid_ones()
    {
        var dir = TmpDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "mp-compat.json"),
                "{ \"x\": \"Banana\", \"y\": \"SpOnly\", \"z\": \"Unknown\" }");
            var map = MpCompatStore.Load(dir);
            Assert.DoesNotContain("x", map.Keys);   // bogus value skipped
            Assert.DoesNotContain("z", map.Keys);   // explicit Unknown skipped
            Assert.Equal(MpRisk.SpOnly, map["y"]);  // valid entry kept
            Assert.Single(map);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Round_trips_multiple_keys()
    {
        var dir = TmpDir();
        try
        {
            MpCompatStore.SetOverride(dir, "coolmod", MpRisk.Risky);
            MpCompatStore.SetOverride(dir, "fancyhud", MpRisk.SpOnly);
            MpCompatStore.SetOverride(dir, "shaderpack", MpRisk.Safe);

            var map = MpCompatStore.Load(dir);
            Assert.Equal(3, map.Count);
            Assert.Equal(MpRisk.Risky, map["coolmod"]);
            Assert.Equal(MpRisk.SpOnly, map["fancyhud"]);
            Assert.Equal(MpRisk.Safe, map["shaderpack"]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SetOverride_persists_enum_names_as_strings()
    {
        var dir = TmpDir();
        try
        {
            MpCompatStore.SetOverride(dir, "coolmod", MpRisk.Risky);
            var raw = File.ReadAllText(Path.Combine(dir, "mp-compat.json"));
            Assert.Contains("\"Risky\"", raw);   // human-readable name, not a number
            Assert.DoesNotContain("\"coolmod\": 2", raw);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
