[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $AutoCadPath,
    [Parameter(Mandatory)] [string] $PluginDll,
    [Parameter(Mandatory)] [string] $BaseDrawing,
    [string] $CoreConsolePath,
    [int] $TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $AutoCadPath -PathType Leaf)) { throw "AutoCAD executable was not found: $AutoCadPath" }
if (-not (Test-Path -LiteralPath $PluginDll -PathType Leaf)) { throw "Plugin DLL was not found: $PluginDll" }
if (-not (Test-Path -LiteralPath $BaseDrawing -PathType Leaf)) { throw "Base drawing was not found: $BaseDrawing" }
if ($TimeoutSeconds -lt 30) { throw 'TimeoutSeconds must be at least 30 seconds.' }
$ConfigurationFile = Join-Path (Split-Path -Parent $AutoCadPath) 'acad2022.cfg'
if (-not (Test-Path -LiteralPath $ConfigurationFile -PathType Leaf)) {
    throw "AutoCAD configuration was not found beside AutoCAD: $ConfigurationFile."
}
if ([string]::IsNullOrWhiteSpace($CoreConsolePath)) {
    $CoreConsolePath = Join-Path (Split-Path -Parent $AutoCadPath) 'accoreconsole.exe'
}
if (-not (Test-Path -LiteralPath $CoreConsolePath -PathType Leaf)) { throw "AutoCAD Core Console was not found: $CoreConsolePath" }

$runDirectory = Join-Path ([IO.Path]::GetTempPath()) ("TeyPdfCad-smoke-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
$isolatedUserData = Join-Path $runDirectory 'userdata'
New-Item -ItemType Directory -Force -Path $isolatedUserData | Out-Null
$workingDwg = Join-Path $runDirectory 'base.dwg'
Copy-Item -LiteralPath $BaseDrawing -Destination $workingDwg
$scriptPath = Join-Path $runDirectory 'smoke.scr'
$sentinelPath = Join-Path $runDirectory 'health.txt'
$pluginPath = (Resolve-Path -LiteralPath $PluginDll).Path.Replace('"', '""')
$lines = @(
    '_.FILEDIA', '0',
    '_.CMDECHO', '1',
    '_.SECURELOAD', '0',
    '_.NETLOAD', ('"' + $pluginPath + '"'),
    '_.TEYPDFHEALTH'
)
[IO.File]::WriteAllLines($scriptPath, $lines, [Text.UTF8Encoding]::new($false))

$previousSentinel = $env:TEYPDFCAD_HEALTH_FILE
$env:TEYPDFCAD_HEALTH_FILE = $sentinelPath
try {
    $arguments = @(
        '/i', $workingDwg,
        '/s', $scriptPath,
        '/isolate', ('teypdfcad-smoke-' + [guid]::NewGuid().ToString('N')), $isolatedUserData,
        '/l', 'ru-RU'
    )
    $process = Start-Process -FilePath (Resolve-Path -LiteralPath $CoreConsolePath).Path `
        -ArgumentList $arguments `
        -WorkingDirectory $runDirectory -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $process.HasExited -and -not (Test-Path -LiteralPath $sentinelPath) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
    }
    $healthy = Test-Path -LiteralPath $sentinelPath
    if (-not $process.HasExited) {
        try { $process.Kill($true) } catch { }
        try { $process.WaitForExit() } catch { }
    }
    if (-not $healthy) {
        if ($process.ExitCode -ne 0) { throw "AutoCAD Core Console exited with code $($process.ExitCode). Run directory: $runDirectory" }
        throw "TEYPDFHEALTH did not write the sentinel. AutoCAD may not have loaded the plugin. Run directory: $runDirectory"
    }

    [pscustomobject]@{
        status = 'ok'
        autoCadExitCode = 0
        sentinel = $sentinelPath
        runDirectory = $runDirectory
    } | ConvertTo-Json
}
finally {
    $env:TEYPDFCAD_HEALTH_FILE = $previousSentinel
}
