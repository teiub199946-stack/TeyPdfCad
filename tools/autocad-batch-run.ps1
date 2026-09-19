[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $AutoCadPath,
    [Parameter(Mandatory)] [string] $PluginDll,
    [Parameter(Mandatory)] [string] $InputDwg,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [int] $TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'

function Assert-File([string] $Path, [string] $Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label was not found: $Path"
    }
}

Assert-File $AutoCadPath 'AutoCAD executable'
Assert-File $PluginDll 'TeyPdfCad plugin DLL'
Assert-File $InputDwg 'Input DWG fixture'
$ConfigurationFile = Join-Path (Split-Path -Parent $AutoCadPath) 'acad2022.cfg'
Assert-File $ConfigurationFile 'AutoCAD configuration'
if ($TimeoutSeconds -lt 30) { throw 'TimeoutSeconds must be at least 30 seconds.' }

$AutoCadPath = (Resolve-Path -LiteralPath $AutoCadPath).Path
$PluginDll = (Resolve-Path -LiteralPath $PluginDll).Path
$InputDwg = (Resolve-Path -LiteralPath $InputDwg).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$runId = Get-Date -Format 'yyyyMMdd_HHmmssfff'
$runDirectory = Join-Path $OutputDirectory "autocad-$runId"
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
$workingDwg = Join-Path $runDirectory 'working.dwg'
$scriptPath = Join-Path $runDirectory 'run.scr'
$outputDwg = Join-Path $runDirectory 'result.dwg'
Copy-Item -LiteralPath $InputDwg -Destination $workingDwg

function ScrQuote([string] $Value) {
    return '"' + $Value.Replace('"', '""') + '"'
}

$script = @(
    '_.FILEDIA', '0',
    '_.CMDECHO', '1',
    '_.NETLOAD', (ScrQuote $PluginDll),
    '_.OPEN', (ScrQuote $workingDwg),
    '_.TEYPDFDUMPALL',
    '_.TEYPDFRECONSTRUCTALL',
    '_.QSAVE',
    '_.QUIT', '_Y'
)
[IO.File]::WriteAllLines($scriptPath, $script, [Text.UTF8Encoding]::new($false))

$arguments = @(
    '/nologo',
    '/b', $scriptPath
)
$process = Start-Process -FilePath $AutoCadPath -ArgumentList $arguments -WorkingDirectory $runDirectory -PassThru
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $process.Refresh()
}

if (-not $process.HasExited) {
    try { $process.Kill($true) } catch { }
    throw "AutoCAD did not exit within $TimeoutSeconds seconds. Run directory: $runDirectory"
}

if ($process.ExitCode -ne 0) {
    throw "AutoCAD exited with code $($process.ExitCode). Run directory: $runDirectory"
}

if (-not (Test-Path -LiteralPath $workingDwg -PathType Leaf)) {
    throw "AutoCAD did not produce the working DWG. Run directory: $runDirectory"
}

Copy-Item -LiteralPath $workingDwg -Destination $outputDwg -Force
$fixtures = Get-ChildItem -Path ([IO.Path]::GetTempPath(), 'TeyPdfCad') -Filter 'PrimitiveScene_*.json' -File -ErrorAction SilentlyContinue |
    Where-Object LastWriteTime -ge (Get-Date).AddMinutes(-5) |
    Sort-Object LastWriteTime -Descending
if ($fixtures | Select-Object -First 1) {
    Copy-Item -LiteralPath ($fixtures | Select-Object -First 1).FullName -Destination (Join-Path $runDirectory 'fixture.json') -Force
}

[pscustomobject]@{
    runDirectory = $runDirectory
    outputDwg = $outputDwg
    fixtureJson = Join-Path $runDirectory 'fixture.json'
    autoCadExitCode = $process.ExitCode
} | ConvertTo-Json
