<#
.SYNOPSIS
    Captures the seven Microsoft Store listing screenshots at exactly 1920x1080.

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

.EXAMPLE
    pwsh scripts/capture-store-screenshots.ps1
    pwsh scripts/capture-store-screenshots.ps1 -Only 3,7
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$Version = '0.18',
    [int[]]$Only
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
       Watch = 'The hero shot. Make sure several games have real cover art and no row is mid-load.' }
    @{ N = 2; Name = '02-game-mods-view'
       State = 'Open a game with a healthy mod list. Elden Ring or Windrose.'
       Watch  = 'Show a mix of enabled and disabled so the toggle reads as a toggle.' }
    @{ N = 3; Name = '03-browse-nexus'
       State = 'Browse Nexus, results loaded.'
       Watch  = 'Signed in, and let thumbnails finish. A grid of grey placeholders is a bad advert.' }
    @{ N = 4; Name = '04-updates-view'
       State = 'The updates view with at least one update pending.'
       Watch  = 'An empty updates list says nothing about the feature.' }
    @{ N = 5; Name = '05-add-game'
       State = 'The +Game dialog, open over the library.'
       Watch  = 'Do not show a path containing anything you would not publish.' }
    @{ N = 6; Name = '06-settings'
       State = 'Settings, scrolled to show the theme picker.'
       Watch  = 'THE POINT OF THIS RETAKE. Confirm the selected theme is navy (626 Labs), not Forge.' }
    @{ N = 7; Name = '07-saves-snapshots'
       State = 'Saves / snapshots for a game that has some.'
       Watch  = 'Real snapshots with real timestamps. This is the reversibility promise made visible.' }
)

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

function Set-WindowExact {
    param([IntPtr]$Handle)
    if ([Win32Cap]::IsIconic($Handle)) { [void][Win32Cap]::ShowWindow($Handle, 9) } # SW_RESTORE
    [void][Win32Cap]::SetForegroundWindow($Handle)
    # HWND_TOP, SWP_NOACTIVATE(0x0010) off so it comes forward and stays put between shots.
    [void][Win32Cap]::SetWindowPos($Handle, [IntPtr]::Zero, 0, 0, $TargetW, $TargetH, 0x0040)
    Start-Sleep -Milliseconds 400

    $r = New-Object Win32Cap+RECT
    [void][Win32Cap]::GetWindowRect($Handle, [ref]$r)
    return @{ X = $r.Left; Y = $r.Top; W = $r.Right - $r.Left; H = $r.Bottom - $r.Top }
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

Write-Host ''
Write-Host '  STEP 0 - before anything else:' -ForegroundColor Magenta
Write-Host '  Set the theme to 626 Labs (navy) in the toolbar theme picker. That is the entire point' -ForegroundColor Magenta
Write-Host '  of this retake - the live listing shows Forge and the app now opens navy.' -ForegroundColor Magenta
Read-Host '  Press Enter once the app is navy'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$plan = if ($Only) { $Shots | Where-Object { $Only -contains $_.N } } else { $Shots }
if (-not $plan) { throw "No shots matched -Only $($Only -join ','). Valid numbers are 1-7." }

Write-Host ''
Write-Host "  Capturing $($plan.Count) shot(s) at ${TargetW}x${TargetH} into $OutDir" -ForegroundColor Cyan
Write-Host '  The window is resized for you. Navigate, then press Enter. S skips, Q quits.' -ForegroundColor DarkGray
Write-Host ''

$done = @()
foreach ($shot in $plan) {
    Write-Host ("  [{0}/7] {1}" -f $shot.N, $shot.Name) -ForegroundColor White
    Write-Host ("        {0}" -f $shot.State) -ForegroundColor Gray
    Write-Host ("        ! {0}" -f $shot.Watch) -ForegroundColor DarkYellow

    $rect = Set-WindowExact -Handle $handle
    if ($rect.W -ne $TargetW -or $rect.H -ne $TargetH) {
        Write-Host ("        window is $($rect.W)x$($rect.H), not ${TargetW}x${TargetH} - it may have a " +
                    "minimum size. Capturing anyway; check the result.") -ForegroundColor Yellow
    }

    $ans = Read-Host '        Ready? [Enter=capture / s=skip / q=quit]'
    if ($ans -eq 'q') { Write-Host '        Stopped.' -ForegroundColor Yellow; break }
    if ($ans -eq 's') { Write-Host '        Skipped.' -ForegroundColor DarkGray; Write-Host ''; continue }

    # Re-assert position and give the prompt's focus steal time to settle before reading the desktop.
    $rect = Set-WindowExact -Handle $handle
    Start-Sleep -Milliseconds 600

    $path = Join-Path $OutDir "$($shot.Name).png"
    $dims = Save-Capture -Rect $rect -Path $path
    $kb = [math]::Round((Get-Item $path).Length / 1KB)
    Write-Host ("        saved $($shot.Name).png  $dims  ${kb} KB") -ForegroundColor Green
    Write-Host ''
    $done += [pscustomobject]@{ Shot = $shot.Name; Dims = $dims; KB = $kb }
}

Write-Host '  ---' -ForegroundColor DarkGray
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
