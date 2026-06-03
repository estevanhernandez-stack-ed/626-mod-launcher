using ModManager.Core;

namespace ModManager.Tests;

// Detect-and-defer truth table: detected owner wins; else an unowned loader conducts; else a
// declared (profile-hint) Managed value is the conservative fallback to read-only; else we own it.
public class CoordinationTests
{
    [Fact]
    public void Detected_owner_is_coexist()
        => Assert.Equal(Posture.Coexist, Coordination.PostureFor(OwnerTool.Vortex, null, loaderCanConduct: false));

    [Fact]
    public void Unowned_loader_is_conductor()
        => Assert.Equal(Posture.Conductor, Coordination.PostureFor(null, null, loaderCanConduct: true));

    [Fact]
    public void Declared_managed_with_no_owner_falls_back_to_coexist()
        => Assert.Equal(Posture.Coexist, Coordination.PostureFor(null, "vortex", loaderCanConduct: false));

    [Fact]
    public void Nothing_known_is_own()
        => Assert.Equal(Posture.Own, Coordination.PostureFor(null, null, loaderCanConduct: false));

    [Fact]
    public void Detected_owner_beats_a_conductable_loader()
        => Assert.Equal(Posture.Coexist, Coordination.PostureFor(OwnerTool.Vortex, null, loaderCanConduct: true));

    [Fact]
    public void Conductable_loader_beats_a_stale_declared_hint()
        => Assert.Equal(Posture.Conductor, Coordination.PostureFor(null, "vortex", loaderCanConduct: true));

    [Fact]
    public void Owned_marker_not_taken_over_is_Coexist()
        => Assert.Equal(Posture.Coexist,
            Coordination.PostureFor(OwnerTool.Vortex, declaredManaged: null, loaderCanConduct: false, reDeployed: false));

    [Fact]
    public void Taken_over_loader_folder_is_Conductor_not_Coexist()
        // owner is null because the marker was archived; loader can drive its manifest
        => Assert.Equal(Posture.Conductor,
            Coordination.PostureFor(owner: null, declaredManaged: null, loaderCanConduct: true, reDeployed: false));

    [Fact]
    public void ReDeployed_still_lets_us_manage_Own_or_Conductor()
    {
        // We took it over; a marker came back. We KEEP managing (Own/Conductor); App shows a notice.
        Assert.Equal(Posture.Conductor,
            Coordination.PostureFor(OwnerTool.Vortex, declaredManaged: null, loaderCanConduct: true, reDeployed: true));
        Assert.Equal(Posture.Own,
            Coordination.PostureFor(OwnerTool.Vortex, declaredManaged: null, loaderCanConduct: false, reDeployed: true));
    }
}
