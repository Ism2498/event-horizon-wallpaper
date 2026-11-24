@echo off
rem Starts the video wallpaper with the newest event-horizon*.mp4. Stop it from the tray icon.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0video-wallpaper.ps1" %*
