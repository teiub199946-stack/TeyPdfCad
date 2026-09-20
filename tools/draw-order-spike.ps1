[CmdletBinding()]
param(
    [ValidateSet('InsertionOrder', 'ExplicitOrder', 'Both')]
    [string] $Mode = 'Both',
    [string] $AutoCadPath,
    [string] $CoreConsolePath,
    [string] $ArtifactRoot,
    [int] $TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'

if ($TimeoutSeconds -lt 30) {
    throw 'TimeoutSeconds must be at least 30 seconds.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot 'tests\TeyPdfCad.Dwg.Tests\TeyPdfCad.Dwg.Tests.csproj'
$defaultArtifactRoot = 'C:\Users\Admin\Documents\ChatGPT\TeyConvert\output\text-fidelity-review\spike-draw-order-report'
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = $env:TEYPDFCAD_DRAW_ORDER_ARTIFACT_ROOT
}
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = $defaultArtifactRoot
}
$artifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$summaryPath = Join-Path $artifactRoot 'read-back-summary.json'
$reportJsonPath = Join-Path $artifactRoot 'report.json'
$reportMarkdownPath = Join-Path $artifactRoot 'report.md'

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

$previousMode = $env:TEYPDFCAD_DRAW_ORDER_SPIKE_MODE
$previousArtifactRoot = $env:TEYPDFCAD_DRAW_ORDER_ARTIFACT_ROOT
$env:TEYPDFCAD_DRAW_ORDER_SPIKE_MODE = $Mode
$env:TEYPDFCAD_DRAW_ORDER_ARTIFACT_ROOT = $artifactRoot
try {
    & dotnet test $testProject --no-restore --filter 'FullyQualifiedName~DrawOrderSpikeTests' --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Draw-order spike test failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:TEYPDFCAD_DRAW_ORDER_SPIKE_MODE = $previousMode
    $env:TEYPDFCAD_DRAW_ORDER_ARTIFACT_ROOT = $previousArtifactRoot
}

if (-not (Test-Path -LiteralPath $summaryPath -PathType Leaf)) {
    throw "The spike test did not create the read-back summary: $summaryPath"
}

$summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
$variants = @($summary.variants)
$insertionStable = $variants.Count -gt 0 -and (@($variants | Where-Object { -not $_.InsertionOrderSurvived }).Count -eq 0)
$hasExplicitSort = $variants.Count -gt 0 -and (@($variants | Where-Object { $_.SortEntitiesTablePresent }).Count -gt 0)
$structuralFinding = if ($insertionStable) { 'insertion-order-stable' } else { 'explicit-sort-required' }

function Get-ExecutableCandidate {
    param([string] $Candidate)

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return $null
    }

    if (Test-Path -LiteralPath $Candidate -PathType Leaf) {
        return (Resolve-Path -LiteralPath $Candidate).Path
    }

    return $null
}

function Resolve-CoreConsole {
    param(
        [string] $ExplicitCoreConsole,
        [string] $ExplicitAutoCad
    )

    $candidate = Get-ExecutableCandidate $ExplicitCoreConsole
    if ($candidate) {
        return $candidate
    }

    $autoCad = Get-ExecutableCandidate $ExplicitAutoCad
    if ($autoCad) {
        $derived = Join-Path (Split-Path -Parent $autoCad) 'accoreconsole.exe'
        $candidate = Get-ExecutableCandidate $derived
        if ($candidate) {
            return $candidate
        }
    }

    $command = Get-Command 'accoreconsole.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return $null
}

function Quote-ProcessArgument {
    param([string] $Value)
    return '"' + $Value.Replace('"', '\"') + '"'
}

