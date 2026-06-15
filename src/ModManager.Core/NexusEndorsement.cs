namespace ModManager.Core;

/// <summary>
/// One row from Nexus <c>GET /v1/user/endorsements.json</c> — the current user's endorse state for
/// a single mod, across every game. <paramref name="Status"/> is Nexus's word
/// (<c>Endorsed</c> / <c>Abstained</c> / <c>Undecided</c>). The bulk list is read-only state sync:
/// <see cref="NexusRefresh.ApplyEndorsements"/> folds the entries matching the active
/// <paramref name="DomainName"/> back onto the library's metas. It never writes — only an explicit
/// per-click endorse/abstain action writes.
/// </summary>
public sealed record NexusEndorsement(int ModId, string DomainName, string Status);
