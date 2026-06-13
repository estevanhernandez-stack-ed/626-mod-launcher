using System.Security.Cryptography;
using System.Text;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class ManifestSigningKeyTests
{
    [Fact]
    public void Pinned_key_imports_as_a_p256_verify_key()
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(ManifestSigningKey.PublicKeySpki, out var read);

        Assert.Equal(ManifestSigningKey.PublicKeySpki.Length, read); // consumed exactly, no trailing junk
        Assert.Equal(256, ecdsa.KeySize);
        Assert.Equal("1.2.840.10045.3.1.7", ecdsa.ExportParameters(false).Curve.Oid.Value); // NIST P-256
    }

    [Fact]
    public void Pinned_key_rejects_a_forged_signature()
    {
        // The production private key lives only in CI, so we can't make a genuine signature here.
        // The security-critical, testable property is REJECTION: the pinned key must refuse a
        // signature made by any other key. (Acceptance of a genuine signature is covered generically
        // in ManifestSignatureTests with an in-test keypair.)
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var data = Encoding.UTF8.GetBytes("{\"schemaVersion\":1}");
        var forged = attacker.SignData(data, HashAlgorithmName.SHA256, ManifestSignature.Format);

        Assert.False(ManifestSignature.Verify(ManifestSigningKey.PublicKeySpki, data, forged));
    }
}
