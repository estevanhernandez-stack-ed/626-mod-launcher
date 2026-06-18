using System.Net;
using System.Text;

namespace ModManager.App.NexusValidate.Tests;

/// <summary>
/// Transport stub for the App-side Nexus validate path. <see cref="NexusKeyValidator.ValidateAsync"/>'s
/// only seam is the <see cref="HttpClient"/> handed in, so every validate test drives it through this
/// mock <see cref="HttpMessageHandler"/> and asserts on the captured request (URL / method / headers)
/// and the mapped result. Same transport-injection shape the relocated plugin tests use — this is the
/// re-home for the deleted Core <c>NexusClient</c>/<c>NexusRequests</c> validate assertions.
/// </summary>
internal sealed class StubHandler : HttpMessageHandler
{
    internal sealed record Call(string Url, string Method, string? ApiKey, string? AppName, string? AppVersion, string? Accept);

    private readonly HttpStatusCode _status;
    private readonly string _json;

    public List<Call> Calls { get; } = new();

    /// <summary>Every request gets the same canned response — validate is a single GET.</summary>
    public StubHandler(HttpStatusCode status, string json)
    {
        _status = status;
        _json = json;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls.Add(new Call(
            request.RequestUri!.ToString(),
            request.Method.Method,
            Header(request, "apikey"),
            Header(request, "Application-Name"),
            Header(request, "Application-Version"),
            Header(request, "Accept")));

        var res = new HttpResponseMessage(_status) { Content = new StringContent(_json, Encoding.UTF8, "application/json") };
        return Task.FromResult(res);
    }

    private static string? Header(HttpRequestMessage req, string name)
        => req.Headers.TryGetValues(name, out var v) ? string.Join(",", v) : null;
}
