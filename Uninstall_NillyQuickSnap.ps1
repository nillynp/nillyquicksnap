# ============================================================================
# NillyQuickSnap — Clean Uninstaller (Zero Admin · HKCU)
# created by Nilly graphics (Sotiris Christos) — https://nillygraphics.com/
# ============================================================================
param([switch]$Silent)

$ErrorActionPreference = 'SilentlyContinue'
Write-Host ''
Write-Host '  Αφαίρεση NillyQuickSnap από Startup, Registry & συντομεύσεις...' -ForegroundColor Yellow

Stop-Process -Name 'NillyQuickSnap' -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'NillyQuickSnap' -Force

$desktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'NillyQuickSnap.lnk'
$startLnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Nilly graphics\NillyQuickSnap.lnk'
if (Test-Path $desktopLnk) { Remove-Item $desktopLnk -Force }
if (Test-Path $startLnk) { Remove-Item $startLnk -Force }

Write-Host '  OK -> Αφαιρέθηκε καθαρά το NillyQuickSnap.' -ForegroundColor Green
Write-Host ''
if (-not $Silent) { Read-Host '  Πάτησε Enter για κλείσιμο' }
