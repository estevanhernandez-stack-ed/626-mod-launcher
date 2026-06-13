using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class ManifestLoaderTests
{
    private static readonly Version Binary = new(0, 6, 0);
    private static readonly IReadOnlySet<string> Engines = EnginePresets.Presets.Keys.ToHashSet();

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

    private static GameManifest OneGame(int schema = 1, string? minBinary = "0.6.0", string engine = "bethesda")
        => new()
        {
            SchemaVersion = schema,
            MinBinaryVersion = minBinary,
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "test-game",
                    Name = "Test Game",
                    Engine = engine,
                    Stores = new StoreIds { SteamAppId = "12345" },
                    Provenance = new ManifestProvenance { Sources = new[] { "nexus-domains" } },
                },
            },
        };

    [Fact]
    public void Valid_signed_manifest_loads()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame());

        var result = ManifestLoader.LoadVerifiedRemote(bytes, sig, spki, Binary, Engines);

        Assert.NotNull(result);
        Assert.Contains(result!.Games, g => g.Id == "test-game");
    }

    [Fact]
    public void Bad_signature_returns_null()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame());
        bytes[10] ^= 0xFF; // tamper payload after signing

        Assert.Null(ManifestLoader.LoadVerifiedRemote(bytes, sig, spki, Binary, Engines));
    }

    [Fact]
    public void Unparseable_json_returns_null()
    {
        var (spki, signer) = NewKeyPair();
        var bytes = Encoding.UTF8.GetBytes("this is not json");
        var sig = signer.SignData(bytes, HashAlgorithmName.SHA256, ManifestSignature.Format);

        Assert.Null(ManifestLoader.LoadVerifiedRemote(bytes, sig, spki, Binary, Engines));
    }

    [Fact]
    public void Newer_schema_version_returns_null()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame(schema: ManifestLoader.KnownSchemaVersion + 1));

        Assert.Null(ManifestLoader.LoadVerifiedRemote(bytes, sig, spki, Binary, Engines));
    }

    [Fact]
    public void Higher_min_binary_version_returns_null()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame(minBinary: "99.0.0"));

        Assert.Null(ManifestLoader.LoadVerifiedRemote(bytes, sig, spki, Binary, Engines));
    }

    [Fact]
    public void Missing_min_binary_version_is_allowed()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame(minBinary: null));

        Assert.NotNull(ManifestLoader.LoadVerifiedRemote(bytes, sig, spki, Binary, Engines));
    }

    [Fact]
    public void Unknown_engine_rows_are_skipped_but_manifest_still_loads()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame(engine: "engine-from-the-future"));

        var result = ManifestLoader.LoadVerifiedRemote(bytes, sig, spki, Binary, Engines);

        Assert.NotNull(result);                                   // load succeeds
        Assert.DoesNotContain(result!.Games, g => g.Id == "test-game"); // unknown-engine row dropped
    }
}
