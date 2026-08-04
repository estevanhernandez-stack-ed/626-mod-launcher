using ModManager.Core;

namespace ModManager.Tests;

// F-024 (vibe-glow wave 4): errors say what happened AND what to do next — never a raw
// .NET exception message. ErrorRemedy is the pure Core mapper the App routes StatusText
// failures through.
public class ErrorRemedyTests
{
    [Fact]
    public void Sharing_violation_names_the_lock_and_the_remedy()
    {
        var e = new IOException("The process cannot access the file 'x.dll' because it is being used by another process.");
        var msg = ErrorRemedy.Describe(e);
        Assert.Contains("in use", msg);
        Assert.Contains("close the game", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("The process cannot access", msg); // raw .NET text never leaks
    }

    [Fact]
    public void Unauthorized_access_points_at_permissions()
    {
        var msg = ErrorRemedy.Describe(new UnauthorizedAccessException("Access to the path 'C:\\x' is denied."));
        Assert.Contains("permission", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("administrator", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_file_says_verify_and_refresh()
    {
        var msg = ErrorRemedy.Describe(new FileNotFoundException("Could not find file 'C:\\mods\\a.dll'.", "C:\\mods\\a.dll"));
        Assert.Contains("missing", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Refresh", msg);
    }

    [Fact]
    public void Disk_full_is_named()
    {
        var msg = ErrorRemedy.Describe(new IOException("There is not enough space on the disk."));
        Assert.Contains("disk is full", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_errors_keep_the_message_but_add_a_next_step()
    {
        var msg = ErrorRemedy.Describe(new InvalidOperationException("Something odd."));
        Assert.Contains("Something odd.", msg);
        Assert.Contains("try again", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Localized_sharing_violation_is_caught_by_hresult()
    {
        // German Windows message — no English fragment; HResult 0x80070020 carries the truth.
        var e = new IOException("Der Prozess kann nicht auf die Datei zugreifen.", unchecked((int)0x80070020));
        var msg = ErrorRemedy.Describe(e);
        Assert.Contains("in use", msg);
        Assert.Contains("close the game", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Localized_disk_full_is_caught_by_hresult()
    {
        var e = new IOException("Auf dem Datenträger ist nicht genügend Speicherplatz vorhanden.", unchecked((int)0x80070070));
        Assert.Contains("disk is full", ErrorRemedy.Describe(e), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unpunctuated_unknown_message_gets_a_period()
    {
        var msg = ErrorRemedy.Describe(new InvalidOperationException("Something odd"));
        Assert.Contains("Something odd. If it keeps happening", msg);
    }

    [Fact]
    public void Action_context_leads_the_message_when_given()
    {
        var e = new IOException("The process cannot access the file 'x.dll' because it is being used by another process.");
        var msg = ErrorRemedy.Describe(e, "Couldn't toggle Faster Ships");
        Assert.StartsWith("Couldn't toggle Faster Ships", msg);
    }
}
