[CmdletBinding()]
param(
    [string] $AutoCadInstall = 'C:\Program Files\Autodesk\AutoCAD 2024',
    [string] $OutputRoot = '',
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pluginProject = Join-Path $repoRoot 'src\TeyPdfCad.AutoCAD\TeyPdfCad.AutoCAD.csproj'
$bridgeProject = Join-Path $repoRoot 'src\TeyPdfCad.AutoCAD.Bridge\TeyPdfCad.AutoCAD.Bridge.csproj'
$pluginOutput = Join-Path $repoRoot 'src\TeyPdfCad.AutoCAD\bin\Release\net48'
$bridgeOutput = Join-Path $repoRoot 'src\TeyPdfCad.AutoCAD.Bridge\bin\Release\net8.0-windows'

function Assert-File([string] $Path, [string] $Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label was not found: $Path"
    }
}

Assert-File (Join-Path $AutoCadInstall 'acad.exe') 'AutoCAD executable'
Assert-File (Join-Path $AutoCadInstall 'accoreconsole.exe') 'AutoCAD Core Console'
if (-not $SkipBuild) {
    $env:TEYPDFCAD_AUTOCAD_LOCAL_API = $AutoCadInstall
    & dotnet build $pluginProject --no-restore -c Release
    if ($LASTEXITCODE -ne 0) { throw "AutoCAD plugin build failed with exit code $LASTEXITCODE." }
    & dotnet build $bridgeProject --no-restore -c Release
    if ($LASTEXITCODE -ne 0) { throw "AutoCAD bridge build failed with exit code $LASTEXITCODE." }
}

Assert-File (Join-Path $pluginOutput 'TeyPdfCad.AutoCAD.dll') 'Built AutoCAD plugin'
Assert-File (Join-Path $pluginOutput 'TeyPdfCad.Core.dll') 'Built Core dependency'
Assert-File (Join-Path $bridgeOutput 'TeyPdfCad.AutoCAD.Bridge.exe') 'Built bridge executable'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'artifacts\autocad-test'
}
$package = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) (Get-Date -Format 'yyyyMMdd_HHmmss')
$pluginPackage = Join-Path $package 'plugin'
$bridgePackage = Join-Path $package 'bridge'
New-Item -ItemType Directory -Force -Path $pluginPackage,$bridgePackage | Out-Null

Copy-Item -LiteralPath (Join-Path $pluginOutput 'TeyPdfCad.AutoCAD.dll') -Destination $pluginPackage
Copy-Item -LiteralPath (Join-Path $pluginOutput 'TeyPdfCad.Core.dll') -Destination $pluginPackage
Get-ChildItem -LiteralPath $bridgeOutput -File |
    Where-Object { $_.Extension -in '.exe', '.dll', '.json' } |
    Copy-Item -Destination $bridgePackage

$instructions = @"
# TeyPdfCad AutoCAD test package

Package: $package
Plugin DLL: $pluginPackage\TeyPdfCad.AutoCAD.dll
Bridge EXE: $bridgePackage\TeyPdfCad.AutoCAD.Bridge.exe
AutoCAD: $(Join-Path $AutoCadInstall 'acad.exe')
Core Console: $(Join-Path $AutoCadInstall 'accoreconsole.exe')
Control fixture (startup/PDFIMPORT only): $(Join-Path $repoRoot 'autocad-live-minimal.pdf')

## Manual AutoCAD plugin smoke

1. Open AutoCAD 2024 and save any active drawing.
2. Run NETLOAD and select the plugin DLL above.
3. Run TEYPDFHEALTH. Expected command-line text: `TeyPdfCad health: ok.`
4. Use the control fixture above only for startup/PDFIMPORT smoke, or use a supplied real vector engineering PDF for semantic acceptance. Run PDFIMPORT, then TEYPDFDUMPALL.
5. Run TEYPDFSHEETCONFIG with measured page bounds before reconstruction. For the current A3 fixture use width 420, height 297, scale 100, MinX 2829.645, and MinY 2065.104.
6. Run TEYPDFRECONSTRUCTALL, then TEYPDFSHEETAUDIT to print Layout, media, block, text, and lineweight evidence.
7. Save a copy of the DWG and return the command-line output plus the fixture JSON path.
8. The control fixture contains only a small text marker; it is not expected to produce native dimensions or an A3 title block. A real imported page may still produce an editable sheet layout from validated linework when no text objects are available.

