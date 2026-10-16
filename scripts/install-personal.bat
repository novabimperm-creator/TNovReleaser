@echo off
chcp 65001 >nul
setlocal
:: ======== Установка TNovClient (личная конфигурация, вне сети компании) ========
:: Запускать из распакованного архива TNov-personal.zip.
set "TNovSource=%~dp0client"
set "TNovDest=%USERPROFILE%\TNovClient"
set "StartupFolder=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"

if not exist "%TNovSource%\TNovClient.exe" (
    echo Не найден %TNovSource%\TNovClient.exe — распакуйте архив целиком.
    pause
    exit /b 1
)

echo Установка клиента TNov в %TNovDest% ...
taskkill /im TNovClient.exe /f >nul 2>&1
mkdir "%TNovDest%" 2>nul
xcopy "%TNovSource%\*" "%TNovDest%\" /E /Y /Q >nul
copy /Y "%~dp0TNovConfig.json" "%TNovDest%\TNovConfig.json" >nul

if exist "%TNovDest%\CreateShortcut.vbs" (
    pushd "%TNovDest%"
    cscript //nologo CreateShortcut.vbs /source:"%TNovDest%\TNovClient.exe" /destination:"%StartupFolder%" /name:"TNovClient"
    popd
)

echo Запуск клиента TNov. Плагин для Revit скачается автоматически (закройте Revit).
start "" "%TNovDest%\TNovClient.exe"
endlocal
