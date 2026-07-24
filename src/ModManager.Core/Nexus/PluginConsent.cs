namespace ModManager.Core.Nexus;

/// <summary>
/// The first-plugin-download consent rule as a pure, testable predicate. The launcher must get explicit
/// user consent before the FIRST plugin install (connect Nexus + download the signed add-on); once any
/// plugin is installed, later updates are silent and never re-prompt. Kept in Core so the rule the App
/// wiring honors is pinned by a unit test rather than mirrored inline.
/// </summary>
public static class PluginConsent
{
    /// <summary>True iff no plugin is installed yet — the only time first-install consent is required.</summary>
    public static bool NeedsFirstInstallConsent(int installedCount) => installedCount == 0;
}
