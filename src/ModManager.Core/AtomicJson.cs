using System.Text.Json;

namespace ModManager.Core;

/// <summary>
/// Atomic JSON state writes. The launcher's registry, classification, metadata, and
/// profiles are small JSON files that must never be left half-written: a crash mid-write
/// corrupts the file and the loader silently falls back to empty, wiping the user's games.
/// Serialize first (a bad object throws before the destination is touched), write a
/// sibling temp file, then rename — rename is atomic on the same volume. Mirrors fs-atomic.js.
/// </summary>
public static class AtomicJson
{
    // camelCase: the launcher's JSON state files are shared with the Electron app, which
    // reads/writes camelCase. Staying camelCase keeps both apps reading the same data.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void WriteJsonAtomic<T>(string file, T value)
    {
        // Serialize FIRST. If the value can't be serialized (cycle, etc.) this throws here,
        // before we have touched the destination — a bad write can never corrupt good data.
        var json = JsonSerializer.Serialize(value, Options);
        WriteTextAtomic(file, json);
    }

    /// <summary>Atomically write text (e.g. the Mod Engine 2 config toml): temp file, then rename.</summary>
    public static void WriteTextAtomic(string file, string text)
    {
        // Create the destination directory first. An atomic write into a not-yet-existent directory
        // throws DirectoryNotFoundException on the temp file — and this is THE canonical wrapper for
        // every JSON/text state file, so a missing-parent footgun here crashes any caller (live-smoke
        // 2026-05-30: a Safe Clear died writing a marker into a game data dir that didn't exist yet).
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = file + ".tmp-" + Environment.ProcessId;
        try
        {
            File.WriteAllText(tmp, text);
            File.Move(tmp, file, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* nothing to clean up */ }
            throw;
        }
    }
}
