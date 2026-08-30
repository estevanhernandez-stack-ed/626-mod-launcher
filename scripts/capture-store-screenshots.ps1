<#
.SYNOPSIS
    Captures the nine Microsoft Store listing screenshots at exactly 1920x1080.

.DESCRIPTION
    You navigate; this captures. It sizes the app window to precisely 1920x1080, walks the seven
    shots in order, and writes each one with the right name into the right folder.

    Why a script rather than the Snipping Tool: the parts that go wrong on a manual pass are the
    boring ones. A window a few pixels off between shots, one capture at a different size because
    the window got dragged, a file named 03-nexus instead of 03-browse-nexus, a stray notification
    toast in frame. Those cost a resubmission. Framing and naming are handled here so the only job
    left is the one a person has to do: getting the app to the right screen.

.PARAMETER OutDir
    Where the PNGs land. Defaults to docs/store-assets/screenshots-<version>/.

.PARAMETER Version
    Names the default output folder. Defaults to 0.18.

.PARAMETER Only
    Capture just these shot numbers, e.g. -Only 3,7 to redo two. Everything else is left alone.

.PARAMETER Auto
    Drive the navigation too, through scripts/uia-lib.ps1, instead of waiting at a prompt for a
    person to click. Each shot carries a Nav block that puts the app in the right state and then
    VERIFIES it arrived - a capture of the wrong screen is worse than no capture, because it looks
    finished. A shot whose Nav cannot confirm its state is skipped and named in the summary rather
    than photographed hopefully.

    The interactive path is unchanged and is still the right one when a shot needs judgement.

.EXAMPLE
    pwsh scripts/capture-store-screenshots.ps1
    pwsh scripts/capture-store-screenshots.ps1 -Only 3,7
    pwsh scripts/capture-store-screenshots.ps1 -Auto -Version 0.19
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$Version = '0.18',
    [int[]]$Only,
    [switch]$Auto
)

$ErrorActionPreference = 'Stop'

# Store listing images must be 1366x768 at minimum and 3840x2160 at most. The live 0.17 set is
# 1920x1080; matching it exactly means the new shots drop into the same listing slots without
# reflowing how they crop in the Store's carousel.
$TargetW = 1920
$TargetH = 1080

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repoRoot "docs/store-assets/screenshots-$Version" }

