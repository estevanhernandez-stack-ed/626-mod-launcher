// src/ModManager.Core/Plugins/PluginFeedInstaller.cs
using System.IO;

namespace ModManager.Core.Plugins;

/// <summary>A plugin the installer placed on disk (ready for the host to load).</summary>
public sealed record InstalledPlugin(string Id, string Version, string DllPath);

/// <summary>Inputs for one feed run. <c>VerifyKey</c> is the pinned plugin SPKI in production (the App
/// passes <c>PluginSigningKey.PublicKeySpki</c>); tests inject a throwaway public key.</summary>
public sealed record PluginFeedRequest(
    string IndexUrl, byte[] VerifyKey, Version BinaryVersion, string PluginsDir, string InstalledRecordPath);

/// <summary>
/// The headless fetch→verify→gate→download→verify→install pipeline. Takes an injected
/// <see cref="PluginDownload"/> so Core never references <c>HttpClient</c> and the whole thing is
/// unit-testable with in-memory bytes. Never throws — any failure (offline, bad index sig, bad schema,
/// too-high min version, sha mismatch, bad dll sig) skips that unit and the method returns whatever
/// installed cleanly. The pinned dll signature is re-verified by the host at load (defense in depth),
/// but we verify here too so a bad download is never written to disk.
/// </summary>
public static class PluginFeedInstaller
{
    /// <summary>Fetch the bytes at <paramref name="url"/>, or null on any failure (the App wraps an
    /// <c>HttpClient</c> and swallows network errors into null).</summary>
    public delegate Task<byte[]?> PluginDownload(string url, CancellationToken ct);

    public static async Task<IReadOnlyList<InstalledPlugin>> RunAsync(
        PluginFeedRequest req, PluginDownload download, CancellationToken ct = default)
    {
        var installed = new List<InstalledPlugin>();

        var indexBytes = await download(req.IndexUrl, ct).ConfigureAwait(false);
        var indexSig = await download(req.IndexUrl + ".sig", ct).ConfigureAwait(false);
        if (indexBytes is null || indexSig is null) return installed;
        if (!PluginSignature.VerifyWithKey(req.VerifyKey, indexBytes, indexSig)) return installed;
        if (!PluginIndex.TryParse(indexBytes, out var index)) return installed;

        var have = InstalledPluginsStore.Read(req.InstalledRecordPath);
        var toInstall = PluginGate.SelectInstallable(index!, req.BinaryVersion, have);
        if (toInstall.Count == 0) return installed;

        // Mutable copy of the record so multiple installs in one run all persist.
        var record = new Dictionary<string, string>(have);

        foreach (var e in toInstall)
        {
            var dll = await download(e.DownloadUrl, ct).ConfigureAwait(false);
            var dllSig = await download(e.SigUrl, ct).ConfigureAwait(false);
            if (dll is null || dllSig is null) continue;
            if (!PluginIntegrity.Sha256Matches(dll, e.Sha256)) continue;
            if (!PluginSignature.VerifyWithKey(req.VerifyKey, dll, dllSig)) continue;

            string dllPath = Path.Combine(req.PluginsDir, e.Id + ".dll");
            string sigPath = dllPath + ".sig";
            try
            {
                Directory.CreateDirectory(req.PluginsDir);
                AtomicWriteBytes(dllPath, dll);     // verify-before-replace: only verified bytes land
                AtomicWriteBytes(sigPath, dllSig);
            }
            catch { continue; } // a write failure leaves any prior install intact

            record[e.Id] = e.Version;
            installed.Add(new InstalledPlugin(e.Id, e.Version, dllPath));
        }

        if (installed.Count > 0)
        {
            try { InstalledPluginsStore.Write(req.InstalledRecordPath, record); } catch { /* best-effort */ }
        }
        return installed;
    }

    // Atomic byte write: temp sibling + rename (mirrors AtomicJson for non-JSON payloads).
    private static void AtomicWriteBytes(string path, byte[] bytes)
    {
        var tmp = path + ".tmp-" + Environment.ProcessId;
        try { File.WriteAllBytes(tmp, bytes); File.Move(tmp, path, overwrite: true); }
        catch { try { File.Delete(tmp); } catch { } throw; }
    }
}
