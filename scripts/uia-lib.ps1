<#
.SYNOPSIS
    UIA driver for the 626 Mod Launcher. Dot-source it; it exposes find/read/drive/capture.

.DESCRIPTION
    The reusable half of the smoke harness (backlog E2). Deliberately dependency-free —
    UIAutomationClient ships with .NET, so this runs on any box that can run the app, with no
    FlaUI / WinAppDriver / nuget restore in the way.

    Everything here finds controls by AutomationId first (see .claude/rules/automation-ids.md).
    Name lookup exists as a fallback for controls that carry a per-row name instead of an id, and
    is the ONLY place display copy is allowed to matter.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

$script:A      = [System.Windows.Automation.AutomationElement]
$script:Walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker

function Get-AppRoot {
    <#.SYNOPSIS Root AutomationElement of the running launcher, or $null.#>
    $p = Get-Process ModManager.App -EA SilentlyContinue |
         Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $p) { return $null }
    return $script:A::FromHandle($p.MainWindowHandle)
}

function Get-Tree {
    <#.SYNOPSIS Flat list of every realised element under $Root.
      Virtualized/collapsed content is genuinely absent — see Expand-Node before asserting absence.#>
    param($Root, [int]$MaxDepth = 20, [int]$Budget = 6000)
    $list = New-Object System.Collections.Generic.List[object]
    function Walk($el, $d) {
        if ($list.Count -ge $Budget -or $d -gt $MaxDepth) { return }
        $c = $script:Walker.GetFirstChild($el)
        while ($null -ne $c -and $list.Count -lt $Budget) {
            try { $list.Add($c); Walk $c ($d + 1) } catch {}
            try { $c = $script:Walker.GetNextSibling($c) } catch { break }
        }
    }
    Walk $Root 0
    return $list
}

function Wait-Ready {
    <#.SYNOPSIS Block until the element tree stops growing, or $TimeoutSec elapses.
      A fixed sleep is a guess about someone else's machine. Measured here: the same launch read
      276 elements on one run and 632 on the next, and the 276 run passed app-starts (which only
      asks for >100) and then failed navigate-to-game because the game list had not arrived yet.
      A harness that reads a half-built window reports on a screen the user never sees.#>
    param($Root, [int]$TimeoutSec = 40, [int]$StableReads = 2, [int]$PollMs = 700)
    $last = -1; $stable = 0
    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSec)
    while ([datetime]::UtcNow -lt $deadline) {
        $n = (Get-Tree $Root).Count
        if ($n -eq $last -and $n -gt 100) { $stable++ } else { $stable = 0 }
        if ($stable -ge $StableReads) { return $n }
        $last = $n
        Start-Sleep -Milliseconds $PollMs
    }
    return $last
}

# Win32 window enumeration. The desktop TREE is not walkable from this session - TreeWalker on
# RootElement returns nothing - which is the same reason Get-AppRoot resolves the app by handle
# rather than by walking. So find the dialog by handle too, then hand it to UIA via FromHandle.
if (-not ("Win32Windows" -as [type])) {
    Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class Win32Windows {
    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    public static List<IntPtr> ByClass(string cls, int pid) {
        var hits = new List<IntPtr>();
        EnumWindows((h, l) => {
            if (!IsWindowVisible(h)) return true;
            int wp; GetWindowThreadProcessId(h, out wp);
            if (pid != 0 && wp != pid) return true;
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            if (sb.ToString() == cls) hits.Add(h);
            return true;
        }, IntPtr.Zero);
        return hits;
    }
}
"@
}

