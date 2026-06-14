using System.Security.Cryptography;
using System.Text;
using ManifestMiner;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Miner;

public class ManifestSignerTests
{
    private static (string PrivatePem, byte[] Spki) NewKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportPkcs8PrivateKeyPem(), ecdsa.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void Signature_verifies_against_the_launchers_verify_path()
    {
        var (privatePem, spki) = NewKeyPair();
        var bytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"games\":[]}");

        var sig = ManifestSigner.Sign(bytes, privatePem);

        // The launcher's own verifier must accept it — proves format + hash match (no DER/P1363 drift).
        Assert.True(ManifestSignature.Verify(spki, bytes, sig));
    }

    [Fact]
    public void Tampered_bytes_fail_verification()
    {
        var (privatePem, spki) = NewKeyPair();
        var bytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1}");
        var sig = ManifestSigner.Sign(bytes, privatePem);

        var tampered = Encoding.UTF8.GetBytes("{\"schemaVersion\":2}");
        Assert.False(ManifestSignature.Verify(spki, tampered, sig));
    }

    [Fact]
    public void Signature_from_one_key_fails_against_another()
    {
        var (privatePem, _) = NewKeyPair();
        var (_, otherSpki) = NewKeyPair();
        var bytes = Encoding.UTF8.GetBytes("payload");
        var sig = ManifestSigner.Sign(bytes, privatePem);

        Assert.False(ManifestSignature.Verify(otherSpki, bytes, sig));
    }

    [Fact]
    public void Garbage_private_key_throws_cryptographic_exception()
    {
        // The CLI surfaces this as a hard failure (a bad MANIFEST_SIGNING_KEY must not silently no-op).
        Assert.ThrowsAny<CryptographicException>(() => ManifestSigner.Sign(new byte[] { 1 }, "not a pem"));
    }
}
