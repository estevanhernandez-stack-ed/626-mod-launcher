namespace ModManager.Core.Manifest;

/// <summary>
/// The pinned public key for verifying remote game-manifest signatures (ECDSA P-256 / SHA-256).
/// This is the trust anchor for the remote feed: <see cref="ManifestLoader.LoadVerifiedRemote"/>
/// accepts a remote manifest only if its detached signature verifies against this key.
///
/// The matching private key lives ONLY in CI (a GitHub Actions secret) and is used by the manifest
/// signer; it never appears in source (the project's no-embedded-secret rule). A public key is safe
/// to commit. Rotation = mint a new keypair and ship a release that re-pins <see cref="PublicKeySpki"/>
/// (the key is pinned in the binary, so rotation is a release — matches the spec's no-key-rotation-
/// machinery non-goal).
/// </summary>
public static class ManifestSigningKey
{
    // SubjectPublicKeyInfo (DER), base64. Generated 2026-06-13 — ECDSA P-256 (secp256r1).
    // Validated on pin: imports as a 256-bit key on curve 1.2.840.10045.3.1.7 (see ManifestSigningKeyTests).
    private const string PublicKeySpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEhFT1W6cjYU4ZHuawx2RioM4xL77U" +
        "LJDfN5uwgA9+2Er1/hleRQ+h336ly43kRVB0fN05i6bp4M2GHn3tOrprTg==";

    /// <summary>
    /// The pinned public key as SubjectPublicKeyInfo bytes. Pass to
    /// <see cref="ManifestSignature.Verify"/> as the trust anchor when verifying a remote manifest.
    /// </summary>
    public static byte[] PublicKeySpki { get; } = Convert.FromBase64String(PublicKeySpkiBase64);
}
