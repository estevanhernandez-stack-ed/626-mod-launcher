using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using ModManager.Core.Nexus;
using Xunit;

public class NexusTokenRequestTests
{
    private static readonly NexusOAuthConfig Cfg =
        new("cid", "https://auth/authorize", "https://auth/token", "public");

    [Fact]
    public void AuthorizeUrl_has_pkce_and_state()
    {
        var url = NexusTokenRequest.BuildAuthorizeUrl(Cfg, "http://127.0.0.1:41999/callback", "chal", "st");
        Assert.StartsWith("https://auth/authorize?", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("client_id=cid", url);
        Assert.Contains("code_challenge=chal", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("state=st", url);
        Assert.Contains("scope=public", url);
        Assert.Contains(Uri.EscapeDataString("http://127.0.0.1:41999/callback"), url);
    }

    [Fact]
    public async Task ExchangeBody_is_authorization_code_grant()
    {
        var body = NexusTokenRequest.BuildExchangeBody(Cfg, "the-code", "http://127.0.0.1:41999/callback", "verif");
        var s = await body.ReadAsStringAsync();
        Assert.Contains("grant_type=authorization_code", s);
        Assert.Contains("code=the-code", s);
        Assert.Contains("code_verifier=verif", s);
        Assert.Contains("client_id=cid", s);
    }

    [Fact]
    public async Task RefreshBody_is_refresh_token_grant()
    {
        var body = NexusTokenRequest.BuildRefreshBody(Cfg, "rtok");
        var s = await body.ReadAsStringAsync();
        Assert.Contains("grant_type=refresh_token", s);
        Assert.Contains("refresh_token=rtok", s);
        Assert.Contains("client_id=cid", s);
    }
}
