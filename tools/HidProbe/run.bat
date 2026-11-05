@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo.
echo   HidProbe - MouseDock HID battery probe
echo.
HidProbe.exe %*
