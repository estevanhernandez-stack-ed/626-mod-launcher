using ModManager.Plugins.Abstractions;

namespace ModManager.Plugin.Nexus;

/// <summary>
/// The plugin entry type the host instantiates and calls <see cref="Register"/> on. It contributes a
/// single <see cref="NexusModSource"/> to the host registry, wired to the shared
/// <see cref="IPluginHostServices.HttpClient"/> and a per-call credential lookup
/// (<c>host.GetCredential("nexus")</c>) — the key is read fresh per request and never stored here.
///
/// <para>References ONLY <c>ModManager.Plugins.Abstractions</c> + the BCL — never
/// <c>ModManager.Core</c>.</para>
/// </summary>
public sealed class NexusPlugin : IModManagerPlugin
{
    public string Id => "nexus";
    public string DisplayName => "Nexus Mods";

    public void Register(IPluginHostServices host)
        => host.AddModSource(new NexusModSource(host.HttpClient, () => host.GetCredential("nexus"), host.AppVersion));
}
