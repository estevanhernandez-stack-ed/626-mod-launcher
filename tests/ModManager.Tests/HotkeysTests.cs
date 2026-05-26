using ModManager.Core;

namespace ModManager.Tests;

public class HotkeysTests
{
    private static LuaKeyBind K(string key, params string[] mods) => new(key, mods);

    [Fact]
    public void Conflicts_flags_a_key_bound_more_than_once()
    {
        var c = Hotkeys.Conflicts(new[] { K("F3"), K("F4"), K("F3") });
        Assert.Contains(Hotkeys.Signature(K("F3")), c);
        Assert.DoesNotContain(Hotkeys.Signature(K("F4")), c);
    }

    [Fact]
    public void Conflicts_treats_modifiers_as_part_of_the_combo()
    {
        // Ctrl+Y and plain Y do NOT conflict; two Ctrl+Y do.
        var c = Hotkeys.Conflicts(new[] { K("Y", "CONTROL"), K("Y"), K("Y", "CONTROL") });
        Assert.Contains(Hotkeys.Signature(K("Y", "CONTROL")), c);
        Assert.DoesNotContain(Hotkeys.Signature(K("Y")), c);
    }

    [Fact]
    public void Signature_is_case_and_order_insensitive_for_modifiers()
        => Assert.Equal(Hotkeys.Signature(K("y", "shift", "control")), Hotkeys.Signature(K("Y", "CONTROL", "SHIFT")));

    [Fact]
    public void Conflicts_empty_when_all_unique() => Assert.Empty(Hotkeys.Conflicts(new[] { K("F1"), K("F2") }));
}
