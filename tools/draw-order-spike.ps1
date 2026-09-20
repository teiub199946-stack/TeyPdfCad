[CmdletBinding()]
param(
    [ValidateSet('InsertionOrder', 'ExplicitOrder', 'Both')]
    [string] $Mode = 'Both',
    [string] $AutoCadPath,
    [string] $CoreConsolePath,
    [int] $TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'

if ($TimeoutSeconds -lt 30) {
    throw 'TimeoutSeconds must be at least 30 seconds.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot 'tests\TeyPdfCad.Dwg.Tests\TeyPdfCad.Dwg.Tests.csproj'
$artifactRoot = 'C:\Users\Admin\Documents\ChatGPT\TeyConvert\output\text-fidelity-review\spike-draw-order-report'
$summaryPath = Join-Path $artifactRoot 'read-back-summary.json'
$reportJsonPath = Join-Path $artifactRoot 'report.json'
$reportMarkdownPath = Join-Path $artifactRoot 'report.md'

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

$previousMode = $env:TEYPDFCAD_DRAW_ORDER_SPIKE_MODE
$env:TEYPDFCAD_DRAW_ORDER_SPIKE_MODE = $Mode
try {
    & dotnet test $testProject --no-restore --filter 'FullyQualifiedName~DrawOrderSpikeTests' --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Draw-order spike test failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:TEYPDFCAD_DRAW_ORDER_SPIKE_MODE = $previousMode
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

function Invoke-AutoCadRender {
    param(
        [string] $VariantName,
        [string] $InputDwg,
        [string] $CoreConsole,
        [int] $Timeout
    )

    $variantDirectory = Join-Path $artifactRoot "autocad\$VariantName"
    New-Item -ItemType Directory -Force -Path $variantDirectory | Out-Null
    $workingDwg = Join-Path $variantDirectory 'roundtrip.dwg'
    $scriptPath = Join-Path $variantDirectory 'roundtrip-and-render.scr'
    $pngPath = Join-Path $variantDirectory 'render.png'
    $stdoutPath = Join-Path $variantDirectory 'accoreconsole.stdout.log'
    $stderrPath = Join-Path $variantDirectory 'accoreconsole.stderr.log'

    Copy-Item -LiteralPath $InputDwg -Destination $workingDwg -Force
    $script = @(
        '_.FILEDIA',
        '0',
        '_.CMDECHO',
        '1',
        '_.ZOOM',
        '_E',
        '_.PNGOUT',
        (Quote-ProcessArgument $pngPath),
        '_.QSAVE',
        '_.CLOSE',
        '_.OPEN',
        (Quote-ProcessArgument $workingDwg),
        '_.ZOOM',
        '_E',
        '_.PNGOUT',
        (Quote-ProcessArgument $pngPath),
        '_.QSAVE',
        '_.QUIT',
        '_Y'
    )
    [IO.File]::WriteAllLines($scriptPath, $script, [Text.UTF8Encoding]::new($false))

    $configuration = Join-Path (Split-Path -Parent $CoreConsole) 'acad2022.cfg'
    if (-not (Test-Path -LiteralPath $configuration -PathType Leaf)) {
        return [pscustomobject]@{
            status = 'blocked'
            blocker = "AutoCAD configuration was not found beside Core Console: $configuration"
            executable = $CoreConsole
            inputDwg = $InputDwg
            workingDwg = $workingDwg
            script = $scriptPath
            png = $pngPath
            stdout = $stdoutPath
            stderr = $stderrPath
        }
    }

    $arguments = @(
        '/i', (Quote-ProcessArgument $workingDwg),
        '/s', (Quote-ProcessArgument $scriptPath),
        '/l', 'en-US'
    )
    $process = Start-Process -FilePath $CoreConsole -ArgumentList $arguments `
        -WorkingDirectory $variantDirectory `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
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
            status = 'failed'
            blocker = "AutoCAD Core Console exceeded the $Timeout second timeout."
            executable = $CoreConsole
            exitCode = $null
            inputDwg = $InputDwg
            workingDwg = $workingDwg
            script = $scriptPath
            png = $pngPath
            stdout = $stdoutPath
            stderr = $stderrPath
            saveReopenObserved = $false
            renderProduced = $false
        }
    }

    $renderProduced = Test-Path -LiteralPath $pngPath -PathType Leaf
    $saveReopenObserved = Test-Path -LiteralPath $workingDwg -PathType Leaf
    $status = if ($process.ExitCode -eq 0 -and $renderProduced -and $saveReopenObserved) {
        'passed'
    }
    else {
        'failed'
    }

    [pscustomobject]@{
        status = $status
        blocker = if ($status -eq 'failed') {
            'AutoCAD Core Console did not complete save/reopen/render successfully; inspect stdout/stderr.'
        }
        else {
            $null
        }
        executable = $CoreConsole
        exitCode = $process.ExitCode
        inputDwg = $InputDwg
        workingDwg = $workingDwg
        script = $scriptPath
        png = $pngPath
        stdout = $stdoutPath
        stderr = $stderrPath
        saveReopenObserved = $saveReopenObserved
        renderProduced = $renderProduced
    }
}

$coreConsole = Resolve-CoreConsole $CoreConsolePath $AutoCadPath
$autoCadRuns = @()
if ($null -eq $coreConsole) {
    $autoCadRuns += [pscustomobject]@{
        status = 'blocked'
        blocker = 'AutoCAD Core Console was not found. Pass -CoreConsolePath or -AutoCadPath; ACadSharp read-back is not used as a render substitute.'
        executable = $null
    }
}
else {
    foreach ($variant in $variants) {
        $variantName = $variant.Variant.ToLowerInvariant() -replace 'order$', '-order'
        $autoCadRuns += Invoke-AutoCadRender `
            -VariantName $variantName `
            -InputDwg $variant.DwgPath `
            -CoreConsole $coreConsole `
            -Timeout $TimeoutSeconds
    }
}

$autoCadBlocked = @($autoCadRuns | Where-Object { $_.status -eq 'blocked' }).Count -gt 0
$autoCadFailed = @($autoCadRuns | Where-Object { $_.status -eq 'failed' }).Count -gt 0
$autoCadPassed = $autoCadRuns.Count -gt 0 -and @($autoCadRuns | Where-Object { $_.status -eq 'passed' }).Count -eq $autoCadRuns.Count
$status = if ($autoCadBlocked -or $autoCadFailed) { 'draw-order-blocked' } else { $structuralFinding }

$report = [ordered]@{
    status = $status
    structuralFinding = $structuralFinding
    acadSharpPackage = '3.7.1'
    mode = $Mode
    insertionOrderStable = $insertionStable
    explicitSortEntitiesTableObserved = $hasExplicitSort
    visualPassInferred = $false
    autoCad = [ordered]@{
        status = if ($autoCadPassed) { 'passed' } elseif ($autoCadFailed) { 'failed' } else { 'blocked' }
        blocker = if ($autoCadBlocked) {
            ($autoCadRuns | Where-Object { $_.status -eq 'blocked' } | Select-Object -First 1).blocker
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
    "- Structural finding: **$structuralFinding**",
    "- ACadSharp package: **3.7.1**",
    "- Insertion order stable after ACadSharp save/reopen: **$insertionStable**",
    "- Explicit `SortEntitiesTable` observed after reopen: **$hasExplicitSort**",
    "- Visual pass inferred from entity lists: **false**",
    '',
    '## Variants',
    ''
)
foreach ($variant in $variants) {
    $markdown += "- $($variant.Variant): source=[$($variant.SourceOrder -join ', ')]; reopened insertion=[$($variant.ReopenedInsertionOrder -join ', ')]; reopened sort=[$($variant.ReopenedSortOrder -join ', ')]"
}
$markdown += @(
    '',
    '## AutoCAD batch/render',
    '',
    "- Status: **$($report.autoCad.status)**",
    "- Blocker: $($report.autoCad.blocker ?? 'none')",
    '',
    'ACadSharp save/reopen is recorded separately and is not treated as a substitute for AutoCAD rendering.'
)
$markdown -join [Environment]::NewLine | Set-Content -LiteralPath $reportMarkdownPath

if ($autoCadFailed) {
    exit 1
}

Write-Output ($report | ConvertTo-Json -Depth 8)
