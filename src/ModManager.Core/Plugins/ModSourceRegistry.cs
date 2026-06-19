using ModManager.Plugins.Abstractions;

namespace ModManager.Core.Plugins;

/// <summary>Holds the mod sources contributed by loaded plugins. Empty when no plugin is loaded (the
/// Store SKU) — every consumer must tolerate an empty registry, which is what keeps the core a complete
/// product on its own.</summary>
public sealed class ModSourceRegistry
{
    private readonly List<IModSource> _sources = new();
    public IReadOnlyList<IModSource> Sources => _sources;
    public void Add(IModSource source) { if (ById(source.Id) is null) _sources.Add(source); }
    public IModSource? ById(string id) => _sources.FirstOrDefault(s => s.Id == id);
}