function Get-FileDialog {
    <#.SYNOPSIS The native Open/Save dialog the app just raised, or $null.
      Located by WINDOW CLASS (#32770, the common-dialog class) rather than by looking for a window
      with an edit box: the app's own window has edit boxes too, and an earlier cut of this happily
      returned the main window and then reported "no accept button in the dialog".#>
    param([int]$TimeoutSec = 15)
    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSec)
    while ([datetime]::UtcNow -lt $deadline) {
        # NOT filtered to the app's process. WinUI 3's FileOpenPicker is hosted in a SEPARATE broker
        # process - measured: app pid 42204, dialog pid 62196 - so a pid filter silently never matches.
        # That cost two wrong diagnoses: first the finder returned the app's own window (it has edit
        # boxes too) and reported "no accept button", then it found nothing at all.
        foreach ($h in [Win32Windows]::ByClass("#32770", 0)) {
            try { return [System.Windows.Automation.AutomationElement]::FromHandle($h) } catch { }
        }
        Start-Sleep -Milliseconds 400
    }
    return $null
}
function Find-DialogEdit {
    <#.SYNOPSIS The "File name:" edit inside a common dialog. Located by control type rather than by
      the localized label, so this does not depend on the machine language.#>
    param($Dialog)
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    $hits = $Dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    foreach ($e in $hits) { if ($e.Current.IsEnabled) { return $e } }
    return $null
}

function Submit-FileDialog {
    <#.SYNOPSIS Put a path into the open dialog and accept it. Returns $true when the dialog closes.

      Driven by CLIPBOARD + KEYSTROKES, not by ValuePattern.SetValue: the dialog lives in a different
      process (see Get-FileDialog) and SetValue against it times out with 0x80131505. Paste-and-Enter
      is what a person does anyway, handles spaces and unicode without escaping, and costs one focus
      call. The clipboard is restored afterwards - a harness that eats the user's clipboard is a
      harness nobody runs twice.#>
    param($Dialog, [string[]]$Paths, [int]$TimeoutSec = 15)
    Add-Type -AssemblyName System.Windows.Forms

    # Deliberately NOT locating the filename edit first. Every UIA query into this dialog crosses a
    # process boundary and they time out (0x80131505) as readily as SetValue did. A freshly raised Open
    # dialog already holds focus in the filename box, which is exactly what a person relies on.
    $text = if ($Paths.Count -eq 1) { $Paths[0] } else { ($Paths | ForEach-Object { '"' + $_ + '"' }) -join ' ' }

    # Track the dialog by HANDLE. Reading a property off a dead cross-process element does not
    # reliably throw, so "has it closed" is answered by asking Windows whether the window still exists.
    $hwndTracked = [IntPtr]::Zero
    try { $hwndTracked = [IntPtr]$Dialog.Current.NativeWindowHandle } catch { }

    $saved = $null
    try { $saved = Get-Clipboard -Raw -EA SilentlyContinue } catch { }
    try {
        Set-Clipboard -Value $text
        # SendKeys goes to whatever the SHELL thinks is foreground, which is not necessarily the window
        # we just found - the harness runs from a console that may hold focus. Force it, by handle.
        try {
            $hwnd = [IntPtr]$Dialog.Current.NativeWindowHandle
            [void][Win32Windows]::BringWindowToTop($hwnd)
            [void][Win32Windows]::SetForegroundWindow($hwnd)
        } catch { }
        try { $Dialog.SetFocus() } catch { }
        Start-Sleep -Milliseconds 600
        [System.Windows.Forms.SendKeys]::SendWait("^a")
        [System.Windows.Forms.SendKeys]::SendWait("^v")
        Start-Sleep -Milliseconds 700
        # ENTER first, then Alt+O. Pasting a path pops the filename box's autocomplete list, and the
        # first ENTER only dismisses that - measured: the dialog sat open with the right path in the
        # field. Alt+O hits the &Open accelerator directly and does not care about the dropdown.
        [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
        Start-Sleep -Milliseconds 900
        if (Test-DialogOpen $hwndTracked) {
            [System.Windows.Forms.SendKeys]::SendWait("%o")
            Start-Sleep -Milliseconds 900
        }
        if (Test-DialogOpen $hwndTracked) { [System.Windows.Forms.SendKeys]::SendWait("{ENTER}") }
    }
    finally {
        Start-Sleep -Milliseconds 600
        if ($null -ne $saved -and $saved -ne "") { try { Set-Clipboard -Value $saved } catch { } }
    }

    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSec)
    while ([datetime]::UtcNow -lt $deadline) {
        if (-not (Test-DialogOpen $hwndTracked)) { return $true }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

function Test-DialogOpen {
    <#.SYNOPSIS Is that window handle still a live, visible common dialog?#>
    param([IntPtr]$Handle)
    if ($Handle -eq [IntPtr]::Zero) { return $false }
    return ([Win32Windows]::ByClass("#32770", 0) -contains $Handle)
}

function Get-ContentDialog {
    <#.SYNOPSIS The app's own in-window ContentDialog, by its title text. WinUI dialogs live inside
      the app window, unlike the native pickers above.#>
    param($Root, [string]$TitleLike, [string]$ButtonName, [int]$TimeoutSec = 10)
    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSec)
    while ([datetime]::UtcNow -lt $deadline) {
        foreach ($n in (Get-Tree $Root)) {
            $nm = ""; try { $nm = $n.Current.Name } catch { }
            # Return the dialog ELEMENT, not the app tree. Its buttons sit deeper than a walk from the
            # app root reaches, and returning the whole tree let a mod row named 'Uninstall <mod>'
            # satisfy a search for the confirm dialog - a match on the wrong thing entirely.
            if ($nm -notlike $TitleLike) { continue }
            # A ContentDialog surfaces as several nodes with the same name (Window, Group, Text). Pick
            # the one that actually CONTAINS the buttons rather than guessing by control type - the
            # first cut guessed, and matched a node whose subtree held nothing to click.
            if (@(Get-Tree $n | Where-Object { try { $_.Current.Name -eq $ButtonName } catch { $false } }).Count -gt 0) { return $n }
        }
        Start-Sleep -Milliseconds 400
    }
    return $null
}

function Find-ById {
    param($Tree, [string]$Id)
    $Tree | Where-Object { try { $_.Current.AutomationId -eq $Id } catch { $false } } | Select-Object -First 1
}

function Find-ByName {
    param($Tree, [string]$Name, [switch]$Like)
    $Tree | Where-Object {
        try { if ($Like) { $_.Current.Name -like $Name } else { $_.Current.Name -eq $Name } } catch { $false }
    } | Select-Object -First 1
}

function Find-AllByIdPrefix {
    param($Tree, [string]$Prefix)
    $Tree | Where-Object { try { $_.Current.AutomationId -like "$Prefix*" } catch { $false } }
}

function Test-Visible {
    <#.SYNOPSIS Visible means realised AND on screen. Absent returns $false with $Realised = $false,
      so a caller can tell "not there" from "not rendered yet" — the distinction three of the four
      v0.18 bugs turned on.#>
    param($Element)
    if ($null -eq $Element) { return [pscustomobject]@{ Realised = $false; Visible = $false } }
    $off = $true
    try { $off = $Element.Current.IsOffscreen } catch {}
    return [pscustomobject]@{ Realised = $true; Visible = -not $off }
}

# Set a text box's contents through UIA rather than through the keyboard. SendKeys goes to whatever
# the SHELL thinks is foreground, which is not necessarily the window under test - and it needs a
# System.Windows.Forms load that is not guaranteed in a bare pwsh session. ValuePattern needs neither.
function Set-EditValue {
    param($Element, [string]$Value)
    if ($null -eq $Element) { throw "Set-EditValue: element is null" }
    $p = $null
    if (-not $Element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$p)) {
        throw "Set-EditValue: element exposes no ValuePattern"
    }
    $p.SetValue($Value)
}

