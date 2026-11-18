@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\uninstall-mod.ps1" %*
exit /b %ERRORLEVEL%
