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
            // Defense-in-depth: the index is signature-verified (maintainer-trust-bounded), but never
            // let an entry Id escape PluginsDir. Only [a-z0-9-] ids compose a filename — anything with a
            // separator, "..", or a stray char is skipped before we touch the filesystem.
            if (!IsSafeId(e.Id)) continue;

            var dll = await download(e.DownloadUrl, ct).ConfigureAwait(false);
            var dllSig = await download(e.SigUrl, ct).ConfigureAwait(false);
            if (dll is null || dllSig is null) continue;
            if (!PluginIntegrity.Sha256Matches(dll, e.Sha256)) continue;
            if (!PluginSignature.VerifyWithKey(req.VerifyKey, dll, dllSig)) continue;

            string dllPath = Path.Combine(req.PluginsDir, e.Id + ".dll");
            string sigPath = dllPath + ".sig";

            // Belt-and-suspenders: the composed full path must stay strictly under PluginsDir.
            var pluginsRoot = Path.GetFullPath(req.PluginsDir);
            var rootWithSep = pluginsRoot.EndsWith(Path.DirectorySeparatorChar)
                ? pluginsRoot : pluginsRoot + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(dllPath).StartsWith(rootWithSep, StringComparison.Ordinal)) continue;

            // Stage BOTH temp files first, then rename the pair back-to-back. The dll and its .sig must
            // land as a matched set: on an UPDATE, a new dll + an old/missing sig would fail the host's
            // load-time verify and silently break a previously-working plugin. If EITHER staging write
            // throws, we delete whatever temp we wrote and skip — the existing live pair is never touched.
            // Residual risk: a hard process-kill in the microsecond between the two final renames can leave
            // a new dll + old sig. It self-heals on the next successful feed run (the sha/version still
            // points at the new pair, so the entry is re-selected and re-written cleanly).
            string dllTmp = dllPath + ".tmp-" + Environment.ProcessId;
            string sigTmp = sigPath + ".tmp-" + Environment.ProcessId;
            try
            {
                Directory.CreateDirectory(req.PluginsDir);
                File.WriteAllBytes(dllTmp, dll);    // verify-before-replace: only verified bytes land
                File.WriteAllBytes(sigTmp, dllSig);
            }
            catch
            {
                TryDelete(dllTmp); TryDelete(sigTmp);
                continue; // staging failed → the existing live install stays intact
            }

            try
            {
                File.Move(dllTmp, dllPath, overwrite: true);
                File.Move(sigTmp, sigPath, overwrite: true);
            }
            catch
            {
                TryDelete(dllTmp); TryDelete(sigTmp);
                continue; // a rename failure leaves any prior install intact (best-effort cleanup above)
            }

            record[e.Id] = e.Version;
            installed.Add(new InstalledPlugin(e.Id, e.Version, dllPath));
        }

        if (installed.Count > 0)
        {
            try { InstalledPluginsStore.Write(req.InstalledRecordPath, record); } catch { /* best-effort */ }
        }
        return installed;
    }

    // An entry Id is safe to compose into a filename only if it's a non-empty run of lowercase
    // alphanumerics and hyphens — no separators, no "..", no anything that could escape PluginsDir.
    private static bool IsSafeId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        foreach (var c in id)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-';
            if (!ok) return false;
        }
        return true;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best-effort */ }
    }
}