function Get-Text {
    param($Element)
    if ($null -eq $Element) { return $null }
    try { return $Element.Current.Name } catch { return $null }
}

function Invoke-Node {
    param($Element)
    if ($null -eq $Element) { throw "Invoke-Node: element is null" }
    $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Expand-Node {
    param($Element)
    $Element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
}

function Collapse-Node {
    param($Element)
    $Element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
}

function Get-Selection {
    <#.SYNOPSIS Reads a ComboBox's current selection WITHOUT opening it. Preferred over driving —
      most verification asks "is the right thing selected", not "what are all the options".#>
    param($Element)
    if ($null -eq $Element) { return $null }
    try {
        $sp = $Element.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
        return ($sp.Current.GetSelection() | ForEach-Object { $_.Current.Name }) -join ', '
    } catch { return $null }
}

function Get-ToggleState {
    param($Element)
    if ($null -eq $Element) { return $null }
    try { return $Element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState }
    catch { return $null }
}

function Set-Toggle {
    param($Element)
    $Element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
}

function Save-Shot {
    <#.SYNOPSIS Screenshot of the app window. Every case captures whether or not it asserts on the
      image — the open-ended read is what catches what nobody thought to assert (A10/A11 were both
      found that way).#>
    param([string]$Path)
    $p = Get-Process ModManager.App -EA SilentlyContinue |
         Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $p) { return $null }
    Add-Type @'
using System; using System.Runtime.InteropServices;
public class UiaShot {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@ -ErrorAction SilentlyContinue
    [void][UiaShot]::SetForegroundWindow($p.MainWindowHandle)
    Start-Sleep -Milliseconds 350
    $r = New-Object UiaShot+RECT
    [void][UiaShot]::GetWindowRect($p.MainWindowHandle, [ref]$r)
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { return $null }
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    New-Item -ItemType Directory -Force -Path (Split-Path $Path) | Out-Null
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $Path
}

function Test-ModalOpen {
    <#.SYNOPSIS Is ANY modal dialog on screen? Returns the dialog's title, or $null.
      Ask this before asserting on the surface behind it. Detecting a specific dialog by its known
      field ids is not the same question and gets the wrong answer for every dialog you did not
      list - a harness once reported "no dialog, added directly" with an adoption prompt sitting
      open, because it only knew how to recognise the add-game form.#>
    param($Root)
    $tree = Get-Tree $Root
    foreach ($e in $tree) {
        try {
            $ct = $e.Current.ControlType.ProgrammaticName
            if ($ct -match 'ControlType\.(Window|Dialog)$' -and $e.Current.Name) {
                # The app's own top-level window is not a modal.
                if ($e.Current.Name -eq $Root.Current.Name) { continue }
                # Neither is anything OFFSCREEN. A WinUI tooltip is a Popup, a Popup is a Window, and
                # it lingers in the tree after a UIA Invoke - so adding a tooltip to + Add mods in
                # wave 10 made this report "a modal is open ('Popup')" and failed two intake cases.
                # The popup's whole content was the tooltip text. Whatever a modal is, it is on screen:
                # asking "can I assert on the surface behind this" has no meaning for something that
                # covers nothing. Red for a false reason is the safe direction to be wrong in, and it
                # still cost two runs and a wrong first diagnosis ("stuck picker broker") to clear.
                if ($e.Current.IsOffscreen) { continue }
                return $e.Current.Name
            }
        } catch {}
    }
    return $null
}

function Assert-NoModal {
    param($Root)
    $m = Test-ModalOpen -Root $Root
    if ($m) { throw "a modal is open ('$m') - the surface behind it cannot be asserted on" }
}

function Get-RealisedCount {
    <#.SYNOPSIS Count of elements whose AutomationId starts with $Prefix that are CURRENTLY REALISED.

      This is deliberately not called Get-Count. A virtualized list realises only what is near the
      viewport, so this number is "how many are rendered", never "how many exist". Reading it as a
      total is how a harness concludes a freshly-added game is missing when it is simply sorted
      below the fold. When you need the real total, get it from the source of truth (the registry via
      MCP, or the view-model), not from the tree.#>
    param($Tree, [string]$Prefix)
    return @(Find-AllByIdPrefix $Tree $Prefix).Count
}

function Test-RowPresent {
    <#.SYNOPSIS Is a specific row present, realising it if the platform can?
      Uses the id directly first, then falls back to ItemContainerPattern/VirtualizedItem via the
      containing list so an offscreen row still answers truthfully.#>
    param($Tree, [string]$AutomationId)
    $hit = Find-ById $Tree $AutomationId
    if ($hit) { return $true }
    foreach ($c in $Tree) {
        try {
            if (-not $c.GetCurrentPropertyValue($script:A::IsItemContainerPatternAvailableProperty)) { continue }
            $icp = $c.GetCurrentPattern([System.Windows.Automation.ItemContainerPattern]::Pattern)
            $found = $icp.FindItemByProperty($null, $script:A::AutomationIdProperty, $AutomationId)
            if ($found) {
                try { $found.GetCurrentPattern([System.Windows.Automation.VirtualizedItemPattern]::Pattern).Realize() } catch {}
                return $true
            }
        } catch {}
    }
    return $false
}

function Wait-Idle { param([int]$Ms = 900) Start-Sleep -Milliseconds $Ms }
