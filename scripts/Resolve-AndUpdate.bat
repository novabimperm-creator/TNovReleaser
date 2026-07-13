@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Resolve-AndUpdate.ps1" -RepoPath "Z:\DEV\TNovReleaser"
if errorlevel 1 exit /b 1
echo.
echo Готово. Откройте TNovReleaser.sln и пересоберите проект.
pause
