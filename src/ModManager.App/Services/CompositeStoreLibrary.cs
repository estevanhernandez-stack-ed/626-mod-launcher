using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>Every store library as one, for the surfaces that are store-agnostic (the discovery lane,
/// covers, last-played). Code that is genuinely about Steam keeps taking <see cref="SteamService"/>.</summary>
public sealed class CompositeStoreLibrary : IStoreLibrary
{
    private readonly IReadOnlyList<IStoreLibrary> _libraries;
    public CompositeStoreLibrary(IEnumerable<IStoreLibrary> libraries) => _libraries = libraries.ToList();

    public string StoreKind => "all";

    public IReadOnlyList<InstalledGame> InstalledGames()
    {
        var all = new List<InstalledGame>();
        foreach (var lib in _libraries)
        {
            try { all.AddRange(lib.InstalledGames()); }
            catch { /* one failing store never hides another */ }
        }
        return all;
    }

    public string? ResolveCoverArtPath(string appId)
        => _libraries.Select(l => l.ResolveCoverArtPath(appId)).FirstOrDefault(p => p is not null);

    public string? ResolveCoverArtPath(string appId, CoverShape shape)
        => _libraries.Select(l => l.ResolveCoverArtPath(appId, shape)).FirstOrDefault(p => p is not null);
}