# Order matters — this is the sequence a shopper scrolls, so it opens on the library and ends on the
# safety story. Each 'state' line is what you navigate to; each 'watch' is the thing that has
# actually ruined one of these before.
$Shots = @(
    @{ N = 1; Name = '01-library-home'
       State = 'Home / library, scrolled to the top. Jump-back-in row visible with cover art.'
       Watch = 'The hero shot. Make sure several games have real cover art and no row is mid-load.'
       Nav   = { Go-Home; (@(Find-AllByIdPrefix (Get-Tree (Get-AppRoot)) 'GameRow.')).Count -gt 3 } }
    @{ N = 2; Name = '02-game-mods-view'
       State = 'Open a game with a healthy mod list. Elden Ring or Windrose.'
       Watch  = 'Show a mix of enabled and disabled so the toggle reads as a toggle.'
       Nav    = { Open-Game 'windrose'; (@(Find-AllByIdPrefix (Get-Tree (Get-AppRoot)) 'ModRow.')).Count -gt 5 } }
    @{ N = 3; Name = '03-browse-nexus'
       State = 'Browse Nexus, results loaded.'
       Watch  = 'Signed in, and let thumbnails finish. A grid of grey placeholders is a bad advert.'
       Nav    = { Open-Game 'windrose'
                  $b = Find-ById (Get-Tree (Get-AppRoot)) 'BrowseNexusButton'
                  if (-not $b) { return $false }
                  $before = (@(Get-Tree (Get-AppRoot))).Count
                  Invoke-Node $b
                  # The cards carry no bound id - they are GridView items over a plain template - so
                  # readiness is measured as the storefront chrome arriving AND the tree growing by a
                  # gridful. Thumbnails then get a beat of their own: a grid of grey placeholders is
                  # the one thing this shot must not be.
                  if (-not (Wait-For { $null -ne (Find-ById (Get-Tree (Get-AppRoot)) 'NexusQueryBox') } 25)) { return $false }
                  Wait-For { (@(Get-Tree (Get-AppRoot))).Count -gt ($before + 40) } 25
                  Start-Sleep -Seconds 6
                  (@(Get-Tree (Get-AppRoot))).Count -gt ($before + 40) } }
    @{ N = 4; Name = '04-updates-view'
       State = 'The updates view with at least one update pending.'
       Watch  = 'An empty updates list says nothing about the feature.'
       Nav    = { Go-Home
                  $e = Find-ById (Get-Tree (Get-AppRoot)) 'LibraryUpdatesEntry'
                  if (-not $e) { return $false }
                  Invoke-Node $e
                  Wait-For { (@(Find-AllByIdPrefix (Get-Tree (Get-AppRoot)) 'UpdateRow.')).Count -gt 0 } 20 } }
    @{ N = 5; Name = '05-add-game'
       State = 'The +Game dialog, open over the library.'
       Watch  = 'Do not show a path containing anything you would not publish.'
       Nav    = { Go-Home
                  $b = Find-ById (Get-Tree (Get-AppRoot)) 'AddGameButton'
                  if (-not $b) { return $false }
                  Invoke-Node $b
                  if (-not (Wait-For { $null -ne (Find-ById (Get-Tree (Get-AppRoot)) 'GameNameBox') } 20)) { return $false }
                  # The Steam list draws its cover art asynchronously; shooting immediately gives a
                  # column of grey rectangles next to the game names.
                  Start-Sleep -Seconds 5
                  $true } }
    @{ N = 6; Name = '06-settings'
       State = 'Settings, open on Appearance.'
       Watch  = 'THE POINT OF THIS RETAKE: the four-group shape. Appearance heading visible, and the ' +
                'Accounts heading below it. The theme picker moved to the toolbar - it is not in here.'
       Nav    = { Open-Game 'windrose'
                  $b = Find-ById (Get-Tree (Get-AppRoot)) 'SettingsButton'
                  if (-not $b) { return $false }
                  Invoke-Node $b
                  Wait-For { $null -ne (Find-ById (Get-Tree (Get-AppRoot)) 'SettingsGroup.appearance') } 15 } }
    @{ N = 7; Name = '07-saves-snapshots'
       State = 'Saves / snapshots for a game that has some.'
       Watch  = 'Real snapshots with real timestamps. This is the reversibility promise made visible.'
       Nav    = { # Elden Ring, not Windrose: this is a FromSoft title, so the save format IS itemised
                  # and the dialog shows real characters. On Windrose the same dialog is three lines
                  # of "No save files / No editable characters / No save mods installed", which is
                  # the exact opposite of the reversibility promise this shot is meant to make.
                  Open-Game 'elden-ring'
                  $b = Find-ById (Get-Tree (Get-AppRoot)) 'SavesButton'
                  if (-not $b) { return $false }
                  Invoke-Node $b
                  # Test-DialogOpen answers a different question - it looks for a Win32 common dialog
                  # (#32770), and this is a WinUI ContentDialog. Wait on a control that only exists
                  # inside it.
                  Wait-For { $null -ne (Find-ById (Get-Tree (Get-AppRoot)) 'BackupNowButton') } 20 } }
    @{ N = 8; Name = '08-back-up-everything'
       State = 'Settings, scrolled to Back up everything.'
       Watch  = 'The two buttons and the sentence under the heading. This is the 0.20 headline feature, ' +
                'and the section says in its own words that nothing on the machine is changed.'
       Nav    = { Open-Game 'windrose'
                  $b = Find-ById (Get-Tree (Get-AppRoot)) 'SettingsButton'
                  if (-not $b) { return $false }
                  Invoke-Node $b
                  if (-not (Wait-For { $null -ne (Find-ById (Get-Tree (Get-AppRoot)) 'SettingsGroup.archive') } 15)) { return $false }
                  # The group is a StackPanel and reaches the tree as a Group only because it carries an
                  # AutomationProperties.Name - an id alone would not have surfaced it. It supports
                  # ScrollItemPattern, so ask for it rather than guessing a scroll offset.
                  $g = Find-ById (Get-Tree (Get-AppRoot)) 'SettingsGroup.archive'
                  try { $g.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView() } catch { }
                  Start-Sleep -Milliseconds 800
                  $g = Find-ById (Get-Tree (Get-AppRoot)) 'SettingsGroup.archive'
                  $g -and -not $g.Current.IsOffscreen } }
    @{ N = 9; Name = '09-inside-a-backup'
       # The report is a Flyout, and SetWindowPos dismisses one. See the Fragile check below.
       Fragile = $true
       State = 'The report a backup opens into, with per-game parts ticked.'
       Watch  = 'The point of the shot is that this screen READS. Headline with a real count, games ' +
                'split into set-up-here and waiting, and the parts a restore would put back. Needs ' +
                'CAPTURE_ARCHIVE set to a real .626profile.'
       Nav    = { if (-not $env:CAPTURE_ARCHIVE -or -not (Test-Path $env:CAPTURE_ARCHIVE)) {
                      Write-Host '    (set CAPTURE_ARCHIVE to a .626profile first)' -f DarkYellow
                      return $false }
                  Open-Game 'windrose'
                  $b = Find-ById (Get-Tree (Get-AppRoot)) 'SettingsButton'
                  if (-not $b) { return $false }
                  Invoke-Node $b
                  if (-not (Wait-For { $null -ne (Find-ById (Get-Tree (Get-AppRoot)) 'ArchiveInspectButton') } 15)) { return $false }
                  # Scroll to the section FIRST. The flyout anchors to the button, so opening it from
                  # an unscrolled dialog seats it at the bottom of the window over the mod list; from
                  # here it lands centred over the section that opened it, which is the coherent shot.
                  $g = Find-ById (Get-Tree (Get-AppRoot)) 'SettingsGroup.archive'
                  try { $g.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView() } catch { }
                  Start-Sleep -Milliseconds 800
                  Invoke-Node (Find-ById (Get-Tree (Get-AppRoot)) 'ArchiveInspectButton')
                  $dlg = Get-FileDialog
                  if (-not $dlg) { return $false }
                  [void](Submit-FileDialog $dlg @($env:CAPTURE_ARCHIVE))
                  # Reading a 12-game manifest and scanning what is installed takes a beat; the flyout
                  # renders empty first and a shot taken then looks like the feature found nothing.
                  if (-not (Wait-For { $null -ne (Find-ById (Get-Tree (Get-AppRoot)) 'ArchiveReportHeadline') } 30)) { return $false }
                  Start-Sleep -Seconds 2
                  (@(Find-AllByIdPrefix (Get-Tree (Get-AppRoot)) 'ArchiveRestorePart.')).Count -gt 0 } }
)

