using ModManager.Core;

namespace ModManager.Tests;

// Pure load-order model: reconcile a saved order against the live mod list, and move entries.
// Engine-specific "apply" (plugins.txt / pak prefixes) is separate; this is just the ordering.
public class LoadOrderTests
{
    [Fact]
    public void Reconcile_keeps_saved_order_appends_new_drops_missing()
    {
        var saved = new[] { "b", "a", "gone" };       // 'gone' no longer installed
        var current = new[] { "a", "b", "c" };          // 'c' is new
        var order = LoadOrder.Reconcile(saved, current);
        Assert.Equal(new[] { "b", "a", "c" }, order.ToArray());
    }

    [Fact]
    public void Reconcile_with_no_saved_order_is_current_order()
        => Assert.Equal(new[] { "a", "b" }, LoadOrder.Reconcile(null, new[] { "a", "b" }).ToArray());

    [Fact]
    public void Move_up_and_down()
    {
        var o = new[] { "a", "b", "c" };
        Assert.Equal(new[] { "b", "a", "c" }, LoadOrder.Move(o, "b", -1).ToArray());
        Assert.Equal(new[] { "a", "c", "b" }, LoadOrder.Move(o, "b", +1).ToArray());
    }

    [Fact]
    public void Move_clamps_at_the_ends()
    {
        var o = new[] { "a", "b", "c" };
        Assert.Equal(new[] { "a", "b", "c" }, LoadOrder.Move(o, "a", -1).ToArray()); // already top
        Assert.Equal(new[] { "a", "b", "c" }, LoadOrder.Move(o, "c", +5).ToArray()); // already bottom
    }

    [Fact]
    public void Move_unknown_key_is_a_noop()
        => Assert.Equal(new[] { "a", "b" }, LoadOrder.Move(new[] { "a", "b" }, "z", -1).ToArray());
}
