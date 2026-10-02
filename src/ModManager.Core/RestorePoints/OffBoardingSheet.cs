using System.Linq;
using System.Text;

namespace ModManager.Core.RestorePoints;

/// <summary>Renders the plain-text "how to launch your game after a reset" sheet. Pure string
/// building — no filesystem, no network, no platform types. Leads with "your mods are preserved"
/// so a missing source URL never implies a missing mod.</summary>
public static class OffBoardingSheet
{
    public static string Render(OffBoardingReport r)
    {
        var sb = new StringBuilder();
        var title = $"How to launch {r.GameName} after resetting 626 Mod Launcher";
        sb.AppendLine(title);
        sb.AppendLine(new string('=', title.Length));
        sb.AppendLine("Your mods are preserved. The full setup is saved in your restore point:");
        sb.AppendLine("  " + r.RestorePointPath);
        sb.AppendLine();

        // Saves first — they're irreplaceable, and a reset is exactly when a user fears for them.
        // Safe Clear NEVER touches the game's live save folder; say so plainly and name where it is.
        sb.AppendLine("YOUR SAVES");
        sb.AppendLine("  Your game saves were not touched by this reset — they stay exactly where the game keeps them.");
        if (!string.IsNullOrEmpty(r.SaveLocation))
            sb.AppendLine("  Location:  " + r.SaveLocation);
        if (r.SaveBackupCount > 0)
            sb.AppendLine($"  Plus {r.SaveBackupCount} save backup{(r.SaveBackupCount == 1 ? "" : "s")} the launcher made are preserved in your restore point.");
        sb.AppendLine();

        sb.AppendLine("HOW TO START THE GAME");
        if (r.LaunchLines.Count == 0)
            sb.AppendLine("  Launch the game the way you normally do.");
        else
            foreach (var line in r.LaunchLines) sb.AppendLine("  " + line);
        sb.AppendLine();

        // Vanilla with a turn-off record: say what went off, where it is held, and that Restore brings back
        // exactly that set. A refused turn-off is named as still active, so "vanilla" never overclaims.
        var skips = r.TurnOffSkips ?? Array.Empty<ClearSkip>();
        if (r.TurnedOffCount > 0 || skips.Count > 0)
        {
            sb.AppendLine("MODS TURNED OFF");
            if (r.TurnedOffCount > 0)
            {
                var mods = r.TurnedOffCount == 1 ? "1 mod" : $"{r.TurnedOffCount} mods";
                sb.AppendLine($"  626 turned off {mods}.");
                // Where each lane's mods went. Without per-lane counts (an older report) all are data-folder held.
                var inData = r.InDataFolder + r.Proxies + r.InConfig == 0 ? r.TurnedOffCount : r.InDataFolder;
                if (inData > 0)
                {
                    sb.AppendLine($"  {inData} held in its data folder:");
                    if (!string.IsNullOrEmpty(r.HeldInDataDir)) sb.AppendLine("    " + r.HeldInDataDir);
                    var copied = Math.Min(r.CopiedToRestorePoint, inData);
                    sb.AppendLine(copied == inData
                        ? "  A copy of each of them is saved in your restore point too, so restoring works from that folder or, if it's gone, from the restore point."
                        : copied > 0
                            ? $"  A copy of {copied} of them is saved in your restore point. Keep that folder until you restore: it is the only copy of the other {inData - copied}."
                            : "  Keep that folder until you restore: it is where they are.");
                }
                if (r.Proxies > 0)
                    sb.AppendLine($"  {r.Proxies} loader DLL{(r.Proxies == 1 ? "" : "s")} stepped aside into the game's _626\\vanilla-proxy folder.");
                if (r.InConfig > 0)
                    sb.AppendLine($"  {r.InConfig} switched off in Mod Engine 2's config.");
                sb.AppendLine("  Restoring this setup turns exactly "
                    + (r.TurnedOffCount == 1 ? "that 1 back on." : $"those {r.TurnedOffCount} back on."));
            }
            foreach (var s in skips)
                sb.AppendLine($"  {s.Name} is still active: 626 couldn't turn it off. {s.Reason}");
            sb.AppendLine();
        }

        sb.AppendLine("WHAT'S STILL INSTALLED");
        sb.AppendLine(r.Frameworks.Count == 0
            ? "  Frameworks:  (none)"
            : "  Frameworks:  " + string.Join(", ", r.Frameworks));
        sb.AppendLine($"  Mods ({r.Mods.Count}):");
        foreach (var m in r.Mods)
        {
            var date = m.InstalledDate is null ? "" : $"   (installed {m.InstalledDate})";
            string line = m.SourceUrl switch
            {
                null => $"    {m.Name} — source not recorded — sideloaded; you'll need to find it again",
                _ when string.Equals(m.SourceConfidence, "nameSearch", StringComparison.OrdinalIgnoreCase)
                    => $"    {m.Name} — likely source: {m.SourceUrl}{date}",
                _ => $"    {m.Name} — source: {m.SourceUrl}{date}",
            };
            sb.AppendLine(line);
        }
        if (r.OwnedMods.Count > 0)
            foreach (var grp in r.OwnedMods.GroupBy(o => o.ManagedBy, StringComparer.OrdinalIgnoreCase))
            {
                var names = string.Join(", ", grp.Select(o => o.Name));
                sb.AppendLine($"  Managed by {grp.Key} ({grp.Count()}): {names} — clean these up in {grp.Key}.");
            }
        sb.AppendLine();

        sb.AppendLine("TO RESTORE THIS SETUP");
        sb.AppendLine("  Open 626 Mod Launcher and choose \"Restore a previous setup\", or");
        sb.AppendLine("  Settings -> Restore points.");
        return sb.ToString();
    }
}
