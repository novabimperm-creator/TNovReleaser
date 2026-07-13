param(
    [string]$RepoPath = (Split-Path -Parent $MyInvocation.MyCommand.Path | Split-Path -Parent)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-ConflictValue {
    param(
        [string]$FilePath,
        [string]$Pattern
    )

    if (-not (Test-Path $FilePath)) {
        return $null
    }

    $content = Get-Content -Path $FilePath -Raw
    if ($content -notmatch '<<<<<<<') {
        return $null
    }

    $localSection = ($content -split '<<<<<<<[^\r\n]*\r?\n', 2)[1]
    $localSection = ($localSection -split '=======', 2)[0]

    if ($localSection -match $Pattern) {
        return $Matches[1]
    }

    return $null
}

function Save-LocalSettings {
    param(
        [string]$TargetPath,
        [string]$SourceDirectory,
        [string[]]$OutputDirectories
    )

    $escapedSource = $SourceDirectory.Replace('"', '""')
    $outputLines = $OutputDirectories |
        ForEach-Object { '            @"{0}",' -f $_.Replace('"', '""') }
    $outputBlock = ($outputLines -join [Environment]::NewLine).TrimEnd(',')

    @"
namespace TNovReleaser
{
    internal static class LocalSettings
    {
        public const string SourceDirectory = @"$escapedSource";

        public static readonly string[] OutputDirectories =
        {
$outputBlock
        };
    }
}
"@ | Set-Content -Path $TargetPath -Encoding UTF8
}

Push-Location $RepoPath

try {
    if (-not (Test-Path ".git")) {
        throw "Каталог не является git-репозиторием: $RepoPath"
    }

    $savedSourceDirectory = $null
    $savedOutputDirectories = @()

    if (Test-Path "AppConfig.cs") {
        $savedSourceDirectory = Get-ConflictValue -FilePath "AppConfig.cs" -Pattern 'SourceDirectory\s*=\s*@"([^"]+)"'
        if ($null -eq $savedSourceDirectory) {
            $plainConfig = Get-Content -Path "AppConfig.cs" -Raw
            if ($plainConfig -match 'SourceDirectory\s*=\s*@"([^"]+)"') {
                $savedSourceDirectory = $Matches[1]
            }
        }
    }

    if (Test-Path "LocalSettings.cs") {
        $localSettings = Get-Content -Path "LocalSettings.cs" -Raw
        if ($localSettings -match 'SourceDirectory\s*=\s*@"([^"]+)"') {
            $savedSourceDirectory = $Matches[1]
        }

        $savedOutputDirectories = [regex]::Matches($localSettings, '@"([^"]+)"') |
            ForEach-Object { $_.Groups[1].Value } |
            Where-Object { $_ -ne $savedSourceDirectory }
    }

    git merge --abort 2>$null | Out-Null
    git rebase --abort 2>$null | Out-Null

    git fetch origin
    git checkout master | Out-Null
    git reset --hard origin/master

    if (-not (Test-Path "LocalSettings.cs")) {
        Copy-Item "LocalSettings.cs.example" "LocalSettings.cs"
    }

    if ($savedSourceDirectory -or $savedOutputDirectories.Count -gt 0) {
        if (-not $savedSourceDirectory) {
            $savedSourceDirectory = "Z:\DEV\TNovReleaser"
        }

        if ($savedOutputDirectories.Count -eq 0) {
            $savedOutputDirectories = @(
                "C:\Output\Dir1",
                "C:\Output\Dir2",
                "C:\Output\Dir3"
            )
        }

        Save-LocalSettings -TargetPath "LocalSettings.cs" `
            -SourceDirectory $savedSourceDirectory `
            -OutputDirectories $savedOutputDirectories
    }

    Write-Host "Локальный репозиторий обновлён до origin/master."
    Write-Host "Локальные пути сохранены в LocalSettings.cs"
}
finally {
    Pop-Location
}
