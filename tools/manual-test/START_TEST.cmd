@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-TeyPdfCadTest.ps1"
set "TEY_EXIT=%ERRORLEVEL%"
if not "%TEY_EXIT%"=="0" if not "%TEY_EXIT%"=="3" pause
exit /b %TEY_EXIT%
