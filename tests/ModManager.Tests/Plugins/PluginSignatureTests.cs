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
}
