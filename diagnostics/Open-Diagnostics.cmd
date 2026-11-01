@echo off
set "GAME=D:\STEAM\steamapps\common\Lethal Company"
if not "%~1"=="" set "GAME=%~1"
"%~dp0Lethal company pack Updater.exe" --diagnostics "%GAME%"