function Get-Sha256 {
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-RendererVersion {
    param([string] $Executable)

    if ([string]::IsNullOrWhiteSpace($Executable) -or
        -not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
        return $null
    }

    return (Get-Item -LiteralPath $Executable).VersionInfo.FileVersion
}

function Invoke-CoreConsolePhase {
    param(
        [string] $CoreConsole,
        [string] $ScriptPath,
        [string] $WorkingDirectory,
        [string] $StdoutPath,
        [string] $StderrPath,
        [string] $WorkingDwg,
        [int] $Timeout
    )

    $arguments = @(
        '/i', (Quote-ProcessArgument $WorkingDwg),
        '/s', (Quote-ProcessArgument $ScriptPath),
        '/l', 'en-US'
    )
    $process = Start-Process -FilePath $CoreConsole -ArgumentList $arguments `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $StdoutPath `
        -RedirectStandardError $StderrPath `
        -PassThru

    $deadline = [DateTime]::UtcNow.AddSeconds($Timeout)
    while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
    }

    if (-not $process.HasExited) {
        try { $process.Kill($true) } catch { }
        try { $process.WaitForExit() } catch { }
        return [pscustomobject]@{
            completed = $false
            timedOut = $true
            exitCode = $null
        }
    }

    [pscustomobject]@{
        completed = $process.ExitCode -eq 0
        timedOut = $false
        exitCode = $process.ExitCode
    }
}

function Invoke-AutoCadRender {
    param(
        [string] $VariantName,
        [string] $InputDwg,
        [string] $CoreConsole,
        [string] $RendererVersion,
        [int] $Timeout
    )

    $variantDirectory = Join-Path $artifactRoot "autocad\$VariantName"
    New-Item -ItemType Directory -Force -Path $variantDirectory | Out-Null
    $workingDwg = Join-Path $variantDirectory 'roundtrip.dwg'
    $saveScriptPath = Join-Path $variantDirectory 'save-and-exit.scr'
    $renderScriptPath = Join-Path $variantDirectory 'reopen-and-render.scr'
    $pngPath = Join-Path $variantDirectory 'render.png'
    $saveStdoutPath = Join-Path $variantDirectory 'save.stdout.log'
    $saveStderrPath = Join-Path $variantDirectory 'save.stderr.log'
    $renderStdoutPath = Join-Path $variantDirectory 'render.stdout.log'
    $renderStderrPath = Join-Path $variantDirectory 'render.stderr.log'

    Copy-Item -LiteralPath $InputDwg -Destination $workingDwg -Force
    $saveScript = @(
        '_.FILEDIA',
        '0',
        '_.CMDECHO',
        '1',
        '_.QSAVE',
        '_.QUIT',
        '_Y'
    )
    $renderScript = @(
        '_.FILEDIA',
        '0',
        '_.CMDECHO',
        '1',
        '_.ZOOM',
        '_E',
        '_.PNGOUT',
        (Quote-ProcessArgument $pngPath),
        '_.QSAVE',
        '_.QUIT',
        '_Y'
    )
    [IO.File]::WriteAllLines($saveScriptPath, $saveScript, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllLines($renderScriptPath, $renderScript, [Text.UTF8Encoding]::new($false))

    $configuration = Join-Path (Split-Path -Parent $CoreConsole) 'acad2022.cfg'
    if (-not (Test-Path -LiteralPath $configuration -PathType Leaf)) {
        return [pscustomobject]@{
            status = 'blocked'
            blocker = "AutoCAD configuration was not found beside Core Console: $configuration"
            executable = $CoreConsole
            rendererVersion = $RendererVersion
            inputDwg = $InputDwg
            inputDwgSha256 = Get-Sha256 $InputDwg
            workingDwg = $workingDwg
            workingDwgSha256 = $null
            saveScript = $saveScriptPath
            renderScript = $renderScriptPath
            png = $pngPath
            saveStdout = $saveStdoutPath
            saveStderr = $saveStderrPath
            renderStdout = $renderStdoutPath
            renderStderr = $renderStderrPath
            saveReopenObserved = $false
            renderArtifactProduced = $false
        }
    }

    $savePhase = Invoke-CoreConsolePhase $CoreConsole $saveScriptPath $variantDirectory `
        $saveStdoutPath $saveStderrPath $workingDwg $Timeout
    if (-not $savePhase.completed) {
        return [pscustomobject]@{
            status = 'failed'
            blocker = if ($savePhase.timedOut) {
                "AutoCAD Core Console save phase exceeded the $Timeout second timeout."
            }
            else {
                'AutoCAD Core Console save phase failed; inspect save.stdout.log/save.stderr.log.'
            }
            executable = $CoreConsole
            rendererVersion = $RendererVersion
            exitCode = $savePhase.exitCode
            inputDwg = $InputDwg
            inputDwgSha256 = Get-Sha256 $InputDwg
            workingDwg = $workingDwg
            workingDwgSha256 = Get-Sha256 $workingDwg
            saveScript = $saveScriptPath
            renderScript = $renderScriptPath
            png = $pngPath
            saveStdout = $saveStdoutPath
            saveStderr = $saveStderrPath
            renderStdout = $renderStdoutPath
            renderStderr = $renderStderrPath
            saveReopenObserved = $false
            renderArtifactProduced = $false
        }
    }

    $renderPhase = Invoke-CoreConsolePhase $CoreConsole $renderScriptPath $variantDirectory `
        $renderStdoutPath $renderStderrPath $workingDwg $Timeout
    $renderArtifactProduced = Test-Path -LiteralPath $pngPath -PathType Leaf
    $saveReopenObserved = $renderPhase.completed
    $status = if ($renderPhase.completed -and $renderArtifactProduced) {
        'completed'
    }
    else {
        'failed'
    }

    [pscustomobject]@{
        status = $status
        blocker = if ($status -eq 'failed') {
            if ($renderPhase.timedOut) {
                "AutoCAD Core Console reopen/render phase exceeded the $Timeout second timeout."
            }
            else {
                'AutoCAD Core Console reopen/render phase failed; inspect render.stdout.log/render.stderr.log.'
            }
        }
        else {
            $null
        }
        executable = $CoreConsole
        rendererVersion = $RendererVersion
        exitCode = $renderPhase.exitCode
        inputDwg = $InputDwg
        inputDwgSha256 = Get-Sha256 $InputDwg
        workingDwg = $workingDwg
        workingDwgSha256 = Get-Sha256 $workingDwg
        saveScript = $saveScriptPath
        renderScript = $renderScriptPath
        png = $pngPath
        saveStdout = $saveStdoutPath
        saveStderr = $saveStderrPath
        renderStdout = $renderStdoutPath
        renderStderr = $renderStderrPath
        saveReopenObserved = $saveReopenObserved
        renderArtifactProduced = $renderArtifactProduced
    }
}

$coreConsole = Resolve-CoreConsole $CoreConsolePath $AutoCadPath
$rendererVersion = Get-RendererVersion $coreConsole
$autoCadRuns = @()
if ($null -eq $coreConsole) {
    $autoCadRuns += [pscustomobject]@{
        status = 'blocked'
        blocker = 'AutoCAD Core Console was not found. Pass -CoreConsolePath or -AutoCadPath; ACadSharp read-back is not used as a render substitute.'
        executable = $null
        rendererVersion = $null
    }
}
else {
    foreach ($variant in $variants) {
        $variantName = $variant.Variant.ToLowerInvariant() -replace 'order$', '-order'
        $autoCadRuns += Invoke-AutoCadRender `
            -VariantName $variantName `
            -InputDwg $variant.DwgPath `
            -CoreConsole $coreConsole `
            -RendererVersion $rendererVersion `
            -Timeout $TimeoutSeconds
    }
}

