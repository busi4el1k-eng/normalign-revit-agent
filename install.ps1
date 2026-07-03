# Normalign Revit Agent — build + install for the current user.
# Works on any PC: no admin rights needed, no hardcoded paths.
#
# Usage (from the repo folder):
#   powershell -ExecutionPolicy Bypass -File install.ps1
#   powershell -ExecutionPolicy Bypass -File install.ps1 -RevitVersion 2027 -NoBuild

param(
    [string]$RevitVersion = "2027",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot

# 1. Build (unless -NoBuild)
if (-not $NoBuild) {
    Write-Host "Building..." -ForegroundColor Cyan
    dotnet build "$repo\NormalignRevitAgent.csproj" -c Debug
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

$dll = Join-Path $repo "bin\NormalignRevitAgent.dll"
if (-not (Test-Path $dll)) { throw "DLL not found: $dll (build first)" }

# 2. Per-user add-ins folder — the location Revit 2025+ accepts without admin.
#    (Revit 2027 rejects the old C:\ProgramData location; all-users installs
#     must go to C:\Program Files\Autodesk\Revit\Addins\<year>, which needs admin.)
$addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$target = Join-Path $addins "NormalignRevitAgent"
New-Item -ItemType Directory -Force -Path $target | Out-Null

# 3. Deploy: our DLL + dependency DLLs (WebView2 SDK) + the native loader,
#    manifest next to the subfolder in the Addins root.
Copy-Item "$repo\bin\*.dll" $target -Force
Copy-Item "$repo\bin\*.pdb" $target -Force -ErrorAction SilentlyContinue
Copy-Item "$repo\bin\NormalignRevitAgent.deps.json" $target -Force -ErrorAction SilentlyContinue
if (Test-Path "$repo\bin\runtimes") {
    Copy-Item "$repo\bin\runtimes" $target -Recurse -Force
}
Copy-Item "$repo\bin\Assets" $target -Recurse -Force
Copy-Item "$repo\NormalignRevitAgent.addin" $addins -Force

# 4. Clean up the obsolete ProgramData copy if a previous install left one.
$old = "C:\ProgramData\Autodesk\Revit\Addins\$RevitVersion\NormalignRevitAgent.addin"
if (Test-Path $old) {
    Remove-Item $old -Force -ErrorAction SilentlyContinue
    Write-Host "Removed obsolete manifest: $old" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Installed for user '$env:USERNAME':" -ForegroundColor Green
Write-Host "  $addins\NormalignRevitAgent.addin"
Write-Host "  $target\NormalignRevitAgent.dll"
Write-Host ""
Write-Host "Restart Revit $RevitVersion and accept the 'unsigned add-in' dialog (Always Load)." -ForegroundColor Green