# --- automated navigation (-Auto) ------------------------------------------------------------
#
# The capture half of this script was always reliable; the navigation half was the part that needed a
# person. It does not any more - the same UIA library the smoke harness drives the app with can put
# each screen up and, more importantly, CONFIRM it arrived. A screenshot of the wrong screen is worse
# than a missing one: it looks finished.

. (Join-Path $PSScriptRoot 'uia-lib.ps1')

function Wait-For {
    param([scriptblock]$Until, [int]$Seconds = 15)
    $deadline = [datetime]::UtcNow.AddSeconds($Seconds)
    while ([datetime]::UtcNow -lt $deadline) {
        try { if (& $Until) { return $true } } catch { }
        Start-Sleep -Milliseconds 700
    }
    return $false
}

function Close-AnyDialog {
    # Esc, then confirm it actually went. Two shots open ContentDialogs and the one after each would
    # otherwise photograph the dialog still sitting there.
    Add-Type -AssemblyName System.Windows.Forms
    for ($i = 0; $i -lt 3; $i++) {
        if (-not (Test-ModalOpen -Root (Get-AppRoot))) { return $true }
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Start-Sleep -Milliseconds 900
    }
    return (-not (Test-ModalOpen -Root (Get-AppRoot)))
}

function Go-Home {
    Close-AnyDialog | Out-Null
    # HomeButton is the game view's way back and does not exist on the full-screen sub-views. The
    # updates view and the storefront each have their own back button, and missing that put the
    # +Game dialog on top of the UPDATES list in the first automated set - a shot whose background
    # was simply the wrong screen, which nothing in the capture path can notice.
    foreach ($id in @('UpdatesBackButton', 'NexusBackButton')) {
        $b = Find-ById (Get-Tree (Get-AppRoot)) $id
        if ($b) { Invoke-Node $b; Start-Sleep -Seconds 3 }
    }
    $h = Find-ById (Get-Tree (Get-AppRoot)) 'HomeButton'
    if ($h) { Invoke-Node $h; Start-Sleep -Seconds 3 }
    Wait-For { (@(Find-AllByIdPrefix (Get-Tree (Get-AppRoot)) 'GameRow.')).Count -gt 0 } 15
}

