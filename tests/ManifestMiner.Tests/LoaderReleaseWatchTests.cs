using System.Net;
using System.Text;
using System.Text.Json;
using ManifestMiner;
using ModManager.Core;
using ModManager.Core.Manifest;

namespace ManifestMiner.Tests;

public class LoaderReleaseWatchTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Canned GitHub: repo path ("praydog/REFramework") to a status code and JSON body.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Code, string Body)> Repos { get; } = new();
        public List<HttpRequestMessage> Seen { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add(request);
            var path = request.RequestUri!.AbsolutePath; // /repos/{owner}/{repo}/releases
            var key = string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(2));
            if (!Repos.TryGetValue(key, out var r)) throw new HttpRequestException("no route " + key);
            return Task.FromResult(new HttpResponseMessage(r.Code) { Content = new StringContent(r.Body, Encoding.UTF8, "application/json") });
        }
    }

    private static string Rel(string tag, string published, bool pre = false, bool draft = false) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["tag_name"] = tag,
            ["html_url"] = $"https://github.com/x/y/releases/tag/{tag}",
            ["published_at"] = published,
            ["prerelease"] = pre,
            ["draft"] = draft,
        });

    private static string Arr(params string[] items) => "[" + string.Join(",", items) + "]";

    private static FakeGitHub AllOk(string refTag = "v1.0.0")
    {
        var gh = new FakeGitHub();
        gh.Repos["praydog/REFramework"] = (HttpStatusCode.OK, Arr(Rel(refTag, "2026-10-10T00:00:00Z")));
        gh.Repos["UE4SS-RE/RE-UE4SS"] = (HttpStatusCode.OK, Arr(Rel("v3.0.1", "2026-09-01T00:00:00Z")));
        gh.Repos["soulsmods/ModEngine2"] = (HttpStatusCode.OK, Arr(Rel("v2.1.0", "2026-08-01T00:00:00Z")));
        return gh;
    }

    private static Task<IReadOnlyList<LoaderReleaseEntry>> Run(
        FakeGitHub gh, IReadOnlyList<LoaderReleaseEntry>? previous = null, string? token = null, StringWriter? log = null)
        => LoaderReleaseWatch.RunAsync(gh, previous ?? Array.Empty<LoaderReleaseEntry>(), token, Now, log ?? new StringWriter());

    private static LoaderReleaseEntry Row(string loader, string version) => new()
    {
        Loader = loader, Version = version, PublishedUtc = "2026-01-01T00:00:00Z", Url = "https://github.com/x/y/releases/tag/" + version,
    };

    [Fact]
    public async Task Picks_newest_stable_over_a_newer_prerelease()
    {
        var gh = AllOk();
        gh.Repos["praydog/REFramework"] = (HttpStatusCode.OK, Arr(
            Rel("nightly-9", "2026-10-15T00:00:00Z", pre: true),
            Rel("v1.0.0", "2026-10-01T00:00:00Z"),
            Rel("v1.1.0", "2026-10-12T00:00:00Z")));
        var rows = await Run(gh);
        var r = Assert.Single(rows, x => x.Loader == "REFramework");
        Assert.Equal("v1.1.0", r.Version);
        Assert.Equal("2026-10-12T00:00:00Z", r.PublishedUtc);
        Assert.Equal("https://github.com/x/y/releases/tag/v1.1.0", r.Url);
    }

    [Fact]
    public async Task Skips_drafts_even_when_newest()
    {
        var gh = AllOk();
        gh.Repos["praydog/REFramework"] = (HttpStatusCode.OK, Arr(
            Rel("v2.0.0", "2026-10-14T00:00:00Z", draft: true),
            Rel("v1.0.0", "2026-10-01T00:00:00Z")));
        var rows = await Run(gh);
        Assert.Equal("v1.0.0", rows.Single(x => x.Loader == "REFramework").Version);
    }

    [Fact]
    public async Task Failure_keeps_the_previous_value_and_others_still_update()
    {
        var gh = AllOk();
        gh.Repos["praydog/REFramework"] = (HttpStatusCode.Forbidden, "{\"message\":\"rate limit\"}");
        var log = new StringWriter();
        var rows = await Run(gh, new[] { Row("REFramework", "v0.9.0") }, log: log);
        Assert.Equal("v0.9.0", rows.Single(x => x.Loader == "REFramework").Version);
        Assert.Equal("v3.0.1", rows.Single(x => x.Loader == "UE4SS").Version);
        Assert.Contains("kept previous", log.ToString());
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("[{\"tag_name\":\"v1\",\"prerelease\":true,\"draft\":false,\"published_at\":\"2026-10-01T00:00:00Z\",\"html_url\":\"https://github.com/a/b\"}]")]
    public async Task Bad_json_or_no_stable_release_keeps_previous(string body)
    {
        var gh = AllOk();
        gh.Repos["soulsmods/ModEngine2"] = (HttpStatusCode.OK, body);
        var rows = await Run(gh, new[] { Row("Mod Engine 2", "v2.0.0") });
        Assert.Equal("v2.0.0", rows.Single(x => x.Loader == "Mod Engine 2").Version);
    }

    [Fact]
    public async Task Network_exception_with_no_previous_writes_no_row()
    {
        var gh = AllOk();
        gh.Repos.Remove("UE4SS-RE/RE-UE4SS"); // handler throws HttpRequestException
        var rows = await Run(gh);
        Assert.DoesNotContain(rows, x => x.Loader == "UE4SS");
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task A_future_dated_release_is_not_written_and_previous_is_kept()
    {
        var gh = AllOk();
        gh.Repos["praydog/REFramework"] = (HttpStatusCode.OK, Arr(Rel("v9", "2027-01-01T00:00:00Z")));
        var rows = await Run(gh, new[] { Row("REFramework", "v0.9.0") });
        Assert.Equal("v0.9.0", rows.Single(x => x.Loader == "REFramework").Version);
    }

    [Fact]
    public async Task Auth_header_only_when_a_token_is_given()
    {
        var anon = AllOk();
        await Run(anon);
        Assert.Equal(3, anon.Seen.Count);
        Assert.All(anon.Seen, r => Assert.Null(r.Headers.Authorization));
        Assert.All(anon.Seen, r =>
        {
            Assert.NotEmpty(r.Headers.UserAgent);
            Assert.Contains("application/vnd.github+json", r.Headers.Accept.Select(a => a.MediaType));
            Assert.EndsWith("/releases?per_page=10", r.RequestUri!.PathAndQuery);
        });

        var authed = AllOk();
        await Run(authed, token: "t0ken");
        Assert.Equal(3, authed.Seen.Count);
        Assert.All(authed.Seen, r =>
        {
            Assert.Equal("Bearer", r.Headers.Authorization!.Scheme);
            Assert.Equal("t0ken", r.Headers.Authorization.Parameter);
        });
    }

    [Fact]
    public async Task Output_rows_pass_the_manifest_validator_and_log_one_line_per_loader()
    {
        var log = new StringWriter();
        var rows = await Run(AllOk(), log: log);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Null(ManifestValidator.LoaderReleaseProblem(r, Now)));
        var lines = log.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Contains("fetched", lines[0]);
    }

    [Fact]
    public void Watched_table_matches_the_compiled_catalog_urls()
    {
        string Url(WatchedLoader w) => $"https://github.com/{w.Owner}/{w.Repo}/releases";
        var re = LoaderReleaseWatch.Watched.Single(w => w.Name == "REFramework");
        Assert.Equal(ProxyLoaderRows.Known.Single(k => k.Name == "REFramework").Url, Url(re));
        foreach (var name in new[] { "UE4SS", "Mod Engine 2" })
            Assert.Equal(FrameworkDeps.Catalog.Single(d => d.Name == name).GetUrl,
                Url(LoaderReleaseWatch.Watched.Single(w => w.Name == name)));
        // Nexus-hosted Elden Mod Loader is deliberately not watched.
        Assert.DoesNotContain(LoaderReleaseWatch.Watched, w => w.Name == "Elden Mod Loader");
        Assert.Equal(3, LoaderReleaseWatch.Watched.Count);
    }

    [Fact]
    public async Task Without_the_flag_published_bytes_are_unchanged()
    {
        var current = new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "g", Name = "G", Engine = "fromsoft", Stores = new StoreIds { SteamAppId = "1" } } },
        };
        var expected = JsonSerializer.SerializeToUtf8Bytes(PublishManifest.ForPublish(current), ManifestJson.Options);
        var actual = await PublishManifest.BuildBytesAsync(current, null);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task With_the_flag_rows_land_in_the_published_bytes()
    {
        var current = new GameManifest();
        var opts = new LoaderReleaseOptions(AllOk(), Array.Empty<LoaderReleaseEntry>(), null, Now, new StringWriter());
        var bytes = await PublishManifest.BuildBytesAsync(current, opts);
        var back = JsonSerializer.Deserialize<GameManifest>(bytes, ManifestJson.Options)!;
        Assert.Equal(3, back.LoaderReleases.Count);
        Assert.Contains("\"loaderReleases\"", Encoding.UTF8.GetString(bytes));
    }
}
