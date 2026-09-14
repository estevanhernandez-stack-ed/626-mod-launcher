namespace ModManager.Core;

/// <summary>
/// A toggle refused because two copies of the same mod would collide: one live in the game folder,
/// one turned off and held. Nothing was moved, and both copies are where they were.
///
/// <para>Its own type so <see cref="ErrorRemedy"/> can pass the message through as written. The generic
/// advice ("try again after a Refresh") would send the user round in a circle: refreshing changes
/// nothing, because the fix is choosing which copy to keep.</para>
/// </summary>
public sealed class HeldCopyCollisionException : InvalidOperationException
{
    public HeldCopyCollisionException(string message) : base(message) { }
}
