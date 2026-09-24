$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms

$packageRoot = $PSScriptRoot
$exePath = Join-Path $packageRoot "TeyPdfCad.Cli.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    [System.Windows.Forms.MessageBox]::Show(
        "Не найден TeyPdfCad.Cli.exe рядом со скриптом.",
        "TeyPdfCad test",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 4
}

$actualExeHash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash
$buildInfoPath = Join-Path $packageRoot "BUILD_INFO.txt"
if (-not (Test-Path -LiteralPath $buildInfoPath)) {
    [System.Windows.Forms.MessageBox]::Show(
        "Пакет неполный: не найден BUILD_INFO.txt. Скачайте и распакуйте ZIP заново целиком.",
        "TeyPdfCad test",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 6
}
$expectedHashLine = Get-Content -LiteralPath $buildInfoPath |
    Where-Object { $_ -like "ExecutableSha256=*" } |
    Select-Object -First 1
if (-not $expectedHashLine) {
    [System.Windows.Forms.MessageBox]::Show(
        "BUILD_INFO.txt не содержит контрольную сумму EXE. Используйте только полный пакет из GitHub Actions.",
        "TeyPdfCad test",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 6
}
$expectedExeHash = $expectedHashLine.Substring("ExecutableSha256=".Length).Trim()
if (-not [string]::Equals($actualExeHash, $expectedExeHash, [System.StringComparison]::OrdinalIgnoreCase)) {
    [System.Windows.Forms.MessageBox]::Show(
        "Проверка целостности пакета не пройдена: TeyPdfCad.Cli.exe не соответствует BUILD_INFO.txt. Скачайте пакет заново.",
        "TeyPdfCad test",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 6
}

$pluginPath = Join-Path $packageRoot "AutoCADPlugin\TeyPdfCad.AutoCAD.dll"
$pluginCorePath = Join-Path $packageRoot "AutoCADPlugin\TeyPdfCad.Core.dll"
$adapterHashLine = Get-Content -LiteralPath $buildInfoPath |
    Where-Object { $_ -like "AutoCADAdapterSha256=*" } |
    Select-Object -First 1
$adapterCoreHashLine = Get-Content -LiteralPath $buildInfoPath |
    Where-Object { $_ -like "AutoCADCoreSha256=*" } |
    Select-Object -First 1
if ((-not (Test-Path -LiteralPath $pluginPath)) -or
    (-not (Test-Path -LiteralPath $pluginCorePath)) -or
    (-not $adapterHashLine) -or
    (-not $adapterCoreHashLine)) {
    [System.Windows.Forms.MessageBox]::Show(
        "Пакет неполный: отсутствует проверяемый AutoCADPlugin. Скачайте ZIP заново целиком.",
        "TeyPdfCad test",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 6
}
$actualAdapterHash = (Get-FileHash -LiteralPath $pluginPath -Algorithm SHA256).Hash
$actualAdapterCoreHash = (Get-FileHash -LiteralPath $pluginCorePath -Algorithm SHA256).Hash
$expectedAdapterHash = $adapterHashLine.Substring("AutoCADAdapterSha256=".Length).Trim()
$expectedAdapterCoreHash = $adapterCoreHashLine.Substring("AutoCADCoreSha256=".Length).Trim()
if ((-not [string]::Equals($actualAdapterHash, $expectedAdapterHash, [System.StringComparison]::OrdinalIgnoreCase)) -or
    (-not [string]::Equals($actualAdapterCoreHash, $expectedAdapterCoreHash, [System.StringComparison]::OrdinalIgnoreCase))) {
    [System.Windows.Forms.MessageBox]::Show(
        "Проверка целостности AutoCADPlugin не пройдена. Скачайте пакет заново и не заменяйте DLL вручную.",
        "TeyPdfCad test",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 6
}

$dialog = New-Object System.Windows.Forms.OpenFileDialog
$dialog.Title = "Выберите PDF для проверки TeyPdfCad"
$dialog.Filter = "PDF (*.pdf)|*.pdf"
$dialog.Multiselect = $false

if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
    exit 0
}

$pdfPath = $dialog.FileName
$baseName = [System.IO.Path]::GetFileNameWithoutExtension($pdfPath)
$stamp = Get-Date -Format "yyyyMMdd-HHmmss-fff"
$resultDir = Join-Path $packageRoot ("Results\" + $baseName + "-" + $stamp)
New-Item -ItemType Directory -Force -Path $resultDir | Out-Null

$dwgPath = Join-Path $resultDir ($baseName + ".dwg")
$probePath = Join-Path $resultDir ($baseName + "_DIAGNOSTIC_PROBE_DO_NOT_USE.dwg")
$reportPath = Join-Path $resultDir ($baseName + ".json")
$logPath = Join-Path $resultDir "run.log"

"Input: $pdfPath" | Set-Content -LiteralPath $logPath -Encoding UTF8
"Started: $(Get-Date -Format o)" | Add-Content -LiteralPath $logPath -Encoding UTF8
"ExecutableSha256: $actualExeHash" | Add-Content -LiteralPath $logPath -Encoding UTF8
"AutoCADAdapterSha256: $actualAdapterHash" | Add-Content -LiteralPath $logPath -Encoding UTF8
"AutoCADCoreSha256: $actualAdapterCoreHash" | Add-Content -LiteralPath $logPath -Encoding UTF8
try {
    $inputHash = (Get-FileHash -LiteralPath $pdfPath -Algorithm SHA256).Hash
    "InputSha256: $inputHash" | Add-Content -LiteralPath $logPath -Encoding UTF8
}
catch {
    "InputSha256: unavailable ($($_.Exception.Message))" | Add-Content -LiteralPath $logPath -Encoding UTF8
}

if (Test-Path -LiteralPath $buildInfoPath) {
    Copy-Item -LiteralPath $buildInfoPath -Destination (Join-Path $resultDir "BUILD_INFO.txt") -Force
    "BuildInfo:" | Add-Content -LiteralPath $logPath -Encoding UTF8
    Get-Content -LiteralPath $buildInfoPath | Add-Content -LiteralPath $logPath -Encoding UTF8
}

& $exePath convert --input $pdfPath --output $dwgPath --report $reportPath --probe-output $probePath *>&1 |
    Tee-Object -FilePath $logPath -Append
$exitCode = $LASTEXITCODE

"ExitCode: $exitCode" | Add-Content -LiteralPath $logPath -Encoding UTF8
"Finished: $(Get-Date -Format o)" | Add-Content -LiteralPath $logPath -Encoding UTF8
if (Test-Path -LiteralPath $probePath) {
    "DiagnosticProbe: $probePath" | Add-Content -LiteralPath $logPath -Encoding UTF8
}

if (($exitCode -eq 0 -or $exitCode -eq 3) -and
    ((-not (Test-Path -LiteralPath $dwgPath))
        -or (-not (Test-Path -LiteralPath $probePath))
        -or (-not (Test-Path -LiteralPath $reportPath)))) {
    "LauncherIntegrityError: CLI returned $exitCode but expected final DWG / diagnostic probe DWG / JSON artifacts are missing." | Add-Content -LiteralPath $logPath -Encoding UTF8
    $exitCode = 5
}

if (Test-Path -LiteralPath $reportPath) {
    Start-Process -FilePath "notepad.exe" -ArgumentList ('"{0}"' -f $reportPath)
}

if (Test-Path -LiteralPath $dwgPath) {
    try {
        Start-Process -FilePath $dwgPath
    }
    catch {
        Start-Process -FilePath "explorer.exe" -ArgumentList @("/select,`"$dwgPath`"")
    }
}

switch ($exitCode) {
    0 {
        $message = "Конвертация завершена. DWG и отчёт уже открываются.\n\nПапка результата:\n$resultDir"
        $icon = [System.Windows.Forms.MessageBoxIcon]::Information
    }
    2 {
        $message = "PDF не содержит пригодной векторной геометрии для текущей версии (например, это скан/растр). DWG не создаётся.\n\nПапка результата:\n$resultDir"
        $icon = [System.Windows.Forms.MessageBoxIcon]::Warning
    }
    3 {
        $message = "DWG создан, но конвертер пометил результат как PARTIAL. Откройте DWG и визуально проверьте его.\n\nПапка результата:\n$resultDir"
        $icon = [System.Windows.Forms.MessageBoxIcon]::Warning
    }
    5 {
        $message = "Пакет обнаружил внутреннюю ошибку: CLI завершился, но ожидаемый DWG или JSON отсутствует. Пришлите всю папку результата.\n\n$resultDir"
        $icon = [System.Windows.Forms.MessageBoxIcon]::Error
    }
    6 {
        $message = "Проверка целостности пакета не пройдена. Скачайте ZIP заново и не заменяйте TeyPdfCad.Cli.exe вручную."
        $icon = [System.Windows.Forms.MessageBoxIcon]::Error
    }
    default {
        $message = "Тест завершился с ошибкой (код $exitCode). Пришлите файл run.log и JSON-отчёт из папки:\n$resultDir"
        $icon = [System.Windows.Forms.MessageBoxIcon]::Error
    }
}

[System.Windows.Forms.MessageBox]::Show(
    $message,
    "TeyPdfCad test",
    [System.Windows.Forms.MessageBoxButtons]::OK,
    $icon) | Out-Null

exit $exitCode
