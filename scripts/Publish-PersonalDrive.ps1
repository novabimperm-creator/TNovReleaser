# Публикация личной конфигурации TNov на Google Drive.
# Копирует с корпоративной шары то, что качает TNovClient в личной конфигурации
# (GoogleDriveMirror), в локальную папку Google Drive for Desktop — Drive сам выгрузит изменения.
# Запуск после обычного релиза:
#   powershell -ExecutionPolicy Bypass -File Publish-PersonalDrive.ps1 -DriveActual "G:\Мой диск\TNov\actual"
param(
    # Папка «actual» внутри синхронизируемого Google Drive (её ID указывается в UpdateSource.FolderId).
    [Parameter(Mandatory = $true)][string]$DriveActual,
    [string]$ShareRoot = '\\fs-nova\Distr\0.For Admin\_TNov',
    # tnovapi.json для внешних тестировщиков (минимальная версия, блокировки). Не задан — не публикуется.
    [string]$TnovApiJson = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$actual = Join-Path $ShareRoot 'actual'
if (-not (Test-Path $actual)) { throw "Нет папки $actual" }
New-Item -ItemType Directory -Force $DriveActual | Out-Null

# robocopy /MIR: удаляет на Drive то, чего нет на шаре; коды 0–7 — успех.
function Mirror([string]$from, [string]$to, [string[]]$excludeFiles = @()) {
    $rcArgs = @($from, $to, '/MIR', '/R:3', '/W:2', '/NP', '/NJH', '/NJS')
    if ($excludeFiles.Count -gt 0) { $rcArgs += '/XF'; $rcArgs += $excludeFiles }
    & robocopy @rcArgs | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy ${from} -> ${to}: код $LASTEXITCODE" }
    Write-Output "  ${from} -> ${to}"
}

Write-Output 'Публикация на Google Drive:'
Mirror (Join-Path $actual 'addin') (Join-Path $DriveActual 'addin')
foreach ($year in '2022', '2027') {
    $src = Join-Path $actual "$year\TNov"
    if (Test-Path $src) { Mirror $src (Join-Path $DriveActual "$year\TNov") }
}
# TNovConfig.json из комплекта клиента — корпоративный, во внешнюю поставку не идёт.
Mirror (Join-Path $actual 'client') (Join-Path $DriveActual 'client') @('TNovConfig.json', 'install-client.bat')

foreach ($file in @((Join-Path $actual 'clientversion.txt'), (Join-Path $ShareRoot 'releases'))) {
    if (Test-Path $file) {
        Copy-Item $file -Destination $DriveActual -Force
        Write-Output "  $file"
    }
}
if ($TnovApiJson) {
    Copy-Item $TnovApiJson -Destination (Join-Path $DriveActual 'tnovapi.json') -Force
    Write-Output "  $TnovApiJson -> tnovapi.json"
}

Write-Output 'Готово. Дождитесь, пока Google Drive закончит синхронизацию.'
