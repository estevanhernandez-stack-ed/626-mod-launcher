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
                if ($e.Current.Name -ne $Root.Current.Name) { return $e.Current.Name }
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
