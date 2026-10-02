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
    /// stamping a launch, re-detection, discovery, the registration repair, restore points). Each used
    /// to Load, change and Save on its own, so a writer holding a stale snapshot could land after
    /// another and restore what that one had just changed. For the repair that meant a game pointing
    /// back at a data folder its mods had just been moved out of.</para>
    ///
    /// <para><b>How.</b> An in-process lock per registry file and an exclusive lock file beside it, so a
    /// second launcher process waits too (<see cref="WithLock"/>). Both are bounded by
    /// <paramref name="timeout"/> (default 10 s); a writer that cannot get them throws
    /// <see cref="IOException"/> and writes nothing, rather than writing unlocked.</para>
    ///
    /// <para><b>Never writes on a bad read.</b> <see cref="Load"/> answers an unreadable file with an
    /// empty registry, which is right for a reader and catastrophic for a writer: saving that would wipe
    /// every registered game for one routine change. Update reads strictly: a missing file is an empty
    /// registry, but a file that exists and cannot be read or parsed throws and nothing is written.</para>
    /// </summary>
    /// <param name="change">Given the registry as it is on disk now, returns the registry to write and a
    /// result handed back to the caller.</param>
    public static T Update<T>(string dataRoot, Func<GameRegistry, (GameRegistry Registry, T Result)> change, TimeSpan? timeout = null)
    {
        T result = default!;
        WithLock(dataRoot, () =>
        {
            var (next, r) = change(LoadForUpdate(dataRoot));
            Save(dataRoot, next);
            result = r;
        }, timeout);
        return result;
    }

    /// <summary><see cref="Update{T}"/> for a change that mutates the registry in place and returns nothing.</summary>
    public static void Update(string dataRoot, Action<GameRegistry> change, TimeSpan? timeout = null)
        => Update<object?>(dataRoot, reg => { change(reg); return (reg, null); }, timeout);

    /// <summary>
    /// Run <paramref name="action"/> holding the registry's locks: for anything that touches games.json
    /// as a FILE rather than through <see cref="Update{T}"/> (restore points copy it in and delete it).
    /// Same in-process and cross-process locks, same bound, same refusal.
    /// </summary>
    public static void WithLock(string dataRoot, Action action, TimeSpan? timeout = null)
    {
        Directory.CreateDirectory(dataRoot);
        var path = Path.GetFullPath(PathFor(dataRoot));
        var limit = timeout ?? TimeSpan.FromSeconds(10);
        var gate = Gates.GetOrAdd(path, _ => new object());
        // Bounded, like the file lock: a writer stuck on a slow folder must not freeze another writer
        // (on the UI thread, the window) forever.
        if (!Monitor.TryEnter(gate, limit)) throw Busy(null);
        try
        {
            using var held = FileLock.Acquire(path + ".lock", limit, Busy);
            action();
        }
        finally { Monitor.Exit(gate); }
    }

    private static GameRegistry LoadForUpdate(string dataRoot)
    {
        var path = PathFor(dataRoot);
        if (!File.Exists(path)) return Registry.EmptyRegistry();
        try
        {
            return JsonSerializer.Deserialize<GameRegistry>(File.ReadAllText(path), ReadOpts)
                   ?? throw new JsonException("games.json is empty.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new IOException("Couldn't read the game list (games.json), so nothing was changed. "
                                  + "If you edited it by hand, check it is valid JSON.", e);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    private static IOException Busy(Exception? inner)
        => new("Another launcher window is saving its game list. Nothing was changed; try again.", inner);
}
