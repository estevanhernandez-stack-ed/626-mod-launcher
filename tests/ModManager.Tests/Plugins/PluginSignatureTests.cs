using System.Security.Cryptography;
using ModManager.Core.Manifest;
using ModManager.Core.Plugins;

namespace ModManager.Tests.Plugins;

public class PluginSignatureTests
{
    [Fact]
    public void Valid_signature_verifies_and_a_tampered_one_does_not()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = ecdsa.ExportSubjectPublicKeyInfo();
        var payload = new byte[] { 1, 2, 3, 4, 5 };  // stand-in for assembly bytes
        var sig = ecdsa.SignData(payload, HashAlgorithmName.SHA256, ManifestSignature.Format);

        Assert.True(PluginSignature.VerifyWithKey(spki, payload, sig));               // valid
        var tampered = (byte[])payload.Clone(); tampered[0] ^= 0xFF;
        Assert.False(PluginSignature.VerifyWithKey(spki, tampered, sig));             // payload changed
        Assert.False(PluginSignature.VerifyWithKey(spki, payload, new byte[64]));     // bad sig
    }

    [Fact]
    public void Pinned_plugin_signing_key_is_a_valid_nistP256_spki()
    {
        // Guards the re-pin: the shipped key must import as a real P-256 public key. A blanked or
        // garbled SPKI would fail every plugin signature closed — no plugin ever loads in FULL, silently.
        var spki = PluginSigningKey.PublicKeySpki;
        Assert.False(spki.IsEmpty);                                  // not the old fail-closed placeholder

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(spki, out var read);        // throws if malformed
        Assert.Equal(spki.Length, read);                             // consumed the whole SPKI, no trailing junk
        Assert.Equal(256, ecdsa.KeySize);
        Assert.Equal("1.2.840.10045.3.1.7", ecdsa.ExportParameters(false).Curve.Oid.Value); // nistP256
    }
}