function Open-Game {
    param([string]$Id)
    Close-AnyDialog | Out-Null
    if (Find-ById (Get-Tree (Get-AppRoot)) 'HomeButton') {
        # Already on a game view. Only navigate if it is the wrong game.
        $picker = Find-ById (Get-Tree (Get-AppRoot)) 'GamePicker'
        if ($picker -and (Get-Text $picker) -and (Get-Text $picker).Length -gt 0) { }
    }
    Go-Home | Out-Null
    $row = Find-ById (Get-Tree (Get-AppRoot)) ("GameRow." + $Id)
    if (-not $row) { return $false }
    Invoke-Node $row
    Wait-For { $null -ne (Find-ById (Get-Tree (Get-AppRoot)) 'HomeButton') } 20
}

Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Win32Cap {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    // GetWindowRect returns the rect INCLUDING the invisible resize border and drop shadow Windows
    // keeps around a window. Capturing that rect photographs a few pixels of whatever is behind, along
    // the bottom and both sides - which is exactly how a terminal window ended up in the bottom edge of
    // the first automated set. The DWM knows the real visible bounds; ask it.
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

# Without this, PowerShell is a DPI-unaware process: Windows lies to it about coordinates on any
# display scaled above 100%, and every capture comes back the wrong size with no error to explain it.
[void][Win32Cap]::SetProcessDPIAware()

function Get-AppWindow {
    $p = Get-Process -Name 'ModManager.App' -ErrorAction SilentlyContinue |
         Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $p) { return $null }
    return $p.MainWindowHandle
}

# DWMWA_EXTENDED_FRAME_BOUNDS
$DwmExtendedFrameBounds = 9

function Get-VisibleBounds {
    param([IntPtr]$Handle)
    $e = New-Object Win32Cap+RECT
    $hr = [Win32Cap]::DwmGetWindowAttribute($Handle, $DwmExtendedFrameBounds, [ref]$e, 16)
    if ($hr -ne 0) {
        # Fall back to the outer rect rather than failing - a slightly-too-large capture beats none,
        # and the dimension check at the end will catch it.
        [void][Win32Cap]::GetWindowRect($Handle, [ref]$e)
    }
    return @{ X = $e.Left; Y = $e.Top; W = $e.Right - $e.Left; H = $e.Bottom - $e.Top }
}

function Set-WindowExact {
    param([IntPtr]$Handle)
    if ([Win32Cap]::IsIconic($Handle)) { [void][Win32Cap]::ShowWindow($Handle, 9) } # SW_RESTORE
    [void][Win32Cap]::SetForegroundWindow($Handle)
    # HWND_TOP, SWP_NOACTIVATE(0x0010) off so it comes forward and stays put between shots.
    [void][Win32Cap]::SetWindowPos($Handle, [IntPtr]::Zero, 0, 0, $TargetW, $TargetH, 0x0040)
    Start-Sleep -Milliseconds 400

    # Size the OUTER rect so that the VISIBLE frame lands at exactly TargetW x TargetH at 0,0. The
    # shadow border is typically 7px on each side and the bottom; measuring it beats assuming it.
    $outer = New-Object Win32Cap+RECT
    [void][Win32Cap]::GetWindowRect($Handle, [ref]$outer)
    $vis = Get-VisibleBounds -Handle $Handle
    $padW = ($outer.Right - $outer.Left) - $vis.W
    $padH = ($outer.Bottom - $outer.Top) - $vis.H
    $offX = $vis.X - $outer.Left
    $offY = $vis.Y - $outer.Top
    # +2 in each dimension, then capture inset by 1. The visible frame sizes to exactly 1920x1080,
    # but the CLIENT area inside it is 1918x1079 - there is a 1px window border down the left, right
    # and bottom which is partly transparent, so the desktop shows through it. That is the 117 bright
    # pixels that appeared in the bottom rows of every shot in the first automated set, identical
    # across six different screens because it was never app content at all. Growing the window by two
    # and taking the middle drops the border entirely and still yields exactly 1920x1080 of app.
    [void][Win32Cap]::SetWindowPos($Handle, [IntPtr]::Zero, (0 - $offX - 1), (0 - $offY - 1),
                                   ($TargetW + $padW + 2), ($TargetH + $padH + 2), 0x0040)
    Start-Sleep -Milliseconds 400

    $vis2 = Get-VisibleBounds -Handle $Handle
    return @{ X = $vis2.X + 1; Y = $vis2.Y + 1; W = $TargetW; H = $TargetH }
}

function Hide-Pointer {
    # Park the cursor off the window and wait for any tooltip to fade. The first automated set caught
    # two: the settings gear's tooltip across the toolbar, and the updates entry's across a row. A
    # tooltip is a truthful part of the UI and still wrong in a listing shot - it is transient, it
    # covers content, and it advertises that a mouse was sitting still.
    param([int]$X = 0, [int]$Y = 0)
    [void][Win32Cap]::SetCursorPos(($TargetW + 200), ($TargetH - 100))
    Start-Sleep -Milliseconds 1400
}

