using ModManager.Core.Nexus;
using Xunit;

namespace ModManager.Tests.Nexus;

public class NexusAuthGateTests
{
    [Theory]
    [InlineData(false, false, false)] // client_id not delivered yet -> dark window
    [InlineData(false, true,  false)] // (can't be connected if not configured, but guard anyway)
    [InlineData(true,  false, false)] // configured but user not signed in
    [InlineData(true,  true,  true )] // configured + signed in -> features live
    public void CanUseUserScopedFeatures(bool configured, bool connected, bool expected) =>
        Assert.Equal(expected, NexusAuthGate.CanUseUserScopedFeatures(configured, connected));

    [Fact]
    public void Status_reports_not_configured_as_dark_window()
    {
        Assert.Equal(NexusAuthStatus.NotConfigured, NexusAuthGate.Status(false, false));
        Assert.Equal(NexusAuthStatus.Configured_Disconnected, NexusAuthGate.Status(true, false));
        Assert.Equal(NexusAuthStatus.Connected, NexusAuthGate.Status(true, true));
    }
}
