namespace ModManager.Core;

public enum ModSiteProvider { Nexus, CurseForge }

/// <summary>The pieces a manual-match URL paste resolves to: which site, which game on that site,
/// which mod (numeric id for Nexus, slug for CurseForge).</summary>
public sealed record ModSiteUrlParts(ModSiteProvider Provider, string GameKey, string ModRef);

/// <summary>
/// Pure URL parser for Nexus Mods and CurseForge mod-page URLs. Tolerant of trailing slashes,
/// query strings, the optional <c>www.</c> subdomain, and extra path segments (e.g. <c>/files</c>).
/// Returns null when the URL isn't a recognized mod page.
/// </summary>
public static class ModSiteUrl
{
    public static ModSiteUrlParts? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return null;
        if (u.Scheme is not "http" and not "https") return null;

        var host = u.Host.ToLowerInvariant().TrimStart('.');
        if (host.StartsWith("www.")) host = host[4..];

        // Trim leading/trailing slashes; split into segments.
        var segments = u.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3) return null;

        // Nexus: /<gameDomain>/mods/<id>(/.../?...)
        if (host == "nexusmods.com")
        {
            if (!segments[1].Equals("mods", StringComparison.OrdinalIgnoreCase)) return null;
            if (!int.TryParse(segments[2], out var id)) return null;
            return new ModSiteUrlParts(ModSiteProvider.Nexus, segments[0].ToLowerInvariant(), id.ToString());
        }

        // CurseForge: /<gameSlug>/mods/<modSlug>(/...)  OR /<gameSlug>/mc-mods/<modSlug>
        if (host == "curseforge.com")
        {
            // accept "mods" or game-specific path like "mc-mods"
            var bucket = segments[1].ToLowerInvariant();
            if (bucket is not "mods" and not "mc-mods") return null;
            var slug = segments[2].ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(slug)) return null;
            return new ModSiteUrlParts(ModSiteProvider.CurseForge, segments[0].ToLowerInvariant(), slug);
        }

        return null;
    }
}
