namespace ModManager.Core.Nexus;

public enum NexusAuthStatus { NotConfigured, Configured_Disconnected, Connected }

/// <summary>
/// Decides whether user-scoped Nexus features (endorse, identify, updates) are usable. During the
/// "dark window" — client_id not yet delivered — features are disabled with a "finalizing sign-in"
/// message; unauthenticated GraphQL search is unaffected and never routed through this gate.
/// </summary>
public static class NexusAuthGate
{
    public static bool CanUseUserScopedFeatures(bool configured, bool connected) => configured && connected;

    public static NexusAuthStatus Status(bool configured, bool connected) =>
        !configured ? NexusAuthStatus.NotConfigured
        : connected ? NexusAuthStatus.Connected
        : NexusAuthStatus.Configured_Disconnected;
}
