namespace ModManager.Core;

// Wave 8. Three rules that share one habit: the app went quiet exactly where it had something useful
// to say. A capability it has, hidden. A state it knows, unlabelled. An action it can perform, offered
// as a link to somebody else's website.
//
// They live together because the fix is the same sentence three times: say what is true, and say what
// to press.

// -------------------------------------------------------------------------------------------------
// Item 3 — the in-app storefront stops vanishing
// -------------------------------------------------------------------------------------------------

/// <summary>What a user would have to do to make in-app browsing work here.</summary>
public enum BrowseRemedy
{
    /// <summary>Nothing — it works.</summary>
    None,
    /// <summary>Sign in to Nexus. One dialog away.</summary>
    ConnectNexus,
    /// <summary>Install or refresh the Nexus plugin. One Settings page away.</summary>
    InstallPlugin,
}

/// <param name="Show">Whether the control appears at all.</param>
/// <param name="Label">What it says. The capability goes in the LABEL, never only the hover.</param>
/// <param name="Detail">The tooltip / dialog sentence. Names the step when there is one.</param>
/// <param name="CanBrowse">True when pressing it opens the storefront; false when it opens a remedy.</param>
public sealed record BrowseAffordance(
    bool Show,
    string Label,
    string Detail,
    bool CanBrowse,
    BrowseRemedy Remedy);

/// <summary>
/// Whether to offer in-app mod browsing, and what to say when it is not available.
///
/// <para><b>The fault.</b> The button bound <c>CatalogVisibility</c> and disappeared entirely when the
/// capability was missing — collapsing three unrelated situations into the same nothing. Not signed in
/// and plugin-not-installed are each ONE STEP from working, and the app said nothing about either, so
/// it presented as though the in-app storefront had never been built. A game with no Nexus page is a
/// different thing: it genuinely does not apply.</para>
///
/// <para><b>The rule this encodes:</b> hide a capability only when it can never work <i>here</i>.
/// Where it is one step away, show it and name the step.</para>
/// </summary>
public static class ModBrowseRules
{
    /// <summary>The in-app door. Paired with <see cref="BrowserLabel"/> — the two are told apart by
    /// where they LAND, which is the thing a person actually needs to know before clicking.</summary>
    public const string InAppLabel = "Find mods (in-app)";

    /// <summary>The out-of-app door, unchanged in behaviour and finally distinguishable. It read
    /// "Find mods" next to a button reading "Browse Nexus", and neither said which one left the app.</summary>
    public const string BrowserLabel = "Find mods in browser";

    public static BrowseAffordance For(bool connected, bool catalogCapable, bool hasDomain)
    {
        // Nexus has no page for this game. Not a capability gap — the capability does not apply, and
        // an explanation nobody can act on is clutter.
        if (!hasDomain)
            return new BrowseAffordance(false, InAppLabel, "", CanBrowse: false, BrowseRemedy.None);

        if (!connected)
            return new BrowseAffordance(true, InAppLabel,
                "Sign in to Nexus to browse this game's mods without leaving the launcher.",
                CanBrowse: false, BrowseRemedy.ConnectNexus);

        if (!catalogCapable)
            return new BrowseAffordance(true, InAppLabel,
                "In-app browsing needs the Nexus plugin. Install it from Settings.",
                CanBrowse: false, BrowseRemedy.InstallPlugin);

        return new BrowseAffordance(true, InAppLabel,
            "Browse this game's Nexus mods in the launcher — sorted, filterable, and marked with what you already have.",
            CanBrowse: true, BrowseRemedy.None);
    }
}

// -------------------------------------------------------------------------------------------------
// Item 4 — the mod list is never a blank rectangle
// -------------------------------------------------------------------------------------------------

