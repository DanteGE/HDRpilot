@echo off
rem HDRpilot Showcase Hero v4 - Prototyp starten (nur lesend, nur 127.0.0.1)
rem Optional vorher: powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make-data.ps1"
start "HDRpilot-Prototyp - Strg+C beendet den Server" powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0server.ps1"
timeout /t 2 /nobreak >nul
start "" "http://127.0.0.1:8765/"
