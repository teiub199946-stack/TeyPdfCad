[CmdletBinding()]
param(
    [ValidateSet('Structural', 'Both')]
    [string] $Mode = 'Structural',
    [string] $ArtifactRoot = $env:TEYPDFCAD_TEXT_FIT_ARTIFACT_ROOT,
    [string] $CoreConsolePath = $env:TEYPDFCAD_TEXT_FIT_CORE_CONSOLE_PATH,
    [int] $TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = Join-Path $repoRoot '..\output\text-fidelity-review\spike-text-fit-report'
}
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
New-Item -ItemType Directory -Force -Path $ArtifactRoot | Out-Null
$env:TEYPDFCAD_TEXT_FIT_ARTIFACT_ROOT = $ArtifactRoot

$project = Join-Path $repoRoot 'tests\TeyPdfCad.Dwg.Tests\TeyPdfCad.Dwg.Tests.csproj'
$testResult = & dotnet test $project `
    --filter 'FullyQualifiedName~TextFitSpikeTests' `
    --no-restore `
    2>&1
$testExitCode = $LASTEXITCODE
$testResult | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'focused-test.log')
if ($testExitCode -ne 0) {
    throw "Focused TextFitSpikeTests failed with exit code $testExitCode. See focused-test.log."
}

$summaryPath = Join-Path $ArtifactRoot 'read-back-summary.json'
$summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json

function Get-Sha256([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Get-RendererVersion([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or
        -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }
    return (Get-Item -LiteralPath $Path).VersionInfo.FileVersion
}

function Invoke-CoreConsolePhase {
    param(
        [string] $Executable,
        [string] $InputDwg,
        [string] $ScriptPath,
        [string] $OutputLog,
        [string] $ErrorLog
    )

    $arguments = @('/i', $InputDwg, '/s', $ScriptPath, '/l', 'en-US')
    $process = Start-Process -FilePath $Executable `
        -ArgumentList $arguments `
        -WorkingDirectory (Split-Path -Parent $InputDwg) `
        -RedirectStandardOutput $OutputLog `
        -RedirectStandardError $ErrorLog `
        -WindowStyle Hidden `
        -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
    }
    if (-not $process.HasExited) {
        try { $process.Kill($true) } catch { }
        return [pscustomobject]@{ status = 'timeout'; exitCode = $null }
    }
    [pscustomobject]@{
        status = if ($process.ExitCode -eq 0) { 'completed' } else { 'failed' }
        exitCode = $process.ExitCode
    }
}

function Invoke-AutoCadCandidate {
    param(
        [string] $CandidateName,
        [string] $InputDwg,
        [string] $Executable
    )

    $directory = Join-Path $ArtifactRoot "autocad\$CandidateName"
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $workingDwg = Join-Path $directory 'roundtrip.dwg'
    $saveScript = Join-Path $directory 'save-and-exit.scr'
    $renderScript = Join-Path $directory 'reopen-and-render.scr'
    $png = Join-Path $directory 'render.png'
    Copy-Item -LiteralPath $InputDwg -Destination $workingDwg -Force
    [IO.File]::WriteAllLines(
        $saveScript,
        @('_.FILEDIA', '0', '_.CMDECHO', '1', '_.QSAVE', '_.QUIT', '_Y'),
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllLines(
        $renderScript,
        @('_.FILEDIA', '0', '_.CMDECHO', '1', '_.ZOOM', '_E', '_.PNGOUT', $png, '_.QSAVE', '_.QUIT', '_Y'),
        [Text.UTF8Encoding]::new($false))

    $save = Invoke-CoreConsolePhase $Executable $workingDwg $saveScript `
        (Join-Path $directory 'save.stdout.log') (Join-Path $directory 'save.stderr.log')
    if ($save.status -ne 'completed') {
        return [pscustomobject]@{
            status = $save.status
            blocker = 'Core Console save/reopen phase failed.'
            inputDwg = $InputDwg
            workingDwg = $workingDwg
            workingDwgSha256 = Get-Sha256 $workingDwg
            renderPng = $png
        }
    }

    $render = Invoke-CoreConsolePhase $Executable $workingDwg $renderScript `
        (Join-Path $directory 'render.stdout.log') (Join-Path $directory 'render.stderr.log')
    [pscustomobject]@{
        status = if ($render.status -eq 'completed' -and (Test-Path -LiteralPath $png)) { 'rendered' } else { $render.status }
        blocker = if ($render.status -eq 'completed' -and (Test-Path -LiteralPath $png)) { $null } else { 'Core Console render did not produce a PNG.' }
        inputDwg = $InputDwg
        workingDwg = $workingDwg
        workingDwgSha256 = Get-Sha256 $workingDwg
        renderPng = $png
    }
}

$rendererVersion = Get-RendererVersion $CoreConsolePath
$autoCad = [ordered]@{
    status = 'blocked'
    rendererVersion = $rendererVersion
    executable = $CoreConsolePath
    candidates = @()
    blocker = if ([string]::IsNullOrWhiteSpace($CoreConsolePath)) {
        'AutoCAD Core Console was not requested; visual result is pending.'
    } elseif ($null -eq $rendererVersion) {
        "AutoCAD Core Console was not found or has no file version: $CoreConsolePath"
    } else {
        $null
    }
}

if ($Mode -eq 'Both' -and $null -ne $rendererVersion) {
    $autoCad.status = 'requested'
    foreach ($candidate in $summary.Candidates) {
        $autoCad.candidates += Invoke-AutoCadCandidate `
            $candidate.Candidate `
            $candidate.DwgPath `
            $CoreConsolePath
    }
    if (($autoCad.candidates | Where-Object status -eq 'rendered').Count -gt 0) {
        $autoCad.status = 'rendered'
        $autoCad.blocker = $null
    }
}

