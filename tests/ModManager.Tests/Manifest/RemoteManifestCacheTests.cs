using System.Security.Cryptography;
using System.Text.Json;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

[Collection("ManifestState")]
public class RemoteManifestCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rmc-" + Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        EffectiveManifest.SetRemote(null);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private static readonly Version Binary = new(0, 6, 0);

    private static (byte[] Spki, ECDsa Signer) NewKeyPair()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportSubjectPublicKeyInfo(), ecdsa);
    }

    private static (byte[] Bytes, byte[] Sig) SignManifest(ECDsa signer)
    {
        var m = new GameManifest
        {
            SchemaVersion = 1,
            MinBinaryVersion = "0.6.0",
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "cached-game", Name = "Cached Game", Engine = "bethesda",
                    Stores = new StoreIds { SteamAppId = "70010" },
                    Provenance = new ManifestProvenance { Sources = new[] { ManifestSources.KnownEngines } },
                },
            },
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(m, ManifestJson.Options);
        return (bytes, signer.SignData(bytes, HashAlgorithmName.SHA256, ManifestSignature.Format));
    }

    [Fact]
    public void Applies_a_validly_signed_cached_manifest()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer);
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "game-manifest.json"), bytes);
        File.WriteAllBytes(Path.Combine(_dir, "game-manifest.json.sig"), sig);

        var applied = RemoteManifestCache.ApplyCached(_dir, Binary, spki);

        Assert.True(applied);
        Assert.Contains(EffectiveManifest.Current.Games, g => g.Id == "cached-game");
    }

    [Fact]
    public void Returns_false_when_cache_is_missing()
    {
        var applied = RemoteManifestCache.ApplyCached(_dir, Binary); // dir doesn't exist
        Assert.False(applied);
        Assert.Same(EmbeddedGameManifest.Current, EffectiveManifest.Current);
    }

    [Fact]
    public void Returns_false_and_stays_on_embedded_for_a_tampered_cache()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer);
        bytes[15] ^= 0xFF; // tamper after signing
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "game-manifest.json"), bytes);
        File.WriteAllBytes(Path.Combine(_dir, "game-manifest.json.sig"), sig);

        var applied = RemoteManifestCache.ApplyCached(_dir, Binary, spki);

        Assert.False(applied);
        Assert.Same(EmbeddedGameManifest.Current, EffectiveManifest.Current);
    }
}
