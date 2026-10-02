using ModManager.Core;
using Xunit;

namespace ModManager.Tests;

// The sentence that says where a save-mod write's undo lives. The agent (MCP) and the Saves dialog both say it, so
// it lives once, here, and neither can drift.
public class SaveModUndoNoteTests
{
    [Fact]
    public void UndoNote_keeps_the_exact_wording_the_agent_has_always_returned()
    {
        // The MCP's SnapshotNote text before the helper existed. Pinned so moving it changed nothing.
        const string dir = @"C:\data\saves\save-mods\worlds\ABC";
        const string world = @"C:\profiles\Worlds\ABC";

        var note = SaveModSnapshots.UndoNote(dir, world);

        Assert.Equal($"626 snapshots this world first, into {dir}. Saves doesn't list those: to undo, unzip the newest one "
                     + $"into {world}.", note);
    }

    [Fact]
    public void UndoNote_ends_with_a_period_and_has_no_em_dash()
    {
        var note = SaveModSnapshots.UndoNote("a", "b");

        Assert.EndsWith(".", note);
        Assert.DoesNotContain("\u2014", note);
    }
}
