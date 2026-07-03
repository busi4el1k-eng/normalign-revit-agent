# Construiește add-in-ul (Release) și compilează installer-ul .exe cu Inno Setup.
#
# Cerință o singură dată pe stația de build: Inno Setup 6 (gratuit)
#   https://jrsoftware.org/isdl.php
#
# Utilizare:
#   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent   # rădăcina repo-ului

Write-Host "1/3  Build add-in (Release)..." -ForegroundColor Cyan
dotnet build "$root\NormalignRevitAgent.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Build eșuat." }

# Găsește compilatorul Inno Setup (ISCC.exe)
$iscc = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
  throw "Inno Setup (ISCC.exe) nu a fost gasit. Instaleaza de la https://jrsoftware.org/isdl.php"
}

# Bootstrapper WebView2 (opțional — pentru instalare offline a runtime-ului)
$boot = "$PSScriptRoot\redist\MicrosoftEdgeWebview2Setup.exe"
if (-not (Test-Path $boot)) {
  Write-Host "2/3  Descarc bootstrapper-ul WebView2 (pentru instalare offline)..." -ForegroundColor Cyan
  try {
    Invoke-WebRequest "https://go.microsoft.com/fwlink/p/?LinkId=2124703" -OutFile $boot
  } catch {
    Write-Warning "Nu am putut descarca bootstrapper-ul WebView2; installer-ul va cere instalarea manuala daca lipseste."
  }
} else { Write-Host "2/3  Bootstrapper WebView2 deja prezent." -ForegroundColor Cyan }

Write-Host "3/3  Compilez installer-ul..." -ForegroundColor Cyan
& $iscc "$PSScriptRoot\normalign-revit-agent.iss"
if ($LASTEXITCODE -ne 0) { throw "Compilarea installer-ului a esuat." }

Write-Host ""
Write-Host "GATA. Installer-ul este in: $PSScriptRoot\Output\" -ForegroundColor Green