/// <summary>
/// What to say when the mod list has nothing in it.
///
/// <para><b>Two holes, one place.</b> The App's empty state was <c>HasGame ? Collapsed : Visible</c>,
/// and the zero-match message only fired on a search miss. So a registered game with no mods rendered
/// a blank rectangle with no words — at exactly the moment the app most needs to say <i>drop a zip
/// here</i>. And wave 6 opened a second one in the same spot: the MP/SP segments became a filter, but
/// the zero-match branch still required search text, so filtering to MP on an all-single-player game
/// went blank and wordless. That one is worse, because the control that did it is a VIEW control and a
/// blank list reads as <i>the mods are gone</i>.</para>
///
/// <para>Every branch names what is true AND what to press. Returns null when there is something to
/// show and therefore nothing to explain.</para>
/// </summary>
public static class ModListEmptyState
{
    public static string? MessageFor(bool hasGame, int totalRows, int visibleRows, string? search, string? mode)
    {
        if (!hasGame) return "No game registered yet. Add one with + Game.";
        if (visibleRows > 0) return null;

        var query = (search ?? "").Trim();
        var m = ModeLabel(mode);

        // Nothing is installed. The most important empty state in the app, and the one that rendered
        // as nothing at all.
        if (totalRows == 0)
            return "No mods here yet. Drop a mod archive on this window, or use + Add mods.";

        // Mods exist; the view is hiding them. Say which control did it, and how to undo it — a person
        // who filtered by accident is looking at what appears to be an empty install.
        if (query.Length > 0 && m is not null)
            return $"No {m} mods match \"{query}\". Clear the search, or switch the loadout back to All.";

        if (query.Length > 0)
            return $"No mods match \"{query}\".";

        if (m is not null)
            return $"None of your {totalRows} mods are marked {m}. Switch the loadout back to All to see them.";

        // Rows exist and none are visible, with no filter to blame. Not reachable through the UI
        // today; still says something rather than nothing, because a blank list reads as broken.
        return "Nothing to show here.";
    }

    /// <summary>The segment label, or null for "all" / unset — the states that are not filtering.</summary>
    private static string? ModeLabel(string? mode) => (mode ?? "").Trim().ToLowerInvariant() switch
    {
        "mp" => "MP",
        "sp" => "SP",
        _ => null,
    };
}

// -------------------------------------------------------------------------------------------------
// Item 5 — the NEEDS chip offers what the app can already do
// -------------------------------------------------------------------------------------------------

/// <param name="Title">The dialog heading. Names the framework, nothing else.</param>
/// <param name="Consequence">What happens if it stays missing. Never a definition of the thing.</param>
/// <param name="CanInstallHere">Whether the launcher can install it from a file the user already has.</param>
/// <param name="InstallLabel">The button for that, when it applies.</param>
/// <param name="GetLabel">The button that opens the download page, when there is one.</param>
public sealed record FrameworkOffer(
    string Title,
    string Consequence,
    bool CanInstallHere,
    string InstallLabel,
    string? GetLabel);

/// <summary>
/// What the <c>NEEDS ___</c> chip says, and what it offers.
///
/// <para><b>The fault.</b> The chip was a <c>HyperlinkButton</c> to a GitHub releases page. So the
/// app's answer to "you need UE4SS" was a list of files a first-time modder cannot choose between —
/// which is where the round table's new modder closed the app, seconds after a successful toggle. And
/// the launcher can install it: drop the right archive and <c>AddModsAsync</c> classifies it,
/// <c>FrameworkInstallDialog</c> shows exactly what lands where, and <c>FrameworkInstaller.Install</c>
/// validate-then-extracts it. The chip had never mentioned that.</para>
///
/// <para><b>Copy rule, taken from the two chips that already work on a newcomer</b> (LOADER, BAN RISK):
/// state the CONSEQUENCE, not the definition. "Mods that need UE4SS will not load until it is
/// installed" — never "UE4SS is a script loader for Unreal Engine games."</para>
/// </summary>
public static class FrameworkOfferRules
{
    public static FrameworkOffer For(string frameworkName, string? getUrl, bool soft)
    {
        var name = (frameworkName ?? "").Trim();
        if (name.Length == 0) name = "this framework";

        // "MAY NEED" on the row is a hint, not an assertion — the launcher is not certain this mod
        // needs it. Saying "will not load" there would be a lie, and the kind that makes someone
        // install something they did not need.
        var consequence = soft
            ? $"This mod may need {name}. If it does, it will not load until {name} is installed."
            : $"Mods that need {name} will not load until it is installed.";

        return new FrameworkOffer(
            Title: name + " is not installed",
            Consequence: consequence,
            // Always true: the install path is the same one a drop uses, and it exists for every
            // framework in the catalog. The dialog is not offering a new capability, it is pointing at
            // the one the window has had all along.
            CanInstallHere: true,
            InstallLabel: "I already have the file",
            GetLabel: SafeUrl.IsHttpUrl(getUrl) ? "Get it" : null);
    }
}
