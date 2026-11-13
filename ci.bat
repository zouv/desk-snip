@echo off
chcp 65001 >nul
setlocal EnableExtensions

rem ============================================================
rem  DeskSnip - one-shot CI: clean -> verify -> package.
rem  Pure cmd; no Git Bash required.
rem ============================================================

pushd "%~dp0"
if errorlevel 1 exit /b 1

set "SAVED_NO_PAUSE=%DESK_SNIP_NO_PAUSE%"
set "DESK_SNIP_NO_PAUSE=1"

call "%~dp0clean.bat"
if errorlevel 1 goto :Fail
call "%~dp0verify.bat"
if errorlevel 1 goto :Fail
call "%~dp0package.bat"
if errorlevel 1 goto :Fail

set "DESK_SNIP_NO_PAUSE=%SAVED_NO_PAUSE%"
popd
echo.
echo ============================================================
echo  [SUCCESS] CI pipeline completed.
echo  Dist: %CD%\dist\
echo ============================================================
if not "%DESK_SNIP_NO_PAUSE%"=="1" pause
exit /b 0

:Fail
set "DESK_SNIP_NO_PAUSE=%SAVED_NO_PAUSE%"
popd
echo.
echo [FAILED] CI pipeline aborted.
if not "%DESK_SNIP_NO_PAUSE%"=="1" pause
exit /b 1
