using System.Net;
using System.Text;

namespace ModManager.Plugin.Nexus.Tests;

/// <summary>
/// Shared transport stub for the plugin's HTTP surface. The plugin's only seam is the
/// <see cref="HttpClient"/> handed to <c>NexusModSource</c>, so every relocated test drives it through
/// a mock <see cref="HttpMessageHandler"/> and asserts on the captured request (URL / method / headers /
/// body) and the mapped response. Replaces the deleted Core <c>NexusClient</c> stub handlers — the same
/// transport-injection shape, now over the plugin instead of the deleted client.
/// </summary>
internal sealed class StubHandler : HttpMessageHandler
{
    internal sealed record Call(string Url, string Method, string? ApiKey, string? AppName, string? AppVersion, string? Accept, string? Body, string? ContentType);

    private readonly Func<HttpRequestMessage, (HttpStatusCode Status, string Json)> _responder;

    public List<Call> Calls { get; } = new();

    /// <summary>Every request gets the same canned response. Used for the single-call read/write paths.</summary>
    public StubHandler(HttpStatusCode status, string json)
        : this(_ => (status, json)) { }

    /// <summary>Response depends on the request (rarely needed — the plugin makes one call per op).</summary>
    public StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> responder) => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var apiKey = Header(request, "apikey");
        var appName = Header(request, "Application-Name");
        var appVersion = Header(request, "Application-Version");
        var accept = Header(request, "Accept");
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var contentType = request.Content?.Headers.ContentType?.MediaType;
        Calls.Add(new Call(request.RequestUri!.ToString(), request.Method.Method, apiKey, appName, appVersion, accept, body, contentType));

        var (status, json) = _responder(request);
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static string? Header(HttpRequestMessage req, string name)
        => req.Headers.TryGetValues(name, out var v) ? string.Join(",", v) : null;

    /// <summary>Build a <c>NexusModSource</c> over this handler with a fixed key + app version, so request
    /// assertions can pin the exact <c>apikey</c> / <c>Application-Version</c> headers.</summary>
    public ModManager.Plugin.Nexus.NexusModSource Source(string? apiKey = "K", string? appVersion = "0.3.0")
        => new(new HttpClient(this), () => apiKey, appVersion);
}
