param(
    [string]$RepoPath = "Z:\DEV\TNovReleaser"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-LocalSettingsFile {
    param(
        [string]$TargetPath,
        [string]$SourceDirectory,
        [string[]]$OutputDirectories
    )

    $outputLines = $OutputDirectories |
        ForEach-Object { '            @"{0}",' -f ($_ -replace '"', '""') }
    $outputBlock = ($outputLines -join [Environment]::NewLine).TrimEnd(',')

    @"
namespace TNovReleaser
{
    internal static class LocalSettings
    {
        public const string SourceDirectory = @"$($SourceDirectory -replace '"', '""')";

        public static readonly string[] OutputDirectories =
        {
$outputBlock
        };
    }
}
"@ | Set-Content -Path $TargetPath -Encoding UTF8
}

function Read-SourceDirectoryFromFile {
    param([string]$FilePath)

    if (-not (Test-Path $FilePath)) {
        return $null
    }

    $content = Get-Content -Path $FilePath -Raw

    if ($content -match '<<<<<<<') {
        $localSection = ($content -split '<<<<<<<[^\r\n]*\r?\n', 2)[1]
        $content = ($localSection -split '=======', 2)[0]
    }

    if ($content -match 'SourceDirectory\s*=\s*@"([^"]+)"') {
        return $Matches[1]
    }

    return $null
}

function Sync-FromGitHub {
    param([string]$TargetPath)

    $tempRoot = Join-Path $env:TEMP ("TNovReleaser-sync-" + [guid]::NewGuid().ToString())
    $clonePath = Join-Path $tempRoot "ClassLibrary1"

    try {
        Write-Host "Скачиваю актуальную версию с GitHub..."
        git clone --depth 1 https://github.com/novabimperm-creator/ClassLibrary1.git $clonePath | Out-Host

        Get-ChildItem -Path $clonePath -Force |
            Where-Object { $_.Name -ne '.git' } |
            ForEach-Object {
                $destination = Join-Path $TargetPath $_.Name
                if ($_.PSIsContainer) {
                    if (Test-Path $destination) {
                        Remove-Item $destination -Recurse -Force
                    }
                    Copy-Item $_.FullName $destination -Recurse -Force
                }
                else {
                    Copy-Item $_.FullName $destination -Force
                }
            }
    }
    finally {
        if (Test-Path $tempRoot) {
            Remove-Item $tempRoot -Recurse -Force
        }
    }
}

if (-not (Test-Path $RepoPath)) {
    New-Item -ItemType Directory -Path $RepoPath -Force | Out-Null
}

Push-Location $RepoPath

try {
    $savedSourceDirectory = Read-SourceDirectoryFromFile -FilePath "AppConfig.cs"
    if (-not $savedSourceDirectory) {
        $savedSourceDirectory = Read-SourceDirectoryFromFile -FilePath "LocalSettings.cs"
    }
    if (-not $savedSourceDirectory) {
        $savedSourceDirectory = "Z:\DEV\TNovReleaser"
    }

    $savedOutputDirectories = @(
        "C:\Output\Dir1",
        "C:\Output\Dir2",
        "C:\Output\Dir3"
    )

    $updated = $false

    if (Test-Path ".git") {
        git merge --abort 2>$null | Out-Null
        git rebase --abort 2>$null | Out-Null

        Write-Host "Текущий remote:"
        git remote -v

        $originUrl = (git remote get-url origin 2>$null)
        if (-not $originUrl) {
            git remote add origin https://github.com/novabimperm-creator/ClassLibrary1.git
        }

        git fetch origin
        git checkout -f master 2>$null | Out-Null
        git branch -M master 2>$null | Out-Null
        git reset --hard origin/master 2>$null | Out-Host

        if (Test-Path "TNovReleaser.sln") {
            $updated = $true
            Write-Host "Обновлено через git reset. Коммит: $(git rev-parse --short HEAD)"
        }
    }

    if (-not $updated) {
        Sync-FromGitHub -TargetPath $RepoPath
        Write-Host "Обновлено через скачивание с GitHub."
    }

    Write-LocalSettingsFile -TargetPath "LocalSettings.cs" `
        -SourceDirectory $savedSourceDirectory `
        -OutputDirectories $savedOutputDirectories

    Write-Host ""
    Write-Host "Готово."
    Write-Host "Локальные пути: $savedSourceDirectory"
    Write-Host "Файл LocalSettings.cs создан/обновлён."
    Write-Host "Откройте TNovReleaser.sln и пересоберите проект."
}
finally {
    Pop-Location
}
