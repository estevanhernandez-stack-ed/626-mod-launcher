using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>What 626 knows about a launch that goes through ELDEN RING Reforged's own launcher.
/// <see cref="Root"/> is the Reforged install folder. <see cref="SaveExtension"/> is the save file that launch reads,
/// or null when that can't be said for sure.</summary>
public sealed record ReforgedLaunchInfo(string Root, bool AltProfile, string? SaveExtension);

/// <summary>
/// Recognizes a launch target that starts Reforged (save-type offer, decision 3: "detected by its launcher").
///
/// <para>Reforged installs outside the game folder and starts through <c>internals\launcher\ReforgedLauncher.exe</c>,
/// usually by way of the numbered <c>.BAT</c> files at its root (measured on Este's D:\ERRv2.1.2.3, 2026-11-07). Its
/// <c>mod\altsaves.toml</c> names the save extension for normal play (<c>extension</c>) and for Seamless
/// (<c>seamless_extension</c>). Both are user-editable, so they're read, not assumed. When they differ, the launch
/// could read either file, so <see cref="ReforgedLaunchInfo.SaveExtension"/> is null and nothing is offered.</para>
///
/// <para>Never throws: anything unreadable is "not Reforged" or "don't know".</para>
/// </summary>
public static class ReforgedLaunch
{
    public const string LauncherExe = "ReforgedLauncher.exe";
    private const string DefaultExtension = ".err";
    private static readonly Regex TomlKey = new(@"^\s*(seamless_extension|extension)\s*=\s*""([^""]*)""", RegexOptions.Multiline);

    public static ReforgedLaunchInfo? Inspect(LaunchTarget? target)
    {
        try
        {
            if (target is null || !string.Equals(target.Kind, "exe", StringComparison.OrdinalIgnoreCase)) return null;
            var path = target.Target;
            if (string.IsNullOrWhiteSpace(path)) return null;

            string? root;
            bool alt;
            if (IsLauncherName(path))
            {
                root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(path))));
                alt = Mentions(target.Args, "--alt-save-profile");
            }
            else if (IsScript(path) && File.Exists(path))
            {
                var text = File.ReadAllText(path);
                if (!Mentions(text, LauncherExe)) return null;
                root = FindRoot(Path.GetDirectoryName(Path.GetFullPath(path)));
                alt = Mentions(text, "--alt-save-profile") || Mentions(target.Args, "--alt-save-profile");
            }
            else return null;

            return root is null ? null : new ReforgedLaunchInfo(root, alt, ReadExtension(root));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True for Reforged's launcher exe, or a script that runs it. Lets <see cref="LaunchTargetPicker"/>
    /// keep a vanilla launch off Reforged.</summary>
    public static bool IsReforgedLauncherPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (IsLauncherName(path)) return true;
        if (!IsScript(path)) return false;
        try { return File.Exists(path) && Mentions(File.ReadAllText(path), LauncherExe); }
        catch { return false; }
    }

    private static bool IsLauncherName(string path)
        => string.Equals(FileName(path), LauncherExe, StringComparison.OrdinalIgnoreCase);

    private static bool IsScript(string path)
    {
        var name = FileName(path);
        return name.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
    }

    private static string FileName(string path)
    {
        var p = path.Replace('\\', '/');
        return p[(p.LastIndexOf('/') + 1)..];
    }

    private static bool Mentions(string? text, string what)
        => text is not null && text.Contains(what, StringComparison.OrdinalIgnoreCase);

    // The script's own folder, then up to three levels: the alt-profile scripts sit two below the root.
    private static string? FindRoot(string? dir)
    {
        for (var i = 0; dir is not null && i < 4; i++, dir = Path.GetDirectoryName(dir))
            if (File.Exists(Path.Combine(dir, "internals", "launcher", LauncherExe))) return dir;
        return null;
    }

    private static string? ReadExtension(string root)
    {
        var toml = Path.Combine(root, "mod", "altsaves.toml");
        if (!File.Exists(toml)) return DefaultExtension;
        string? normal = null, seamless = null;
        foreach (Match m in TomlKey.Matches(File.ReadAllText(toml)))
        {
            var value = Normalize(m.Groups[2].Value);
            if (m.Groups[1].Value == "extension") normal = value;
            else seamless = value;
        }
        if (normal is not null && seamless is not null)
            return string.Equals(normal, seamless, StringComparison.OrdinalIgnoreCase) ? normal : null;
        return normal ?? seamless ?? DefaultExtension;
    }

    private static string? Normalize(string value)
    {
        var v = value.Trim();
        if (v.Length == 0) return null;
        return v.StartsWith('.') ? v : "." + v;
    }
}
