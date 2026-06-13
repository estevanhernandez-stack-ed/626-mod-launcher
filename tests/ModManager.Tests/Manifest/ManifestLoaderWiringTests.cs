using System.Security.Cryptography;
using System.Text.Json;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

[Collection("ManifestState")]
public class ManifestLoaderWiringTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    private static readonly Version Binary = new(0, 6, 0);

    private static (byte[] Spki, ECDsa Signer) NewKeyPair()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportSubjectPublicKeyInfo(), ecdsa);
    }

    private static (byte[] Bytes, byte[] Sig) SignManifest(ECDsa signer, GameManifest m)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(m, ManifestJson.Options);
        var sig = signer.SignData(bytes, HashAlgorithmName.SHA256, ManifestSignature.Format);
        return (bytes, sig);
    }

    private static GameManifest OneGame(string id = "wired-game") => new()
    {
        SchemaVersion = 1,
        MinBinaryVersion = "0.6.0",
        Games = new[]
        {
            new GameManifestEntry
            {
                Id = id, Name = id, Engine = "bethesda",
                Stores = new StoreIds { SteamAppId = "80001" },
                Provenance = new ManifestProvenance { Sources = new[] { ManifestSources.KnownEngines } },
            },
        },
    };

    [Fact]
    public void Convenience_overload_rejects_a_signature_not_from_the_pinned_key()
    {
        // Signed by a random key, not the pinned production key -> the 3-arg overload (which defaults
        // to the pinned key) must refuse it.
        var (_, attacker) = NewKeyPair();
        var (bytes, sig) = SignManifest(attacker, OneGame());

        Assert.Null(ManifestLoader.LoadVerifiedRemote(bytes, sig, Binary));
    }

    [Fact]
    public void TryApplyRemote_applies_a_genuinely_signed_manifest()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame("applied-game"));

        var applied = ManifestLoader.TryApplyRemote(bytes, sig, Binary, spki); // explicit test key

        Assert.True(applied);
        Assert.Contains(EffectiveManifest.Current.Games, g => g.Id == "applied-game");
    }

    [Fact]
    public void TryApplyRemote_does_not_apply_a_forged_manifest()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame("forged-game"));
        bytes[12] ^= 0xFF; // tamper after signing

        var applied = ManifestLoader.TryApplyRemote(bytes, sig, Binary, spki);

        Assert.False(applied);
        Assert.DoesNotContain(EffectiveManifest.Current.Games, g => g.Id == "forged-game");
        Assert.Same(EmbeddedGameManifest.Current, EffectiveManifest.Current); // stayed on embedded
    }

    [Fact]
    public void TryApplyRemote_with_default_pinned_key_rejects_a_non_pinned_signature()
    {
        var (_, attacker) = NewKeyPair();
        var (bytes, sig) = SignManifest(attacker, OneGame("nope"));

        var applied = ManifestLoader.TryApplyRemote(bytes, sig, Binary); // default -> pinned key

        Assert.False(applied);
        Assert.Same(EmbeddedGameManifest.Current, EffectiveManifest.Current);
    }
}
