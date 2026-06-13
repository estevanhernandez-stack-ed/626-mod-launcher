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
}
