namespace ModManager.Core;

/// <summary>
/// Pure readme selection: given a mod's file / zip-entry names, pick the one document worth
/// surfacing. README.md wins, then README.txt, then the first <c>.md</c>, then the first
/// <c>.txt</c>; nothing readable -> null. Selection only — the caller does the safe extraction
/// (basename + the existing zip-slip guards). Mod-supplied content is attacker-controlled, so the
/// viewer renders it as text/native controls only, never as markup that can execute.
/// </summary>
public static class ReadmeCapture
{
    public static string? PickReadme(IEnumerable<string>? entryNames)
    {
        var names = (entryNames ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (names.Count == 0) return null;

        static string Base(string n) => n.Replace('\\', '/').Split('/')[^1];
        bool Is(string n, string exact) => string.Equals(Base(n), exact, StringComparison.OrdinalIgnoreCase);
        bool Ext(string n, string ext) => Base(n).EndsWith(ext, StringComparison.OrdinalIgnoreCase);

        return names.FirstOrDefault(n => Is(n, "README.md"))
            ?? names.FirstOrDefault(n => Is(n, "README.txt"))
            ?? names.FirstOrDefault(n => Ext(n, ".md"))
            ?? names.FirstOrDefault(n => Ext(n, ".txt"));
    }
}
