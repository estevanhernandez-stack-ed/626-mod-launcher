using System.Text.Json;

namespace ModManager.Core.Persistence;

/// <summary>
/// Reads/writes the games registry (games.json) under a data root. Extracted from the App's
/// LauncherService so the App and the headless agent-access MCP share ONE reader — no second
/// source of truth, no drift. On-disk shape is camelCase (the launcher's historical Electron-shared
/// convention, written via <see cref="AtomicJson"/>); reads are case-insensitive for tolerance.
/// A missing or unreadable file yields an empty registry — <see cref="Load"/> never throws.
/// </summary>
public static class RegistryStore
{
    public const string FileName = "games.json";

    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    public static string PathFor(string dataRoot) => Path.Combine(dataRoot, FileName);

    public static GameRegistry Load(string dataRoot)
    {
        try { return JsonSerializer.Deserialize<GameRegistry>(File.ReadAllText(PathFor(dataRoot)), ReadOpts) ?? Registry.EmptyRegistry(); }
        catch { return Registry.EmptyRegistry(); }
    }

    /// <summary>Write the whole registry. Prefer <see cref="Update{T}"/> for any change to what is on
    /// disk: a bare Load → change → Save races every other writer (A6).</summary>
    public static void Save(string dataRoot, GameRegistry reg)
    {
        Directory.CreateDirectory(dataRoot);
        AtomicJson.WriteJsonAtomic(PathFor(dataRoot), reg);
    }

    /// <summary>
    /// Load, change and save the registry as ONE step that no other writer can interleave with (A6).
    ///
    /// <para><b>Why.</b> games.json is shared by every writer in the app (setting the active game,
    /// stamping a launch, re-detection, discovery, the registration repair). Each used to Load, change
    /// and Save on its own, so a writer holding a stale snapshot could land after another and restore
    /// what that one had just changed. For the repair that meant a game pointing back at a data folder
    /// its mods had just been moved out of.</para>
    ///
    /// <para><b>How.</b> An in-process lock per registry file, and an exclusive lock file beside it so a
    /// second launcher process waits too. The lock file is deleted when released. A writer that cannot
    /// get the lock within <paramref name="timeout"/> (default 10 s) throws <see cref="IOException"/>
    /// and writes nothing, rather than writing unlocked.</para>
    /// </summary>
    /// <param name="change">Given the registry as it is on disk now, returns the registry to write and a
    /// result handed back to the caller.</param>
    public static T Update<T>(string dataRoot, Func<GameRegistry, (GameRegistry Registry, T Result)> change, TimeSpan? timeout = null)
    {
        Directory.CreateDirectory(dataRoot);
        var path = Path.GetFullPath(PathFor(dataRoot));
        lock (Gates.GetOrAdd(path, _ => new object()))
        {
            using var held = AcquireFileLock(path + ".lock", timeout ?? TimeSpan.FromSeconds(10));
            var (next, result) = change(Load(dataRoot));
            Save(dataRoot, next);
            return result;
        }
    }

    /// <summary><see cref="Update{T}"/> for a change that mutates the registry in place and returns nothing.</summary>
    public static void Update(string dataRoot, Action<GameRegistry> change, TimeSpan? timeout = null)
        => Update<object?>(dataRoot, reg => { change(reg); return (reg, null); }, timeout);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    private static FileStream AcquireFileLock(string lockPath, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    bufferSize: 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
            }
            catch (IOException e)
            {
                throw new IOException("Another launcher window is saving its game list. Nothing was changed; try again.", e);
            }
        }
    }
}
