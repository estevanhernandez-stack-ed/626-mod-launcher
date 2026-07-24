using ModManager.Core.Nexus;
using Xunit;

namespace ModManager.Tests.Nexus;

public class PluginConsentTests
{
    // Consent is required iff no plugin is installed yet: the first-ever install is the only time the
    // user must agree to connect + download the signed add-on. Already-installed updates never re-prompt.
    [Theory]
    [InlineData(0, true)]   // first-ever install -> must consent
    [InlineData(1, false)]  // already installed -> update, no re-prompt
    [InlineData(3, false)]  // several installed -> still no re-prompt
    public void NeedsFirstInstallConsent_only_on_zero_installed(int installed, bool expected) =>
        Assert.Equal(expected, PluginConsent.NeedsFirstInstallConsent(installed));
}
