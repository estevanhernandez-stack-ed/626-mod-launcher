using ModManager.Core.Manifest;

namespace ModManager.Core.Plugins;

/// <summary>Detached-signature verify for plugin assemblies — the same ECDSA P-256/SHA-256 scheme as the
/// manifest feed (<see cref="ManifestSignature"/>), applied to the plugin .dll bytes. The host verifies
/// a plugin against the pinned <see cref="PluginSigningKey"/> before loading it; a bad/missing signature
/// means the assembly is never loaded.</summary>
public static class PluginSignature
{
    /// <summary>Verify the assembly bytes against the pinned plugin-signing public key.</summary>
    public static bool Verify(ReadOnlySpan<byte> assemblyBytes, ReadOnlySpan<byte> signature)
        => VerifyWithKey(PluginSigningKey.PublicKeySpki, assemblyBytes, signature);

    /// <summary>Verify against an explicit key (test seam).</summary>
    public static bool VerifyWithKey(ReadOnlySpan<byte> spki, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
        => ManifestSignature.Verify(spki, data, signature);
}