## One-command A3 acceptance

After saving a DWG that already contains the imported A3 PDF geometry, run the AutoCAD `SCRIPT` command and select `RUN_A3_ACCEPTANCE.scr` from this package. It loads the package plugin, configures the measured A3 bounds, runs reconstruction and sheet audit, and saves the DWG. Use the manual commands above only when the script output needs diagnosis.

For the complete deterministic AutoCAD command pass, select `RUN_FULL_A3_TEST.scr`. It additionally runs the ping and full Model Space fixture dump before reconstruction. PDFIMPORT remains a one-time fixture preparation step because its Russian AutoCAD dialog is not stable in a script.

## Controlled A3 sheet hook

For a measured A3 PDF page, process variables can provide page width, height, and origin before starting AutoCAD. They do not provide the drawing-units-per-millimetre scale; use `TEYPDFSHEETCONFIG` for the scale before `TEYPDFRECONSTRUCTALL` when the import is not 1 unit/mm:

TEYPDFCAD_SHEET_WIDTH_MM=420
TEYPDFCAD_SHEET_HEIGHT_MM=297
TEYPDFCAD_SHEET_MIN_X=0
TEYPDFCAD_SHEET_MIN_Y=0

The sheet path creates an editable Paper Space frame and title-block block when a validated sheet candidate is found, including the geometry-only fallback for imported linework without text. Do not set these variables from guessed geometry.

The bridge is not marked production-ready until Core Console completes with exit code 0,
the status file reports `ok`, and the output DWG is independently inspected.
"@
[IO.File]::WriteAllText((Join-Path $package 'TEST_INSTRUCTIONS.md'), $instructions, [Text.UTF8Encoding]::new($false))

$acceptanceScript = @"
_.FILEDIA
0
_.NETLOAD
"$pluginPackage\TeyPdfCad.AutoCAD.dll"
_.FILEDIA
1
_.TEYPDFHEALTH
_.TEYPDFSHEETCONFIG
420
297
100
2829.645
2065.104
_.TEYPDFRECONSTRUCTALL
_.TEYPDFSHEETAUDIT
_.QSAVE
"@
$acceptanceScriptPath = Join-Path $package 'RUN_A3_ACCEPTANCE.scr'
[IO.File]::WriteAllText($acceptanceScriptPath, $acceptanceScript, [Text.UTF8Encoding]::new($false))

$fullTestScript = @"
_.FILEDIA
0
_.NETLOAD
"$pluginPackage\TeyPdfCad.AutoCAD.dll"
_.FILEDIA
1
_.TEYPDFPING
_.TEYPDFHEALTH
_.TEYPDFDUMPALL
_.TEYPDFSHEETCONFIG
420
297
100
2829.645
2065.104
_.TEYPDFRECONSTRUCTALL
_.TEYPDFSHEETAUDIT
_.QSAVE
"@
$fullTestScriptPath = Join-Path $package 'RUN_FULL_A3_TEST.scr'
[IO.File]::WriteAllText($fullTestScriptPath, $fullTestScript, [Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    package = $package
    pluginDll = Join-Path $pluginPackage 'TeyPdfCad.AutoCAD.dll'
    bridgeExe = Join-Path $bridgePackage 'TeyPdfCad.AutoCAD.Bridge.exe'
    instructions = Join-Path $package 'TEST_INSTRUCTIONS.md'
    acceptanceScript = $acceptanceScriptPath
    fullTestScript = $fullTestScriptPath
} | ConvertTo-Json
