namespace ModManager.Core.LooseMods;

/// <summary>
/// What a proxy DLL in a game root actually is, and what disabling it costs.
///
/// <para><b>Why this exists.</b> A file called <c>version.dll</c> was labelled "Version (ASI Loader)"
/// and its disable warning read <i>"disabling it disables every ASI plugin"</i>. On Death Stranding 2
/// that file is <b>DLSS Enabler 4.5.2.2</b>, and the neighbouring <c>dxgi.dll</c> is
/// <b>ReShade 6.7.3</b>. Neither is an ASI loader; disabling the first costs DLSS and frame generation,
/// and the three <c>.addon64</c> mods on that install are ReShade addons riding on the second. The
/// label came from the filename and the consequence came from the label (A24).</para>
///
/// <para>The launcher already knows better one surface over — the discovery review dialog says
/// <i>"Several different loaders ship under this filename, so it can't be named from the file alone."</i>
/// That is the fallback here. Where the file names itself we say what it is; where it does not, we say
/// what we don't know, and never a confident sentence about ASI plugins.</para>
/// </summary>
public static class LoaderIdentity
{
    /// <summary>Known proxies whose consequence we can state exactly. Keyed on the product name the
    /// file reports, not on its filename — the filename is the thing that misled us.</summary>
    private static readonly (string Product, string Consequence)[] Known =
    {
        ("reshade", "ReShade and any addons that ride on it will not load."),
        ("dlss enabler", "DLSS and frame generation will not be available."),
        ("ultimate asi loader", "Every .asi plugin in the game folder will not load."),
        ("asi loader", "Every .asi plugin in the game folder will not load."),
    };

    /// <summary>How to name the row's DISPLAY, given what the file says about itself. Never the row's
    /// key: the key is derived from the filename and a holding folder is already named after it, so
    /// changing it would orphan a disabled loader's files.</summary>
    public static string Label(string fileName, string? product, string? version)
    {
        var name = (fileName ?? "").Trim();
        var p = Clean(product);
        if (p.Length == 0) return $"{name} (loader)";
        var v = Clean(version);
        return v.Length == 0 ? $"{name} — {p}" : $"{name} — {p} {v}";
    }

    /// <summary>What disabling it costs, in a sentence. A product we recognise gets the real answer; a
    /// product we can read but don't recognise gets named without a guess at its role; a file that says
    /// nothing about itself gets the honest admission.</summary>
    public static string Consequence(string fileName, string? product)
    {
        var p = Clean(product);
        if (p.Length == 0)
            return $"\"{fileName}\" is a loader other mods ride on. Several different loaders ship under "
                   + "this filename, so 626 can't tell which one this is from the file alone — anything "
                   + "depending on it will stop loading.";

        var match = Known.FirstOrDefault(k => p.Contains(k.Product, StringComparison.OrdinalIgnoreCase));
        return match.Product is null
            ? $"\"{fileName}\" is {p}. Anything that rides on it will stop loading."
            : $"\"{fileName}\" is {p}. {match.Consequence}";
    }

    /// <summary>What a DLL says about itself, from its version resource. Empty for a file that
    /// carries none, is unreadable, or is not a PE at all — never an exception, because a loader we
    /// cannot read is a fallback case, not a failure.</summary>
    public static (string? Product, string? Version) ReadProduct(string absPath)
    {
        try
        {
            if (!File.Exists(absPath)) return (null, null);
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(absPath);
            return (info.ProductName, info.FileVersion);
        }
        catch { return (null, null); }
    }

    /// <summary>True when the file identified itself well enough to be worth quoting.</summary>
    public static bool Identified(string? product) => Clean(product).Length > 0;

    // A version resource can carry whitespace-only or placeholder junk; treat that as unknown rather
    // than printing it. Quoting an empty product is worse than the filename alone.
    private static string Clean(string? s)
    {
        var t = (s ?? "").Trim();
        return t.Length == 0 || t == "-" ? "" : t;
    }
}
