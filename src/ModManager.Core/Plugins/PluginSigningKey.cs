namespace ModManager.Core.Plugins;

/// <summary>The pinned plugin-signing public key (SubjectPublicKeyInfo / DER). The launcher verifies a
/// plugin's detached signature against this key before loading it; a bad/missing signature fails closed
/// (no plugin loads).
///
/// <para>This is a SEPARATE key from <see cref="Manifest.ManifestSigningKey"/> on purpose. The manifest
/// key signs game-definition <em>data</em> and its private half lives in the public feed repo's CI
/// secret; this one signs executable plugin <em>code</em>. Keeping them distinct means a leak of the
/// feed-signing secret can never be used to sign a malicious plugin (code execution) — only to forge
/// data. Do not collapse the two into one key.</para>
///
/// <para>Rotation = mint a new ECDSA P-256 keypair, re-pin the SPKI here, ship a release (the key is
/// pinned in the binary; there is no separate revocation channel).</para></summary>
public static class PluginSigningKey
{
    // SubjectPublicKeyInfo (DER), base64. ECDSA P-256 (secp256r1). Minted 2026-06-18 for the off-Store
    // plugin source; the private PKCS#8 half is held only in the maintainer's password manager and is
    // never committed. Verified well-formed by PluginSignatureTests.Pinned_plugin_signing_key_*.
    private const string PublicKeySpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEcTkhIs2RG99S6MWBJBol2H/kOvBdgrY08FwqNdOKR/KSAwyIOb33yiCgxoHGWihOc4cm648//vWcSHuCJBPJwA==";

    private static readonly byte[] _spki = Convert.FromBase64String(PublicKeySpkiBase64);

    /// <summary>The pinned public key as SubjectPublicKeyInfo bytes (empty would fail every verify closed).</summary>
    public static ReadOnlySpan<byte> PublicKeySpki => _spki;
}
