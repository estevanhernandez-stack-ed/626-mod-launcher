using System.Net;
using System.Net.Http;
using ModManager.App.Services;

namespace ModManager.App.NexusValidate.Tests;

/// <summary>
/// The App-side Nexus validate contract (<c>GET /v1/users/validate.json</c>), re-homed from the deleted
/// Core <c>NexusClientTests.ValidateAsync_*</c> (200-&gt;user / 401-&gt;null / non-ok-&gt;throw) and
/// <c>NexusRequestsTests.ValidateRequest_*</c> + <c>MapValidateResponse_*</c> (the request shape + the
/// snake_case <c>name</c>/<c>is_premium</c> parse). The behavior moved out of the deleted
/// <c>NexusClient</c> into <see cref="NexusKeyValidator"/>; these tests follow it. A wrong header or a
/// mis-parsed <c>is_premium</c> silently breaks Connect Nexus / premium detection — so it stays tested.
/// </summary>
public class NexusValidateTests
{
    private const string AppVersion = "0.3.0";

    private static Task<(string? Name, bool IsPremium)?> Validate(StubHandler h, string key = "K")
        => NexusKeyValidator.ValidateAsync(new HttpClient(h), key, AppVersion);

    // --- MapValidateResponse_* : the snake_case body parse -------------------------------------------

    [Fact]
    public async Task ValidateAsync_maps_a_200_body_to_user()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "name": "Este", "is_premium": true }""");

        var user = await Validate(h);

        Assert.NotNull(user);
        Assert.Equal("Este", user!.Value.Name);
        Assert.True(user.Value.IsPremium);
    }

    [Fact]
    public async Task MapValidateResponse_reads_premium_false_when_is_premium_is_false()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "name": "Este", "is_premium": false }""");

        var user = await Validate(h);

        Assert.Equal("Este", user!.Value.Name);
        Assert.False(user.Value.IsPremium);
    }

    [Fact]
    public async Task MapValidateResponse_defaults_premium_false_when_is_premium_is_absent()
    {
        // is_premium absent — only JsonValueKind.True flips the flag, so this stays false (never throws).
        var h = new StubHandler(HttpStatusCode.OK, """{ "name": "Este" }""");

        var user = await Validate(h);

        Assert.Equal("Este", user!.Value.Name);
        Assert.False(user.Value.IsPremium);
    }

    [Fact]
    public async Task MapValidateResponse_returns_null_on_a_non_object_body()
    {
        // A JSON array (or any non-object) is not a valid user — map to null, not a throw.
        var h = new StubHandler(HttpStatusCode.OK, "[]");

        var user = await Validate(h);

        Assert.Null(user);
    }

    // --- ValidateAsync_* : the HTTP status contract -------------------------------------------------

    [Fact]
    public async Task ValidateAsync_returns_null_on_401()
    {
        // A rejected / expired key is the normal "bad key" path — null, never a throw.
        var h = new StubHandler(HttpStatusCode.Unauthorized, """{ "message": "Please provide a valid API Key" }""");

        var user = await Validate(h);

        Assert.Null(user);
    }

    [Fact]
    public async Task ValidateAsync_throws_on_other_non_ok()
    {
        // 500 (or any non-2xx that isn't 401) is unexpected — surface it so the caller can decide.
        var h = new StubHandler(HttpStatusCode.InternalServerError, "{}");

        await Assert.ThrowsAsync<HttpRequestException>(() => Validate(h));
    }

    // --- ValidateRequest_* : the request shape (ToS headers + URL + method) -------------------------

    [Fact]
    public async Task ValidateRequest_targets_the_validate_endpoint_with_GET()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "name": "Este", "is_premium": false }""");

        await Validate(h);

        Assert.Equal("https://api.nexusmods.com/v1/users/validate.json", h.Calls[0].Url);
        Assert.Equal("GET", h.Calls[0].Method);
    }

    [Fact]
    public async Task ValidateRequest_carries_the_apikey_and_tos_identity_headers()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "name": "Este", "is_premium": false }""");

        await Validate(h, key: "secret-key");

        Assert.Equal("secret-key", h.Calls[0].ApiKey);
        Assert.Equal("626-mod-launcher", h.Calls[0].AppName);
        Assert.Equal(AppVersion, h.Calls[0].AppVersion);
        Assert.Equal("application/json", h.Calls[0].Accept);
    }
}