$autoCadBlocked = @($autoCadRuns | Where-Object { $_.status -eq 'blocked' }).Count -gt 0
$autoCadFailed = @($autoCadRuns | Where-Object { $_.status -eq 'failed' }).Count -gt 0
$status = if ($autoCadBlocked -or $autoCadFailed) { 'draw-order-blocked' } else { $structuralFinding }

$report = [ordered]@{
    status = $status
    statusScope = 'structural-only'
    structuralFinding = $structuralFinding
    structuralOnly = $true
    provisionalUntilAutoCadRenderPass = $true
    visualStatus = 'pending'
    visualProof = 'not-claimed'
    acadSharpPackage = '3.7.1'
    mode = $Mode
    artifactRoot = $artifactRoot
    insertionOrderStable = $insertionStable
    explicitSortEntitiesTableObserved = $hasExplicitSort
    variants = $variants
    autoCad = [ordered]@{
        status = if ($autoCadBlocked) { 'blocked' } elseif ($autoCadFailed) { 'failed' } else { 'completed' }
        rendererVersion = $rendererVersion
        visualStatus = 'pending'
        visualProof = 'not-claimed'
        blocker = if ($autoCadBlocked) {
            ($autoCadRuns | Where-Object { $_.status -eq 'blocked' } | Select-Object -First 1).blocker
        }
        elseif ($autoCadFailed) {
            ($autoCadRuns | Where-Object { $_.status -eq 'failed' } | Select-Object -First 1).blocker
        }
        else {
            $null
        }
        runs = $autoCadRuns
    }
    readBackSummary = $summaryPath
}

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportJsonPath

$markdown = @(
    '# Spike A: DWG draw order',
    '',
    "- Status: **$($report.status)**",
    "- Scope: **structural only**",
    "- Structural finding: **$structuralFinding** (provisional until an AutoCAD render pass is available)",
    "- ACadSharp package: **3.7.1**",
    "- Insertion order stable after ACadSharp save/reopen: **$insertionStable**",
    "- Explicit `SortEntitiesTable` observed after reopen: **$hasExplicitSort**",
    "- Visual proof: **not claimed**",
    [string]::Format('- Artifact root: `{0}`', $artifactRoot),
    '',
    '> `insertion-order-stable` and the `SortEntitiesTable` result describe serialized DWG structure only. They do not prove AutoCAD display order and remain provisional until AutoCAD save/reopen/render succeeds.',
    '',
    '## Variants',
    ''
)
foreach ($variant in $variants) {
    $markdown += [string]::Format('- {0}: SHA-256={1}; source=[{2}]; reopened insertion=[{3}]; reopened sort=[{4}]',
        $variant.Variant,
        $variant.DwgSha256,
        ($variant.SourceOrder -join ', '),
        ($variant.ReopenedInsertionOrder -join ', '),
        ($variant.ReopenedSortOrder -join ', '))
}
$rendererVersionText = if ($null -eq $report.autoCad.rendererVersion) { 'null' } else { $report.autoCad.rendererVersion }
$blockerText = if ($null -eq $report.autoCad.blocker) { 'none' } else { $report.autoCad.blocker }
$markdown += @(
    '',
    '## AutoCAD batch/render',
    '',
    [string]::Format('- Status: **{0}**', $report.autoCad.status),
    [string]::Format('- Renderer version: {0}', $rendererVersionText),
    [string]::Format('- Blocker: {0}', $blockerText),
    '',
    'Visual status remains pending unless a real AutoCAD Core Console invocation produces the recorded render artifact. Even then, the report records invocation evidence only and does not claim human visual acceptance.',
    '',
    'ACadSharp save/reopen is recorded separately and is not treated as a substitute for AutoCAD rendering.'
)
$markdown -join [Environment]::NewLine | Set-Content -LiteralPath $reportMarkdownPath

if ($autoCadFailed) {
    exit 1
}

Write-Output ($report | ConvertTo-Json -Depth 8)
