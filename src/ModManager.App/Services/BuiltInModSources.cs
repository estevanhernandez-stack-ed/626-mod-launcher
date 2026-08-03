#if STORE_NEXUS
using System.Net.Http;
using System.Reflection;
using ModManager.Core.Plugins;
using ModManager.Plugins.Abstractions;

namespace ModManager.App.Services;

/// <summary>
/// Registers mod sources that are COMPILED INTO this build, for the packaged (Microsoft Store) SKU.
///
/// <para>The off-Store build obtains Nexus by downloading a signed assembly at runtime and loading it.
/// A Store package must not do that — Store policy expects everything the app runs to ship inside the
/// reviewed package (no dynamically downloaded or executed code) — so here the Nexus source is a compile
/// -time dependency and is simply instantiated. Nothing is fetched, verified, or loaded from disk, and no
/// loader ships in this binary at all: <c>scripts/check-store-seal.ps1</c> still fails the build if the
/// loader's symbols appear.</para>
///
/// <para>It deliberately calls the SAME <see cref="IModManagerPlugin.Register"/> entry point the off-Store
/// loader calls, through the same <see cref="ModSourceHostServices"/>. That is the point: the two SKUs
/// differ ONLY in how the code arrives, never in how it is wired up or authorized, so a Nexus behavior fix
/// lands identically in both without a second code path to keep in sync.</para>
/// </summary>
internal static class BuiltInModSources
{
    /// <summary>Register every compiled-in source. Never throws — a source that fails to register leaves
    /// the app on the zero-sources path, which is exactly how the Store SKU behaved before Nexus was
    /// compiled in (every Nexus surface is capability-gated on the registry, so it just stays hidden).</summary>
    public static void RegisterAll(
        ModSourceRegistry registry, HttpClient httpClient, NexusService nexus)
    {
        try
        {
            var host = new ModSourceHostServices(
                registry, httpClient, nexus,
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0");

            new ModManager.Plugin.Nexus.NexusPlugin().Register(host);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("built-in-sources", ex);
        }
    }
}
#endif
