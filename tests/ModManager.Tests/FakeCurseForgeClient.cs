using ModManager.Core;

namespace ModManager.Tests;

// A configurable fake of ICurseForgeClient — set only the funcs a test needs.
internal sealed class FakeCurseForgeClient : ICurseForgeClient
{
    public Func<string, Task<int?>>? OnResolveGameId;
    public Func<int, string, Task<IReadOnlyList<CfMod>>>? OnSearch;
    public Func<IEnumerable<long>, Task<IReadOnlyList<FingerprintMatch>>>? OnGetFingerprintMatches;
    public Func<IEnumerable<int>, Task<IReadOnlyList<ModMeta>>>? OnGetMods;

    public Task<int?> ResolveGameIdAsync(string gameName) => OnResolveGameId!(gameName);
    public Task<IReadOnlyList<CfMod>> SearchAsync(int gameId, string query) => OnSearch!(gameId, query);
    public Task<IReadOnlyList<FingerprintMatch>> GetFingerprintMatchesAsync(IEnumerable<long> fps) => OnGetFingerprintMatches!(fps);
    public Task<IReadOnlyList<ModMeta>> GetModsAsync(IEnumerable<int> modIds) => OnGetMods!(modIds);
    public Task<ModMeta?> GetModAsync(int modId) => throw new NotSupportedException();
}