function Save-Capture {
    param([hashtable]$Rect, [string]$Path)
    $bmp = New-Object System.Drawing.Bitmap($Rect.W, $Rect.H)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    # CopyFromScreen, not PrintWindow: WinUI 3 composites on the GPU and PrintWindow returns a blank
    # or half-drawn surface for it. Reading back the composited desktop is the reliable route, which
    # is also why the window has to be genuinely on top and unobscured.
    $g.CopyFromScreen($Rect.X, $Rect.Y, 0, 0, (New-Object System.Drawing.Size($Rect.W, $Rect.H)))
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $dims = "$($bmp.Width)x$($bmp.Height)"
    $bmp.Dispose()
    return $dims
}

# --- preflight -------------------------------------------------------------

Add-Type -AssemblyName System.Windows.Forms
$vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
if ($vs.Width -lt $TargetW -or $vs.Height -lt $TargetH) {
    throw "Display is $($vs.Width)x$($vs.Height); a $TargetW x $TargetH window will not fit on screen. " +
          "Capture needs the whole window visible and unobscured. Use a larger display."
}

$handle = Get-AppWindow
if (-not $handle) {
    throw "626 Mod Launcher is not running (no ModManager.App process with a window). Start it first, " +
          "then re-run. See the note below on which build to shoot from."
}

# WHICH BUILD TO SHOOT FROM
#
# The installed Store package is what to use, even when it is a release behind. As of 2026-08-16 that
# is 0.17.0.0 while the submission is 0.18.1.0, and shooting 0.17 with the theme switched to navy is
# CORRECT rather than a shortcut. Verified before relying on it:
#
#   - The navy theme is byte-identical between the two. The only theme commit since v0.17.0 is
#     f5f28b5, and its diff touches ThemeService.cs (which theme is DEFAULT) and not Themes.cs
#     (what navy actually looks like).
#   - None of 0.18.1's three fixes changes any of these seven views. The first-run discovery lane is
#     empty-state only and every shot here has a populated library; the repaint fix is behaviour, not
#     appearance; the duplicate-add guard changes a status line after adding, not the dialog in shot 5.
#   - No version string appears in any of the seven frames, so nothing contradicts the listing.
#
# What NOT to do: a local Debug build. It stamps itself 0.1.0.0, which fails the Nexus plugin's
# minBinaryVersion gate, so shot 3 (Browse Nexus) would come back empty or absent. The freshly built
# submission bundle is no good either - it is unsigned by design (Microsoft signs it), so it cannot
# be side-loaded.
#
# If a future release DOES change one of these views, this reasoning expires. Re-check the diff.

