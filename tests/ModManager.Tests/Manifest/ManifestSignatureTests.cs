using System.Security.Cryptography;
using System.Text;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class ManifestSignatureTests
{
    // Make an ephemeral P-256 keypair; return (spkiPublicKey, signer).
    private static (byte[] Spki, ECDsa Signer) NewKeyPair()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportSubjectPublicKeyInfo(), ecdsa);
    }

    private static byte[] Sign(ECDsa signer, byte[] data)
        => signer.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    [Fact]
    public void Valid_signature_over_the_bytes_verifies()
    {
        var (spki, signer) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("{\"schemaVersion\":1}");
        var sig = Sign(signer, data);

        Assert.True(ManifestSignature.Verify(spki, data, sig));
    }

    [Fact]
    public void Tampered_payload_fails()
    {
        var (spki, signer) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("{\"schemaVersion\":1}");
        var sig = Sign(signer, data);

        var tampered = Encoding.UTF8.GetBytes("{\"schemaVersion\":2}");
        Assert.False(ManifestSignature.Verify(spki, tampered, sig));
    }

    [Fact]
    public void Tampered_signature_fails()
    {
        var (spki, signer) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("payload");
        var sig = Sign(signer, data);
        sig[0] ^= 0xFF; // flip a bit

        Assert.False(ManifestSignature.Verify(spki, data, sig));
    }

    [Fact]
    public void Signature_from_a_different_key_fails()
    {
        var (_, signerA) = NewKeyPair();
        var (spkiB, _) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("payload");
        var sigFromA = Sign(signerA, data);

        Assert.False(ManifestSignature.Verify(spkiB, data, sigFromA)); // wrong public key
    }

    [Theory]
    [InlineData(new byte[0])]              // empty signature
    [InlineData(new byte[] { 1, 2, 3 })]   // garbage signature
    public void Malformed_signature_returns_false_not_throws(byte[] badSig)
    {
        var (spki, _) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("payload");
        Assert.False(ManifestSignature.Verify(spki, data, badSig));
    }

    [Fact]
    public void Garbage_public_key_returns_false_not_throws()
    {
        var data = Encoding.UTF8.GetBytes("payload");
        Assert.False(ManifestSignature.Verify(new byte[] { 9, 9, 9 }, data, new byte[64]));
    }
}
