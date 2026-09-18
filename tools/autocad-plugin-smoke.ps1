[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $AutoCadPath,
    [Parameter(Mandatory)] [string] $PluginDll,
    [string] $ProfileName,
    [string] $CoreConsolePath,
    [int] $TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $AutoCadPath -PathType Leaf)) { throw "AutoCAD executable was not found: $AutoCadPath" }
if (-not (Test-Path -LiteralPath $PluginDll -PathType Leaf)) { throw "Plugin DLL was not found: $PluginDll" }
if ($TimeoutSeconds -lt 30) { throw 'TimeoutSeconds must be at least 30 seconds.' }
$configPath = Join-Path (Split-Path -Parent $AutoCadPath) 'acad2022.cfg'
if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw "AutoCAD configuration was not found: $configPath. Repair or initialize AutoCAD before running Core Console smoke."
}
if ([string]::IsNullOrWhiteSpace($CoreConsolePath)) {
    $CoreConsolePath = Join-Path (Split-Path -Parent $AutoCadPath) 'accoreconsole.exe'
}
if (-not (Test-Path -LiteralPath $CoreConsolePath -PathType Leaf)) { throw "AutoCAD Core Console was not found: $CoreConsolePath" }

$runDirectory = Join-Path ([IO.Path]::GetTempPath()) ("TeyPdfCad-smoke-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
$scriptPath = Join-Path $runDirectory 'smoke.scr'
$sentinelPath = Join-Path $runDirectory 'health.txt'
$pluginPath = (Resolve-Path -LiteralPath $PluginDll).Path.Replace('"', '""')
$lines = @(
    '_.FILEDIA', '0',
    '_.CMDECHO', '1',
    '_.NETLOAD', ('"' + $pluginPath + '"'),
    '_.TEYPDFHEALTH',
    '_.QUIT', 'Y'
)
[IO.File]::WriteAllLines($scriptPath, $lines, [Text.UTF8Encoding]::new($false))

$previousSentinel = $env:TEYPDFCAD_HEALTH_FILE
$env:TEYPDFCAD_HEALTH_FILE = $sentinelPath
try {
    $arguments = @('/s', $scriptPath)
    if (-not [string]::IsNullOrWhiteSpace($ProfileName)) {
        $arguments = @('/p', $ProfileName) + $arguments
    }
    $process = Start-Process -FilePath (Resolve-Path -LiteralPath $CoreConsolePath).Path `
        -ArgumentList $arguments `
        -WorkingDirectory $runDirectory -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
    }
    if (-not $process.HasExited) {
        try { $process.Kill($true) } catch { }
        throw "AutoCAD did not exit within $TimeoutSeconds seconds. Run directory: $runDirectory"
    }
    if ($process.ExitCode -ne 0) { throw "AutoCAD Core Console exited with code $($process.ExitCode). Run directory: $runDirectory" }
    if (-not (Test-Path -LiteralPath $sentinelPath -PathType Leaf)) {
        throw "TEYPDFHEALTH did not write the sentinel. AutoCAD may not have loaded the plugin. Run directory: $runDirectory"
    }

    [pscustomobject]@{
        status = 'ok'
        autoCadExitCode = $process.ExitCode
        sentinel = $sentinelPath
        runDirectory = $runDirectory
    } | ConvertTo-Json
}
finally {
    $env:TEYPDFCAD_HEALTH_FILE = $previousSentinel
}
