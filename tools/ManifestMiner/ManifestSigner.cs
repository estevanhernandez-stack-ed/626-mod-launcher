using System.Security.Cryptography;
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Signs the manifest bytes with ECDSA P-256 / SHA-256, using the EXACT
/// <see cref="ManifestSignature.Format"/> the launcher verifies with — so sign and verify cannot
/// drift. The private key (PKCS#8 PEM) comes from CI (the MANIFEST_SIGNING_KEY secret); it never
/// touches source. Sign the literal published bytes — the caller must pass the same bytes it writes
/// to games-manifest.json (no re-serialize on either side).</summary>
public static class ManifestSigner
{
    public static byte[] Sign(ReadOnlySpan<byte> manifestBytes, string privateKeyPem)
    {
        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportFromPem(privateKeyPem);
        }
        catch (ArgumentException ex)
        {
            // No recognized PEM label / multiple keys / encrypted key — a bad MANIFEST_SIGNING_KEY.
            // Normalize to CryptographicException so the CLI fails hard on any unusable key, whether
            // the body is malformed (already CryptographicException) or the envelope is (ArgumentException).
            throw new CryptographicException("Could not import a private key from MANIFEST_SIGNING_KEY (expected an unencrypted PKCS#8 PEM).", ex);
        }
        return ecdsa.SignData(manifestBytes, HashAlgorithmName.SHA256, ManifestSignature.Format);
    }
}
