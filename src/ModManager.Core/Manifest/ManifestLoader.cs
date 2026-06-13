using System.Linq;
using System.Text.Json;

namespace ModManager.Core.Manifest;

/// <summary>
/// Loads and trusts a remote manifest payload. Pure: takes raw bytes + signature + the pinned public
/// key + this binary's version, returns a validated <see cref="GameManifest"/> or null. The App layer
/// fetches the bytes (network) and supplies them here; verification, gating, and validation are pure
/// and live in Core. ANY failure returns null so the caller silently falls back to the embedded
/// snapshot — a bad/old/tampered remote can never break a working install (spec §5, §6).
/// </summary>
public static class ManifestLoader
{
    /// <summary>The newest schema version this binary understands. A remote manifest declaring a
    /// higher version is ignored (forward-compat: an old binary never consumes a newer schema).</summary>
    public const int KnownSchemaVersion = 1;

    public static GameManifest? LoadVerifiedRemote(
        ReadOnlySpan<byte> manifestBytes,
        ReadOnlySpan<byte> signature,
        ReadOnlySpan<byte> pinnedPublicKey,
        Version currentBinaryVersion,
        IReadOnlySet<string> knownEngines)
    {
        // 1. Signature over the exact bytes.
        if (!ManifestSignature.Verify(pinnedPublicKey, manifestBytes, signature))
            return null;

        // 2. Parse.
        GameManifest? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<GameManifest>(manifestBytes, ManifestJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
        if (parsed is null)
            return null;

        // 3. Schema-version gate (don't consume a newer schema than we understand).
        if (parsed.SchemaVersion > KnownSchemaVersion)
            return null;

        // 4. minBinaryVersion gate (manifest demands a newer app than this one).
        if (!string.IsNullOrWhiteSpace(parsed.MinBinaryVersion))
        {
            if (!Version.TryParse(parsed.MinBinaryVersion, out var min))
                return null; // malformed version string — refuse rather than guess
            if (min > currentBinaryVersion)
                return null;
        }

        // 5. Validate (skips unknown-engine rows, rejects unsafe modPath).
        return ManifestValidator.Validate(parsed, knownEngines).Manifest;
    }

    /// <summary>
    /// Convenience overload: verify + load a remote manifest using the PINNED production key
    /// (<see cref="ManifestSigningKey.PublicKeySpki"/>) and this binary's known engine set
    /// (<see cref="EnginePresets.Presets"/>). Returns null on any failure (caller falls back to embedded).
    /// </summary>
    public static GameManifest? LoadVerifiedRemote(byte[] manifestBytes, byte[] signature, Version currentBinaryVersion)
        => LoadVerifiedRemote(
            manifestBytes,
            signature,
            ManifestSigningKey.PublicKeySpki,
            currentBinaryVersion,
            EnginePresets.Presets.Keys.ToHashSet());
}
