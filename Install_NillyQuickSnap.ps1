# ============================================================================
# NillyQuickSnap — Universal Windows 10/11 Installer (Zero Admin · Auto-Start)
# created by Nilly graphics (Sotiris Christos) — https://nillygraphics.com/
# Ultra-Light Replacement for Windows Snipping Tool (585.8 MB -> ~40 KB!)
# ============================================================================
param([switch]$Silent)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exePath = Join-Path $root 'NillyQuickSnap.exe'
$icoPath = Join-Path $root 'NillyQuickSnap.ico'
$srcPath = Join-Path $root 'desktop\NillyQuickSnap.cs'

Write-Host ''
Write-Host '  ============================================================' -ForegroundColor Cyan
Write-Host '   NillyQuickSnap — Ultra-Light Snipping Tool Replacement' -ForegroundColor White
Write-Host '   created by Nilly graphics (Sotiris Christos)' -ForegroundColor DarkGray
Write-Host '   https://nillygraphics.com/' -ForegroundColor DarkGray
Write-Host '  ============================================================' -ForegroundColor Cyan
Write-Host ''

# Stop running daemon before recompiling
Stop-Process -Name 'NillyQuickSnap' -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 200

# 1. Unblock files if extracted from ZIP
Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue

# 2. Auto-compile NillyQuickSnap.exe
$needBuild = (-not (Test-Path $exePath)) -or ((Test-Path $srcPath) -and ((Get-Item $srcPath).LastWriteTimeUtc -gt (Get-Item $exePath).LastWriteTimeUtc))
if ($needBuild) {
    $fxDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
    if (-not (Test-Path "$fxDir\csc.exe")) { $fxDir = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319' }
    $csc = Join-Path $fxDir 'csc.exe'
    if (-not (Test-Path $csc)) { throw 'Δεν βρέθηκε ο .NET Framework 4.x C# compiler (csc.exe).' }
    Write-Host '  [1/4] Μεταγλώττιση NillyQuickSnap.exe (100% Native Windows Host)...' -ForegroundColor Yellow
    $cscArgs = @(
        '/nologo', '/target:winexe', '/optimize+', '/utf8output', "/out:$exePath",
        '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
    )
    if (Test-Path $icoPath) { $cscArgs += "/win32icon:$icoPath" }
    $cscArgs += $srcPath
    & $csc @cscArgs
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exePath)) {
        throw "Αποτυχία μεταγλώττισης NillyQuickSnap.exe (exit $LASTEXITCODE)"
    }
    Write-Host ("        OK -> " + [math]::Round((Get-Item $exePath).Length / 1KB, 1) + " KB") -ForegroundColor Green
} else {
    Write-Host '  [1/4] NillyQuickSnap.exe έτοιμο.' -ForegroundColor Green
}

# 3. Register the per-user startup entry.
Write-Host '  [2/4] Ρύθμιση εκκίνησης με τα Windows...' -ForegroundColor Yellow
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (-not (Test-Path -LiteralPath $runKey)) { New-Item -Path $runKey -Force | Out-Null }
Set-ItemProperty -Path $runKey -Name 'NillyQuickSnap' -Value "`"$exePath`""
Write-Host '        OK -> Αυτόματη εκκίνηση ενεργή.' -ForegroundColor Green

# 4. Desktop & Start Menu Shortcuts
Write-Host '  [3/4] Δημιουργία συντομεύσεων (Επιφάνεια Εργασίας & Start Menu)...' -ForegroundColor Yellow
$wsh = New-Object -ComObject WScript.Shell
$desktopDir = [Environment]::GetFolderPath('Desktop')
$startMenuDir = Join-Path ([Environment]::GetFolderPath('Programs')) 'Nilly graphics'
New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null

foreach ($lnkPath in @(
    (Join-Path $desktopDir 'NillyQuickSnap.lnk'),
    (Join-Path $startMenuDir 'NillyQuickSnap.lnk')
)) {
    $sc = $wsh.CreateShortcut($lnkPath)
    $sc.TargetPath = $exePath
    $sc.WorkingDirectory = $root
    $sc.Description = 'NillyQuickSnap — Ultra-Light Screen Capture & Pixel Ruler | created by Nilly graphics'
    if (Test-Path $icoPath) { $sc.IconLocation = "$icoPath,0" }
    $sc.Save()
}
Write-Host '        OK -> Έτοιμες οι συντομεύσεις!' -ForegroundColor Green

# 5. Start Tray Daemon immediately
Write-Host '  [4/4] Εκκίνηση NillyQuickSnap στο System Tray...' -ForegroundColor Yellow
if (-not $Silent) { Start-Process -FilePath $exePath -WorkingDirectory $root }
if (-not $Silent) { Write-Host '        OK -> Τρέχει στο System Tray (κάτω δεξιά).' -ForegroundColor Green }
Write-Host ''
Write-Host '  ΧΡΗΣΗ: Ctrl+Alt+S ή κλικ στο εικονίδιο στο Tray' -ForegroundColor Cyan
Write-Host '   -> Σύρε & άφησε = Copy στο Clipboard + Αυτόματο Save στα «Στιγμιότυπα οθόνης»!' -ForegroundColor White
Write-Host '   -> Κράτα Shift όταν αφήνεις (ή Ctrl+Alt+Shift+S) = Εργαλεία Σχεδίασης (Βέλος/Πλαίσιο/Blur)' -ForegroundColor White
Write-Host ''
