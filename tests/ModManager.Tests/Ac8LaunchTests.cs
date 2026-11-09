using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests;

// ACE COMBAT 8 (Steam 2288340). Unreal 5.4 behind EasyAntiCheat (start_protected_game.exe) and Denuvo.
// Measured 2026-11-08: Steam's normal Play runs EAC, which refuses to start with UE4SS's dwmapi.dll present
// ("Untrusted system file"). The modded way in is the real exe run directly with -SaveToUserDir, the args
// Bandai's own internal "ACE COMBAT 8 No EAC" Steam launch config uses. Run without SteamAppId in its
// environment the game exits mid-init, so the target has to carry it.
public class Ac8LaunchTests
{
    private const string AppId = "2288340";

    [Fact]
    public void Ac8_has_an_internal_modded_launch_that_skips_anti_cheat_and_carries_the_steam_app_id()
    {
        var opt = Assert.Single(LaunchOptions.For(AppId));
        Assert.Equal(LaunchOptionKind.Internal, opt.Kind);
        Assert.Equal(@"Game\Binaries\Win64\AceCombat8.exe", opt.Exe);
        Assert.Equal("-SaveToUserDir", opt.Args);
        Assert.Equal(@"Game\Binaries\Win64", opt.WorkingSubdir);
        Assert.True(opt.SkipsAntiCheat);
        Assert.Equal(AppId, opt.Env!["SteamAppId"]);
        Assert.Equal(AppId, opt.Env!["SteamGameId"]);
        Assert.Contains("offline", opt.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_makes_the_modded_target_the_default_and_keeps_Steam_as_the_vanilla_way_in()
    {
        var root = Path.Combine(Path.GetTempPath(), "ac8-launch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var win64 = Path.Combine(root, "Game", "Binaries", "Win64");
            Directory.CreateDirectory(win64);
            File.WriteAllText(Path.Combine(win64, "AceCombat8.exe"), "");

            var targets = LaunchScan.Detect(root, "ue-pak", AppId).Targets;

            var modded = Assert.Single(targets, t => t.Kind == "exe");
            Assert.True(modded.IsDefault);
            Assert.True(modded.SkipsAntiCheat);
            Assert.Equal(Path.Combine(root, @"Game\Binaries\Win64\AceCombat8.exe"), modded.Target);
            Assert.Equal(Path.Combine(root, @"Game\Binaries\Win64"), modded.WorkingDir);
            Assert.Equal(AppId, modded.Env!["SteamAppId"]);

            var steam = Assert.Single(targets, t => t.Kind == "steam");
            Assert.False(steam.IsDefault);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Detect_offers_no_modded_target_when_the_exe_is_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "ac8-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var targets = LaunchScan.Detect(root, "ue-pak", AppId).Targets;
            Assert.DoesNotContain(targets, t => t.Kind == "exe");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Vanilla_never_picks_a_target_that_skips_anti_cheat_even_with_no_Steam_target()
    {
        var noEac = new LaunchTarget("Play modded (offline)", "exe", @"C:\AC8\Game\Binaries\Win64\AceCombat8.exe")
        {
            IsDefault = true, SkipsAntiCheat = true,
        };
        var game = new GameEntry { Id = "ac8", GameName = "ACE COMBAT 8", LaunchTargets = new[] { noEac } };

        Assert.Null(LaunchTargetPicker.Pick(game, LaunchMode.Vanilla, seamlessFullyInstalled: false));
        Assert.Same(noEac, LaunchTargetPicker.Pick(game, LaunchMode.Modded, seamlessFullyInstalled: false));
    }

    [Fact]
    public void Launch_target_env_and_skipsAntiCheat_round_trip_as_camelCase_with_env_names_kept_verbatim()
    {
        var game = new GameEntry
        {
            Id = "ac8",
            LaunchTargets = new[]
            {
                new LaunchTarget("Play modded (offline)", "exe", "AceCombat8.exe")
                {
                    SkipsAntiCheat = true,
                    Env = new Dictionary<string, string> { ["SteamAppId"] = AppId },
                },
            },
        };
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var json = JsonSerializer.Serialize(game, opts);
        Assert.Contains("\"skipsAntiCheat\":true", json);
        Assert.Contains("\"env\":{\"SteamAppId\":\"2288340\"}", json); // the variable name is the OS's, not ours
        Assert.DoesNotContain("\"SkipsAntiCheat\"", json);

        var back = JsonSerializer.Deserialize<GameEntry>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var t = Assert.Single(back.LaunchTargets);
        Assert.True(t.SkipsAntiCheat);
        Assert.Equal(AppId, t.Env!["SteamAppId"]);
    }

    [Fact]
    public void A_target_without_env_omits_both_fields_so_existing_registries_stay_byte_shaped()
    {
        var game = new GameEntry { Id = "x", LaunchTargets = new[] { new LaunchTarget("Play", "steam", "steam://rungameid/1") } };
        var json = JsonSerializer.Serialize(game, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.DoesNotContain("\"env\"", json);
        Assert.DoesNotContain("skipsAntiCheat", json);
    }
}
