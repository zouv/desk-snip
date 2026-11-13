@echo off
chcp 65001 >nul
setlocal EnableExtensions

rem ============================================================
rem  DeskSnip - Verify: run scripts/verify.ps1
rem  Pure PowerShell entry; no Git Bash required.
rem  Mirrors: sh manager.sh verify
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
echo  Verify  ^|  scripts\verify.ps1
echo ============================================================
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\verify.ps1" %*
set "RC=%ERRORLEVEL%"
popd
if not "%RC%"=="0" goto :Fail

echo.
echo [SUCCESS] Verify passed.
exit /b 0

:NoPowerShell
echo [ERROR] PowerShell not found. Install PowerShell 7 or use Windows PowerShell 5.1.
popd
exit /b 1

:Fail
echo.
echo [FAILED] verify failed ^(exit code %RC%^).
if not "%DESK_SNIP_NO_PAUSE%"=="1" pause
exit /b %RC%
