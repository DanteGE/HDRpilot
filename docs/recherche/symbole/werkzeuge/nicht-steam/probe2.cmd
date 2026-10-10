@echo off
chcp 65001 > nul
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall" /s > "%TEMP%\HDRpilot-Recherche\icons-research\nonsteam-local\outside\hkcu-uninstall.txt" 2>&1
echo done > "%TEMP%\HDRpilot-Recherche\icons-research\nonsteam-local\outside\done.txt"