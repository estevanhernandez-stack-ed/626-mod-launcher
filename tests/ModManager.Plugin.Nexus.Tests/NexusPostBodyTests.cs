using System.Net;

namespace ModManager.Plugin.Nexus.Tests;

// POST-body wiring over the plugin. Relocated from the deleted Core NexusPostBodyTests, which verified
// the client's SendAsync attached a JSON body on writes, sent no content on GETs, and never threw by
// copying a Content-Type onto the message headers. The plugin's SendAsync is private, so this exercises
// it through its only writer (SetEndorsedAsync) and its readers (GETs): the POST rides as
// application/json content with the right body, and GETs carry no content at all.
public class NexusPostBodyTests
{
    private static readonly ModManager.Plugins.Abstractions.SourceModRef Ref =
        new(SourceId: "nexus", GameDomain: "eldenring", ModId: 42, Version: "1.0");

    [Fact]
    public async Task Endorse_post_sends_json_content_with_the_version_body()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{"status":"Endorsed"}""");
        await h.Source().SetEndorsedAsync(Ref, endorsed: true);

        Assert.Equal("POST", h.Calls[0].Method);
        Assert.Equal("""{"Version":"1.0"}""", h.Calls[0].Body);
        // StringContent sets the media type — never copied off a header dictionary (the bug the old
        // SendAsync guarded against can't occur with the plugin's StringContent path).
        Assert.Equal("application/json", h.Calls[0].ContentType);
    }

    [Fact]
    public async Task Get_read_sends_no_content()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "mod_id": 42, "name": "X" }""");
        await h.Source().FetchMetadataAsync(Ref);

        Assert.Equal("GET", h.Calls[0].Method);
        Assert.Null(h.Calls[0].Body);
        Assert.Null(h.Calls[0].ContentType);
    }
}
