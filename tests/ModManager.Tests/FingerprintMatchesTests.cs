using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests;

// Ports fingerprint-matches.test.js — parse /v1/fingerprints exactMatches into {modId, fingerprint}.
public class FingerprintMatchesTests
{
    [Fact]
    public void ParseFingerprintMatches_maps_exactmatches()
    {
        using var json = JsonDocument.Parse("""
            {"data":{"exactMatches":[
              {"id":238222,"file":{"fileFingerprint":3089143260}},
              {"id":999,"file":{"fileFingerprint":111}}
            ]}}
            """);
        var matches = CurseForgeRequests.ParseFingerprintMatches(json.RootElement);
        Assert.Equal(2, matches.Count);
        Assert.Equal(238222, matches[0].ModId);
        Assert.Equal(3089143260L, matches[0].Fingerprint);
        Assert.Equal(999, matches[1].ModId);
        Assert.Equal(111L, matches[1].Fingerprint);
    }

    [Fact]
    public void ParseFingerprintMatches_handles_empty_or_missing()
    {
        using var emptyArr = JsonDocument.Parse("""{"data":{"exactMatches":[]}}""");
        Assert.Empty(CurseForgeRequests.ParseFingerprintMatches(emptyArr.RootElement));

        using var none = JsonDocument.Parse("{}");
        Assert.Empty(CurseForgeRequests.ParseFingerprintMatches(none.RootElement));
    }
}
