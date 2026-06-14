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
            ecdsa.ImportFromPem(NormalizeKey(privateKeyPem));
        }
        catch (ArgumentException ex)
        {
            // No recognized PEM label / multiple keys / encrypted key — a bad MANIFEST_SIGNING_KEY.
            // Normalize to CryptographicException so the CLI fails hard on any unusable key, whether
            // the body is malformed (already CryptographicException) or the envelope is (ArgumentException).
            throw new CryptographicException(
                "Could not import a private key from MANIFEST_SIGNING_KEY (expected an unencrypted PKCS#8 PEM, or that PEM base64-encoded).", ex);
        }
        return ecdsa.SignData(manifestBytes, HashAlgorithmName.SHA256, ManifestSignature.Format);
    }

    // CI secrets routinely mangle a multi-line PEM's newlines. Accept either a raw PEM or a
    // base64-encoded PEM (a single-line secret is newline-proof). Anything else is returned
    // unchanged so ImportFromPem reports the clear error.
    private static string NormalizeKey(string privateKeyPem)
    {
        var trimmed = (privateKeyPem ?? string.Empty).Trim();
        if (trimmed.Contains("-----BEGIN", StringComparison.Ordinal))
            return trimmed; // already a PEM (multi-line)

        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(trimmed));
            if (decoded.Contains("-----BEGIN", StringComparison.Ordinal))
                return decoded; // it was a base64-encoded PEM
        }
        catch (FormatException) { /* not base64 — fall through */ }

        return trimmed;
    }
}
