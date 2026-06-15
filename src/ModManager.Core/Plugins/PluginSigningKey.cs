namespace ModManager.Core.Plugins;

/// <summary>The pinned plugin-signing public key (SubjectPublicKeyInfo / DER). Mint the real ECDSA P-256
/// keypair when the plugin source goes live (sub-project 5) and paste the SPKI bytes here; until then an
/// empty key means every signature fails closed (no plugin loads), which is the safe default.</summary>
public static class PluginSigningKey
{
    public static ReadOnlySpan<byte> PublicKeySpki => Array.Empty<byte>();
}
