using ModManager.Core;

namespace ModManager.Tests;

// Ports core-purity.test.js — operating law #4: the core stays UI-free. The JS guard asserts
// the cores never import electron; the C# analog asserts ModManager.Core references no UI
// framework (WinUI / Windows App SDK / the App project / WPF / WinForms / MAUI).
public class CorePurityTests
{
    private static readonly string[] Forbidden =
    {
        "Microsoft.UI", "Microsoft.WinUI", "WinRT", "Microsoft.Windows.SDK", "Microsoft.Windows.ApplicationModel",
        "ModManager.App", "PresentationFramework", "PresentationCore", "WindowsBase", "System.Windows.Forms", "Microsoft.Maui",
    };

    private static IEnumerable<string> Offenders(IEnumerable<string> referencedNames)
        => referencedNames.Where(n => Forbidden.Any(f => n.StartsWith(f, StringComparison.OrdinalIgnoreCase)));

    [Fact]
    public void Core_assembly_references_no_ui_frameworks()
    {
        var core = typeof(Scanner).Assembly;
        var names = core.GetReferencedAssemblies().Select(a => a.Name ?? "");
        Assert.Empty(Offenders(names));
    }

    [Fact]
    public void Guard_targets_the_real_core_and_is_not_vacuous()
    {
        // The guarded assembly is the real core (holds Scanner + the pure cores).
        var types = typeof(Scanner).Assembly.GetTypes().Select(t => t.Name).ToHashSet();
        Assert.Contains("Scanner", types);
        Assert.Contains("Fingerprint", types);
        Assert.Contains("CurseForgeRequests", types);

        // And the detector actually flags a UI reference when one is present (not a no-op).
        Assert.Equal(new[] { "Microsoft.UI.Xaml" },
            Offenders(new[] { "System.Text.Json", "Microsoft.UI.Xaml", "ModManager.Core" }).ToArray());
    }
}
