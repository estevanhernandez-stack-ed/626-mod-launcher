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

        sb.AppendLine("HOW TO START THE GAME");
        if (r.LaunchLines.Count == 0)
            sb.AppendLine("  Launch the game the way you normally do.");
        else
            foreach (var line in r.LaunchLines) sb.AppendLine("  " + line);
        sb.AppendLine();

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
            sb.AppendLine($"  Managed by Vortex ({r.OwnedMods.Count}): {string.Join(", ", r.OwnedMods)} — clean these up in Vortex.");
        sb.AppendLine();

        sb.AppendLine("TO RESTORE THIS SETUP");
        sb.AppendLine("  Open 626 Mod Launcher and choose \"Restore a previous setup\", or");
        sb.AppendLine("  Settings -> Restore points.");
        return sb.ToString();
    }
}
