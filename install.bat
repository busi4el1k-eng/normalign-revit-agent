@echo off
rem Double-click installer: builds the add-in and installs it for the current user.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
pause
