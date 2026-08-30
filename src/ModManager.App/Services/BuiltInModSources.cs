using System.Net.Http;
using System.Reflection;
using ModManager.Core.Plugins;
using ModManager.Plugins.Abstractions;

namespace ModManager.App.Services;

/// <summary>
/// Registers the mod sources COMPILED INTO this build. Both SKUs, since the Nexus partner
/// approval landed.
///
/// <para>Nexus used to arrive two different ways: downloaded as a signed plugin off-Store, compiled in
/// for the Store. That split was never Microsoft's rule — it was <b>Nexus's</b>: their integration
/// could not ship until they approved us as a partner, and the plugin kept it off a certified package
/// meanwhile. The approval landed, so both builds compile it in and nothing is fetched, verified, or
/// loaded from disk to make Nexus work. The Store package additionally ships no loader at all —
/// <c>scripts/check-store-seal.ps1</c> fails the build if the loader's symbols appear.</para>
///
/// <para>It deliberately calls the SAME <see cref="IModManagerPlugin.Register"/> entry point the off-Store
/// loader calls, through the same <see cref="ModSourceHostServices"/>. That is the point: the two SKUs
/// differ ONLY in how the code arrives, never in how it is wired up or authorized, so a Nexus behavior fix
/// lands identically in both without a second code path to keep in sync.</para>
/// </summary>
internal static class BuiltInModSources
{
    /// <summary>Register every compiled-in source. Never throws — a source that fails to register leaves
    /// the app on the zero-sources path, and every Nexus surface is capability-gated on the registry, so
    /// it stays hidden rather than half-working.</summary>
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
