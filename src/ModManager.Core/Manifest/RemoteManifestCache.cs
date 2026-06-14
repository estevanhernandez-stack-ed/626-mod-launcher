namespace ModManager.Core.Manifest;

/// <summary>
/// On-disk cache for the remote game manifest. The App fetches the manifest + detached signature
/// into this cache (background, debounced) and applies the cached copy at startup via
/// <see cref="ApplyCached"/> — so a slow/offline network never blocks launch, and a bad/tampered
/// cache silently falls back to the embedded manifest. Pure Core (file IO + crypto, no network).
/// </summary>
public static class RemoteManifestCache
{
    public const string ManifestFileName = "game-manifest.json";
    public const string SignatureFileName = "game-manifest.json.sig";

    /// <summary>
    /// Read the cached manifest + signature from <paramref name="cacheDir"/> and, if they verify,
    /// make the manifest effective. Returns true iff a remote was applied. Missing/unreadable/invalid
    /// cache → false and the effective manifest stays on the embedded snapshot. Never throws.
    /// <paramref name="publicKey"/> defaults to the pinned production key; tests pass an explicit key.
    /// </summary>
    public static bool ApplyCached(string cacheDir, Version binaryVersion, byte[]? publicKey = null)
    {
        try
        {
            var manifestPath = Path.Combine(cacheDir, ManifestFileName);
            var sigPath = Path.Combine(cacheDir, SignatureFileName);
            if (!File.Exists(manifestPath) || !File.Exists(sigPath))
                return false;

            var bytes = File.ReadAllBytes(manifestPath);
            var sig = File.ReadAllBytes(sigPath);
            return ManifestLoader.TryApplyRemote(bytes, sig, binaryVersion, publicKey);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Write a freshly fetched manifest + detached signature to the cache atomically (temp file +
    /// rename, mirroring <see cref="AtomicJson"/>), so a crash mid-write can never leave a torn cache.
    /// </summary>
    public static void WriteCache(string cacheDir, byte[] manifestBytes, byte[] signature)
    {
        Directory.CreateDirectory(cacheDir);
        WriteAtomic(Path.Combine(cacheDir, ManifestFileName), manifestBytes);
        WriteAtomic(Path.Combine(cacheDir, SignatureFileName), signature);
    }

    private static void WriteAtomic(string file, byte[] bytes)
    {
        var tmp = file + ".tmp-" + Environment.ProcessId;
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, file, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* nothing to clean up */ }
            throw;
        }
    }
}
