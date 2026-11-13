@echo off
chcp 65001 >nul
setlocal EnableExtensions

rem ============================================================
rem  DeskSnip - Stop: request the app to exit normally
rem  Pure PowerShell entry; no Git Bash required.
rem  Mirrors: sh manager.sh stop
rem ============================================================

pushd "%~dp0"
if errorlevel 1 exit /b 1

where pwsh >nul 2>&1
if errorlevel 1 goto :TryWindowsPowerShell
set "PS=pwsh"
goto :Run
:TryWindowsPowerShell
where powershell >nul 2>&1
if errorlevel 1 goto :NoPowerShell
set "PS=powershell"

:Run
echo ============================================================
echo  Stop  ^|  scripts\desktop.ps1
echo ============================================================
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\desktop.ps1" stop %*
set "RC=%ERRORLEVEL%"
popd
if not "%RC%"=="0" goto :Fail

echo.
echo [SUCCESS] Stop requested.
exit /b 0

:NoPowerShell
echo [ERROR] PowerShell not found. Install PowerShell 7 or use Windows PowerShell 5.1.
popd
exit /b 1

:Fail
echo.
echo [FAILED] stop failed ^(exit code %RC%^).
if not "%DESK_SNIP_NO_PAUSE%"=="1" pause
exit /b %RC%
