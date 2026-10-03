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
                        ? (inData == 1
                            ? "  A copy of it is saved in your restore point too, so restoring works from that folder or, if it's gone, from the restore point."
                            : "  A copy of each of them is saved in your restore point too, so restoring works from that folder or, if it's gone, from the restore point.")
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

        // The files no mod row claims, swept out of the mod folders into the restore point.
        if (r.RemainderMoved > 0)
        {
            sb.AppendLine("ALSO MOVED TO YOUR RESTORE POINT");
            var files = r.RemainderMoved == 1 ? "1 other file" : $"{r.RemainderMoved} other files";
            sb.AppendLine($"  {files} in the game's mod folders (loose scripts, plugins, sidecars no mod row claims) "
                + (r.RemainderMoved == 1 ? "was" : "were") + " moved into your restore point. Restoring puts "
                + (r.RemainderMoved == 1 ? "it" : "them") + " back.");
            sb.AppendLine();
        }
        // What vanilla knowingly left, and why: the honest exception list behind "may not be fully vanilla".
        if (r.StillInPlace is { Count: > 0 } still)
        {
            sb.AppendLine("STILL IN PLACE");
            foreach (var n in still) sb.AppendLine($"  {n.Path} — {n.Reason}");
            sb.AppendLine();
        }

        // Vanilla: the list is split by where each mod stands, so nothing still active ever sits under a
        // "turned off" heading (replica r7). Every other sheet keeps one plain list.
        var split = r.KeptTurnedOff && r.Mods.Any(m => m.State is not null);
        sb.AppendLine(split ? "YOUR MODS" : r.KeptTurnedOff ? "YOUR MODS (KEPT, TURNED OFF)" : "WHAT'S STILL INSTALLED");
        sb.AppendLine(r.Frameworks.Count == 0
            ? "  Frameworks:  (none)"
            : "  Frameworks:  " + string.Join(", ", r.Frameworks));
        void Lines(IEnumerable<OffBoardingModLine> mods)
        {
            foreach (var m in mods)
            {
                var date = m.InstalledDate is null ? "" : $"   (installed {m.InstalledDate})";
                string line = m.SourceUrl switch
                {
                    null => $"    {m.Name} — source not recorded — sideloaded; you'll need to find it again",
                    _ when string.Equals(m.SourceConfidence, "nameSearch", StringComparison.OrdinalIgnoreCase)
                        => $"    {m.Name} — likely source: {m.SourceUrl}{date}",
                    _ => $"    {m.Name} — source: {m.SourceUrl}{date}",
                };
                sb.AppendLine(m.StateNote is null ? line : $"{line} — {m.StateNote}");
            }
        }
        if (split)
        {
            var groups = new[]
            {
                (OffBoardingModState.TurnedOff, "Turned off by 626 (restoring turns these back on)"),
                (OffBoardingModState.StillActive, "Still active (626 left these on)"),
                (OffBoardingModState.LeftAlone, "Left alone (626 didn't touch these)"),
                (OffBoardingModState.AlreadyOff, "Already off before the reset"),
            };
            foreach (var (state, label) in groups)
            {
                var these = r.Mods.Where(m => m.State == state).ToList();
                if (these.Count == 0) continue;
                sb.AppendLine($"  {label} ({these.Count}):");
                Lines(these);
            }
            var rest = r.Mods.Where(m => m.State is null).ToList();
            if (rest.Count > 0) { sb.AppendLine($"  Other mods ({rest.Count}):"); Lines(rest); }
        }
        else
        {
            sb.AppendLine(r.KeptTurnedOff
                ? $"  Mods ({r.Mods.Count}), with where to find each again:"
                : $"  Mods ({r.Mods.Count}):");
            Lines(r.Mods);
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
