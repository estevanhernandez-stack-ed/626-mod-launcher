namespace ModManager.Core;

/// <summary>
/// Loader rows for process-load proxy DLLs the launcher did not install.
///
/// <para>A proxy DLL at the play-folder root is loaded into the game by the OS on every start. On
/// FromSoft games the direct-inject lane already lists it (the "DLL mod loader" row); on every other
/// engine it was invisible — a Monster Hunter Wilds install showed six lua mods and no sign of the
/// REFramework that loads them, so turning every mod off changed nothing and the game kept
/// crashing.</para>
///
/// <para>The row is honest about its limits. Several different loaders ship under the same filename,
/// so the name alone cannot say WHICH loader this is — the same admission the adoption dialog
/// already makes. It reports the file, not a guess at the product.</para>
/// </summary>
public static class ProxyLoaderRows
{
    /// <summary>The location tag these rows carry, so the App can route their toggle through the
    /// proxy step-aside lane rather than the scanner's move.</summary>
    public const string LocationTag = "proxy-loader";

    /// <summary>
    /// Build the rows. Pure: takes filenames, decides nothing from disk.
    ///
    /// <param name="topLevelNames">Filenames directly in the play folder — a proxy here is ACTIVE.</param>
    /// <param name="heldNames">Filenames in the vanilla-proxy holding folder — stepped aside, so the
    /// row exists but reads as off. Without this a disabled loader would vanish from the list
    /// entirely, and a user who stepped it aside would have no way to put it back from the UI.</param>
    /// <param name="alreadyListedNames">Files another lane already lists. FromSoft's direct-inject
    /// row owns dinput8.dll there; producing a second row for the same file would give the user two
    /// switches for one thing, wired to different mechanisms.</param>
    /// </summary>
    /// <summary>One recognisable loader: the proxy it ships as, plus something beside it that only
    /// this loader has. The pair is what makes the name a fact instead of a guess.</summary>
    private sealed record KnownProxyLoader(string Proxy, string Sibling, string Name, string Author, string Url);

    /// <summary>Loaders we can name from disk. Deliberately small: an entry earns its place by having
    /// a sibling nothing else ships. Absent from here means the row says the filename and admits it
    /// cannot say more, which is the honest answer and not a gap to be filled with a guess.</summary>
    private static readonly IReadOnlyList<KnownProxyLoader> Known = new[]
    {
        new KnownProxyLoader("dinput8.dll", "reframework", "REFramework", "praydog",
            "https://github.com/praydog/REFramework/releases"),
        new KnownProxyLoader("dinput8.dll", "mod_loader_config.ini", "Elden Mod Loader", "TechieW",
            "https://www.nexusmods.com/eldenring/mods/117"),
        new KnownProxyLoader("dxgi.dll", "reshade-shaders", "ReShade", "crosire",
            "https://reshade.me"),
    };

    public static IReadOnlyList<Mod> Build(
        IEnumerable<string>? topLevelNames,
        IEnumerable<string>? heldNames,
        IEnumerable<string>? alreadyListedNames,
        IEnumerable<string>? siblingEntries = null)
    {
        var siblings = new HashSet<string>(
            (siblingEntries ?? Enumerable.Empty<string>())
                .Select(e => Path.GetFileName(e.TrimEnd(Path.DirectorySeparatorChar, '/')))
                .Where(n => !string.IsNullOrEmpty(n))!,
            StringComparer.OrdinalIgnoreCase);

        var listed = new HashSet<string>(
            (alreadyListedNames ?? Enumerable.Empty<string>()).Select(Path.GetFileName).Where(n => n is not null)!,
            StringComparer.OrdinalIgnoreCase);

        var active = DirectInject.ProcessLoadProxiesIn(topLevelNames ?? Enumerable.Empty<string>());
        var held   = DirectInject.ProcessLoadProxiesIn(heldNames ?? Enumerable.Empty<string>());

        var rows = new List<Mod>();
        foreach (var name in active.Concat(held).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (listed.Contains(name)) continue;
            var known = Known.FirstOrDefault(k =>
                string.Equals(k.Proxy, name, StringComparison.OrdinalIgnoreCase) && siblings.Contains(k.Sibling));

            rows.Add(new Mod
            {
                Name = name,
                Base = name,
                DisplayName = known is null ? $"{name} — mod loader" : $"{known.Name} ({name})",
                Class = "dll",
                Location = LocationTag,
                Files = new List<string> { name },
                IsLoader = true,
                Author = known?.Author,
                ModUrl = known?.Url,
                // Active means physically at the root, where the OS will load it. A held copy is off.
                Enabled = active.Contains(name, StringComparer.OrdinalIgnoreCase),
                Description = known is null
                    ? "Loaded into the game by Windows on every start. 626 did not install this — several "
                      + "different loaders ship under this filename, so it can't be named from the file "
                      + "alone. Turning it off steps the file aside; mods that ride on it will stop loading."
                    : $"{known.Name} — loaded into the game by Windows on every start, and what the mods "
                      + "here ride on. 626 did not install this, so it isn't tracked for updates. Turning "
                      + "it off steps the file aside; every mod that needs it will stop loading.",
            });
        }
        return rows;
    }
}
