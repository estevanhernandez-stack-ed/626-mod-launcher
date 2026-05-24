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
        var tmp = file + ".tmp-" + Environment.ProcessId;
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, file, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* nothing to clean up */ }
            throw;
        }
    }
}
