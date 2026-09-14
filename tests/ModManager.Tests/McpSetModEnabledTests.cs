using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Persistence;
using ModManager.Mcp;
using ModManager.Mcp.Tools;

namespace ModManager.Tests;

/// <summary>
/// The agent-access tool end to end, against a throwaway registry.
///
/// <para>On 2026-09-14 <c>set_mod_enabled</c> returned <c>ok</c> for four Elden Ring mods and moved
/// nothing: it sent every mod through the scanner's folder lane, and a direct-inject enable there is a
/// silent no-op. The same call on disable threw an index error. Both are asserted here through the tool
/// itself, because a router the tool does not call fixes nothing.</para>
/// </summary>
[Collection("McpDataRoot")]
public class McpSetModEnabledTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-mcp-toggle-");
    private readonly string _savedDataRoot = McpConfig.DataRoot;
    private string GameRoot => Path.Combine(_root, "ELDEN RING");
    private string Play => Path.Combine(GameRoot, "Game");

    public McpSetModEnabledTests()
    {
        McpConfig.DataRoot = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(Path.Combine(Play, "reshade-shaders"));
        File.WriteAllText(Path.Combine(Play, "reshade-shaders", "shader.fx"), "fx");
        File.WriteAllText(Path.Combine(Play, "ReShadePreset.ini"), "preset");
        File.WriteAllText(Path.Combine(Play, "eldenring.exe"), "game");

        var game = new GameEntry
        {
            // Engine fromsoft without a Mod Engine 2 config is the direct-inject lane. No Steam id, so
            // no ban-risk gate stands between the tool and the write this test is about.
            Id = "er-mcp", GameName = "ER", Engine = "fromsoft", GameRoot = GameRoot,
            DataDir = Path.Combine(_root, "data"),
        };
        RegistryStore.Save(McpConfig.DataRoot, Registry.UpsertGame(Registry.EmptyRegistry(), game));
    }

    public void Dispose()
    {
        McpConfig.DataRoot = _savedDataRoot;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static JsonElement Json(object result) => JsonSerializer.SerializeToElement(result);

    [Fact]
    public async Task Turning_a_direct_inject_mod_off_and_on_moves_its_files_and_reports_it()
    {
        var off = Json(await WriteTools.SetModEnabled("er-mcp", "ReShade", enabled: false));
        Assert.True(off.GetProperty("ok").GetBoolean(), off.ToString());
        Assert.False(File.Exists(Path.Combine(Play, "ReShadePreset.ini")));
        Assert.True(File.Exists(Path.Combine(Play, "eldenring.exe")));

        var on = Json(await WriteTools.SetModEnabled("er-mcp", "ReShade", enabled: true));
        Assert.True(on.GetProperty("ok").GetBoolean(), on.ToString());
        Assert.Equal("preset", File.ReadAllText(Path.Combine(Play, "ReShadePreset.ini")));
        Assert.Equal("fx", File.ReadAllText(Path.Combine(Play, "reshade-shaders", "shader.fx")));
    }

    [Fact]
    public async Task Asking_for_the_state_a_mod_is_already_in_is_still_ok()
    {
        // The post-write check compares the listing with what was asked, not with what it was before,
        // so a no-op request for the current state is correctly reported as applied.
        var on = Json(await WriteTools.SetModEnabled("er-mcp", "ReShade", enabled: true));
        Assert.True(on.GetProperty("ok").GetBoolean(), on.ToString());
        Assert.True(File.Exists(Path.Combine(Play, "ReShadePreset.ini")));
    }
}
