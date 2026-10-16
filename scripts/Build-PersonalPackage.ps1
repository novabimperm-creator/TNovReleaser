# Архив для первой установки TNov вне сети компании (личная конфигурация).
# Внутри: client\ (TNovClient с шары), TNovConfig.json (личный, с источником обновлений
# на Google Drive) и install-personal.bat. Плагин клиент скачает сам при первом запуске.
#   powershell -ExecutionPolicy Bypass -File Build-PersonalPackage.ps1 -FolderId <id папки actual> -ApiKey <ключ> -Output C:\Temp\TNov-personal.zip
param(
    [Parameter(Mandatory = $true)][string]$FolderId,
    [Parameter(Mandatory = $true)][string]$ApiKey,
    [Parameter(Mandatory = $true)][string]$Output,
    [string]$ShareRoot = '\\fs-nova\Distr\0.For Admin\_TNov'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$clientSource = Join-Path $ShareRoot 'actual\client'
if (-not (Test-Path (Join-Path $clientSource 'TNovClient.exe'))) { throw "Нет клиента в $clientSource" }

$staging = Join-Path ([IO.Path]::GetTempPath()) ("TNov-personal-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $staging 'client') | Out-Null
try {
    Get-ChildItem $clientSource -File |
        Where-Object { $_.Name -notin @('TNovConfig.json', 'install-client.bat') } |
        Copy-Item -Destination (Join-Path $staging 'client')

    # ServerPath не пишем: клиент и плагин подставят папку TNovClient текущего пользователя.
    $config = [ordered]@{
        LicenseType  = 'personal'
        CorpName     = ''
        UpdateSource = [ordered]@{ Type = 'GoogleDrive'; FolderId = $FolderId; ApiKey = $ApiKey }
    }
    $config | ConvertTo-Json -Depth 3 | Set-Content -Encoding UTF8 (Join-Path $staging 'TNovConfig.json')

    Copy-Item (Join-Path $PSScriptRoot 'install-personal.bat') $staging

    if (Test-Path $Output) { Remove-Item $Output -Force }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $Output
    Write-Output "Архив: $Output"
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
