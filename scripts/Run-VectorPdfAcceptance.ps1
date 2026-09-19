[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$InputPdf,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$AutoCadCoreConsole,

    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $PSScriptRoot
$cliProject = Join-Path $scriptRoot 'src\TeyPdfCad.Cli\TeyPdfCad.Cli.csproj'
$pluginAssembly = Join-Path $scriptRoot "src\TeyPdfCad.AutoCAD\bin\$Configuration\net48\TeyPdfCad.AutoCAD.dll"
$inputFullPath = (Resolve-Path -LiteralPath $InputPdf).Path
$outputFullPath = [System.IO.Path]::GetFullPath($OutputDirectory)

New-Item -ItemType Directory -Force -Path $outputFullPath | Out-Null
$dwgPath = Join-Path $outputFullPath 'converted.dwg'
$reportPath = Join-Path $outputFullPath 'conversion-report.json'
$auditScriptPath = Join-Path $outputFullPath 'RUN_VECTOR_PDF_ACCEPTANCE.scr'
$auditLogPath = Join-Path $outputFullPath 'autocad-audit.log'

& dotnet run --project $cliProject --configuration $Configuration -- convert --input $inputFullPath --output $dwgPath --report $reportPath
if ($LASTEXITCODE -ne 0) {
    throw "Vector conversion failed with exit code $LASTEXITCODE. Inspect $reportPath."
}

if (-not (Test-Path -LiteralPath $pluginAssembly -PathType Leaf)) {
    throw "AutoCAD plugin was not built: $pluginAssembly. Build src/TeyPdfCad.AutoCAD first."
}

$auditScript = @"
_.NETLOAD
$pluginAssembly
TEYPDFAUDITDWG
_.QSAVE
_.QUIT
"@
Set-Content -LiteralPath $auditScriptPath -Value $auditScript -Encoding ASCII

$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$manifest = [ordered]@{
    inputPdf = $inputFullPath
    inputSha256 = (Get-FileHash -LiteralPath $inputFullPath -Algorithm SHA256).Hash
    dwg = $dwgPath
    dwgSha256 = (Get-FileHash -LiteralPath $dwgPath -Algorithm SHA256).Hash
    report = $reportPath
    pagesRead = $report.pagesRead
    pagesProcessed = $report.pagesProcessed
    complete = $report.complete
    autoCadAuditScript = $auditScriptPath
}

if ($AutoCadCoreConsole) {
    if (-not (Test-Path -LiteralPath $AutoCadCoreConsole -PathType Leaf)) {
        throw "AutoCAD Core Console was not found: $AutoCadCoreConsole"
    }
    & $AutoCadCoreConsole /i $dwgPath /s $auditScriptPath | Tee-Object -FilePath $auditLogPath
    if ($LASTEXITCODE -ne 0) {
        throw "AutoCAD audit failed with exit code $LASTEXITCODE. Inspect $auditLogPath."
    }
    $manifest.autoCadAuditLog = $auditLogPath
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputFullPath 'acceptance-manifest.json') -Encoding UTF8
Write-Host "Acceptance package created: $outputFullPath"
Write-Host "Run $auditScriptPath in AutoCAD if -AutoCadCoreConsole was not supplied."