if (-not $Auto) {
Write-Host ''
Write-Host '  STEP 0 - before anything else:' -ForegroundColor Magenta
Write-Host '  Set the theme to 626 Labs (navy) in the toolbar theme picker. That is the entire point' -ForegroundColor Magenta
Write-Host '  of this retake - the live listing shows Forge and the app now opens navy.' -ForegroundColor Magenta
Read-Host '  Press Enter once the app is navy'
}
else {
    # -Auto reads the theme rather than asking. It is a toolbar control now, so it can be checked.
    $tp = Find-ById (Get-Tree (Get-AppRoot)) 'ThemePicker'
    $theme = if ($tp) { Get-Text $tp } else { '(no ThemePicker found)' }
    Write-Host ("  theme reads '{0}'" -f $theme) -ForegroundColor Cyan
    if ($theme -notmatch '626') {
        throw "Theme is '$theme', not 626 Labs. Switch it before capturing - the whole set has to match."
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$plan = if ($Only) { $Shots | Where-Object { $Only -contains $_.N } } else { $Shots }
# Computed, not written down: this message said "1-7" for as long as there were seven shots,
# and went stale the moment a shot was added - reporting a valid number as invalid.
if (-not $plan) {
    $valid = ($Shots | ForEach-Object { $_.N }) -join ', '
    throw "No shots matched -Only $($Only -join ','). Valid numbers are $valid."
}

Write-Host ''
# @() around it: $plan is a bare hashtable when exactly one shot matches, and .Count on a
# hashtable is its KEY count - "-Only 9" cheerfully reported "Capturing 6 shot(s)".
Write-Host "  Capturing $(@($plan).Count) shot(s) at ${TargetW}x${TargetH} into $OutDir" -ForegroundColor Cyan
Write-Host '  The window is resized for you. Navigate, then press Enter. S skips, Q quits.' -ForegroundColor DarkGray
Write-Host ''

$done = @()
$failed = @()
foreach ($shot in $plan) {
    Write-Host ("  [{0}/{1}] {2}" -f $shot.N, $Shots.Count, $shot.Name) -ForegroundColor White
    Write-Host ("        {0}" -f $shot.State) -ForegroundColor Gray
    Write-Host ("        ! {0}" -f $shot.Watch) -ForegroundColor DarkYellow

    $rect = Set-WindowExact -Handle $handle
    if ($rect.W -ne $TargetW -or $rect.H -ne $TargetH) {
        Write-Host ("        window is $($rect.W)x$($rect.H), not ${TargetW}x${TargetH} - it may have a " +
                    "minimum size. Capturing anyway; check the result.") -ForegroundColor Yellow
    }

    if ($Auto) {
        if (-not $shot.Nav) { Write-Host '        no Nav block - skipped.' -ForegroundColor Yellow; Write-Host ''; continue }
        $ok = $false
        try { $ok = [bool](& $shot.Nav | Select-Object -Last 1) } catch { $ok = $false }
        if (-not $ok) {
            # Named, not photographed hopefully. The summary lists what is missing and a partial set
            # cannot be uploaded, so a failed Nav stops the set rather than poisoning it.
            Write-Host '        could not reach this state - NOT captured.' -ForegroundColor Red
            Write-Host ''
            $failed += $shot.Name
            continue
        }
        Start-Sleep -Milliseconds 900
    }
    else {
        $ans = Read-Host '        Ready? [Enter=capture / s=skip / q=quit]'
        if ($ans -eq 'q') { Write-Host '        Stopped.' -ForegroundColor Yellow; break }
        if ($ans -eq 's') { Write-Host '        Skipped.' -ForegroundColor DarkGray; Write-Host ''; continue }
    }

    # Re-assert position and give the prompt's focus steal time to settle before reading the desktop.
    #
    # EXCEPT for a shot whose state a reposition destroys. SetWindowPos light-dismisses a WinUI
    # Flyout, so shot 9 navigated correctly, verified correctly, and then photographed the screen
    # BEHIND the thing it was meant to show - a capture that looks finished, which is the failure
    # this whole -Auto path exists to avoid. The window was already sized before Nav ran, so for
    # those shots the second assert only costs the state.
    if ($shot.Fragile) {
        Write-Host '        (not re-asserting the window: a reposition dismisses this state)' -ForegroundColor DarkGray
    } else {
        $rect = Set-WindowExact -Handle $handle
    }
    Hide-Pointer
    Start-Sleep -Milliseconds 600

    $path = Join-Path $OutDir "$($shot.Name).png"
    $dims = Save-Capture -Rect $rect -Path $path
    $kb = [math]::Round((Get-Item $path).Length / 1KB)
    Write-Host ("        saved $($shot.Name).png  $dims  ${kb} KB") -ForegroundColor Green
    Write-Host ''
    $done += [pscustomobject]@{ Shot = $shot.Name; Dims = $dims; KB = $kb }
}

Write-Host '  ---' -ForegroundColor DarkGray
if ($failed) {
    Write-Host ("  {0} shot(s) could not be navigated to: {1}" -f $failed.Count, ($failed -join ', ')) -ForegroundColor Red
}
if ($done) {
    $done | Format-Table -AutoSize
    $bad = $done | Where-Object { $_.Dims -ne "${TargetW}x${TargetH}" }
    if ($bad) {
        Write-Host "  $($bad.Count) capture(s) are NOT ${TargetW}x${TargetH}. Do not upload those." -ForegroundColor Red
    } else {
        Write-Host "  All captures are ${TargetW}x${TargetH}." -ForegroundColor Green
    }
    $missing = $Shots | Where-Object { -not (Test-Path (Join-Path $OutDir "$($_.Name).png")) }
    if ($missing) {
        Write-Host "  Still missing: $($missing.Name -join ', ')" -ForegroundColor Yellow
        Write-Host '  A partial set cannot be uploaded - a half-swapped listing reads as a rendering bug.' -ForegroundColor Yellow
    } else {
        Write-Host "  Full set of 7 present in $OutDir" -ForegroundColor Green
    }
} else {
    Write-Host '  Nothing captured.' -ForegroundColor Yellow
}
Write-Host ''
