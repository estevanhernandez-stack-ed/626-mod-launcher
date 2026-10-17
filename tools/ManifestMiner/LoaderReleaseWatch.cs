using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>A GitHub-hosted, version-locked loader the release watch asks about. <see cref="Name"/> is the
/// product name <c>StaleLoaders</c> uses, which is also the <c>loaderReleases[].loader</c> key.</summary>
public sealed record WatchedLoader(string Name, string Owner, string Repo);

/// <summary>Everything the opt-in <c>--loader-releases</c> step needs. The handler is the HTTP seam, so
/// tests run offline; <see cref="Previous"/> is what a failed lookup falls back to.</summary>
public sealed record LoaderReleaseOptions(
    HttpMessageHandler Handler,
    IReadOnlyList<LoaderReleaseEntry> Previous,
    string? Token,
    DateTime NowUtc,
    TextWriter Log);

/// <summary>
/// Patch Day's release watch (spec section 4): the newest stable GitHub release of each watched loader,
/// written as <c>loaderReleases</c> before signing. A lookup that fails for any reason keeps that loader's
/// previous value, and a row the launcher's validator would refuse is never written.
///
/// <para>Elden Mod Loader is Nexus-hosted, so it is not here. The owner/repo table mirrors the compiled
/// catalog URLs (<c>ProxyLoaderRows.Known</c>, <c>FrameworkDeps.Catalog</c>); a test pins the match.</para>
/// </summary>
public static class LoaderReleaseWatch
{
    public static readonly IReadOnlyList<WatchedLoader> Watched = new[]
    {
        new WatchedLoader("REFramework", "praydog", "REFramework"),
        new WatchedLoader("UE4SS", "UE4SS-RE", "RE-UE4SS"),
        new WatchedLoader("Mod Engine 2", "soulsmods", "ModEngine2"),
    };

    public static async Task<IReadOnlyList<LoaderReleaseEntry>> RunAsync(
        HttpMessageHandler handler, IReadOnlyList<LoaderReleaseEntry> previous, string? token,
        DateTime nowUtc, TextWriter log, CancellationToken ct = default)
    {
        using var http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(20) };
        var rows = new List<LoaderReleaseEntry>();

        foreach (var w in Watched)
        {
            LoaderReleaseEntry? fresh = null;
            string why;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.github.com/repos/{w.Owner}/{w.Repo}/releases?per_page=10");
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("626-manifest-miner", "1"));
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                if (!string.IsNullOrWhiteSpace(token))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var resp = await http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    why = $"HTTP {(int)resp.StatusCode}";
                }
                else
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    (fresh, why) = Pick(w, body, nowUtc);
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                if (ct.IsCancellationRequested) throw;
                why = e.GetType().Name;
            }

            if (fresh is not null)
            {
                rows.Add(fresh);
                log.WriteLine($"loader-releases: {w.Name}: fetched {fresh.Version} ({fresh.PublishedUtc})");
                continue;
            }

            var old = previous.FirstOrDefault(p => string.Equals(p.Loader, w.Name, StringComparison.Ordinal));
            if (old is not null && ManifestValidator.LoaderReleaseProblem(old, nowUtc) is null)
            {
                rows.Add(old);
                log.WriteLine($"loader-releases: {w.Name}: kept previous {old.Version} ({why})");
            }
            else
            {
                log.WriteLine($"loader-releases: {w.Name}: skipped, no row ({why}; no usable previous)");
            }
        }
        return rows;
    }

    private static (LoaderReleaseEntry? Row, string Why) Pick(WatchedLoader w, string json, DateTime nowUtc)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return (null, "response is not a release list");

        LoaderReleaseEntry? best = null;
        DateTime bestWhen = default;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object) continue;
            if (Flag(r, "draft") || Flag(r, "prerelease")) continue;
            var tag = Str(r, "tag_name");
            var url = Str(r, "html_url");
            var published = Str(r, "published_at");
            if (tag is null || url is null || !ManifestValidator.TryParseUtc(published, out var when)) continue;
            if (best is not null && when <= bestWhen) continue;
            bestWhen = when;
            best = new LoaderReleaseEntry
            {
                Loader = w.Name,
                Version = tag,
                PublishedUtc = when.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                Url = url,
            };
        }
        if (best is null) return (null, "no stable release");
        var problem = ManifestValidator.LoaderReleaseProblem(best, nowUtc);
        return problem is null ? (best, "") : (null, problem);
    }

    private static bool Flag(JsonElement o, string name)
        => o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static string? Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