$report = [ordered]@{
    spike = 'native-text-fitting'
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    artifactRoot = $ArtifactRoot
    focusedTest = [ordered]@{
        project = $project
        filter = 'FullyQualifiedName~TextFitSpikeTests'
        status = 'passed'
        log = Join-Path $ArtifactRoot 'focused-test.log'
    }
    fixtureMetadataPath = $summary.FixtureMetadataPath
    naturalWidthReference = $summary.NaturalWidthReference
    candidates = @($summary.Candidates)
    autoCad = $autoCad
    visualStatus = if ($autoCad.status -eq 'rendered') { 'render-artifact-produced; human visual acceptance pending' } else { 'visual-pending' }
}

$reportPath = Join-Path $ArtifactRoot 'report.json'
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath

$markdown = @(
    '# Native text-fitting spike',
    '',
    '- Structural test: **passed**',
    "- Fixture metadata: ``$($summary.FixtureMetadataPath)``",
    "- Artifact root: ``$ArtifactRoot``",
    "- Renderer version: ``$($autoCad.rendererVersion)``",
    "- Visual status: **$($report.visualStatus)**",
    "- Blocker: $($autoCad.blocker)",
    '',
    '## Candidates',
    ''
)
foreach ($candidate in $summary.Candidates) {
    $markdown += "- $($candidate.Orientation) / $($candidate.Candidate): DWG SHA-256 ``$($candidate.DwgSha256)``; baseline start error=$($candidate.Metrics.BaselineStartError); baseline end error=$($candidate.Metrics.BaselineEndError); height error=$($candidate.Metrics.HeightError); angle error=$($candidate.Metrics.AngleErrorRadians); visual=$($candidate.VisualStatus)"
}
$markdown += @(
    '',
    'ACadSharp save/reopen proves serialized structure only. It is not a renderer, so word-mask IoU and character-order visual metrics remain null until a real AutoCAD render is available.'
)
$markdown -join [Environment]::NewLine | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'report.md')

$report | ConvertTo-Json -Depth 12
