@echo off
reg export "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall" "%TEMP%\HDRpilot-Recherche\icons-research\nonsteam-local\outside\hkcu-uninstall.reg" /y > "%TEMP%\HDRpilot-Recherche\icons-research\nonsteam-local\outside\reg.log" 2>&1
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders" > "%TEMP%\HDRpilot-Recherche\icons-research\nonsteam-local\outside\shellfolders.txt" 2>&1
dir /s /b "%APPDATA%\Microsoft\Windows\Start Menu\Programs" > "%TEMP%\HDRpilot-Recherche\icons-research\nonsteam-local\outside\startmenu-user.txt" 2>&1
dir /s /b /a-d "%LOCALAPPDATA%\EpicGamesLauncher\Saved\webcache_4430\Cache" > "%TEMP%\HDRpilot-Recherche\icons-research\nonsteam-local\outside\epic-cache.txt" 2>&1
dir /s /b /a-d "%LOCALAPPDATA%\Electronic Arts\EA Desktop\CEF\2\EADesktop\BrowserCache\Cache" > "%TEMP%\HDRpilot-Recherche\icons-research\nonsteam-local\outside\ea-cache.txt" 2>&1
dir /b "%LOCALAPPDATA%\EpicGamesLauncher\Saved" > "%TEMP%\HDRpilot-Recherche\icons-research\nonsteam-local\outside\epic-saved.txt" 2>&1
echo done > "%TEMP%\HDRpilot-Recherche\icons-research\nonsteam-local\outside\done.txt"