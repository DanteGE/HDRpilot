param(
  [Parameter(Mandatory = $true)][string]$Name,   # Dateiname ohne .png
  [string]$Hash = '',                            # Hash-Parameter der Seite, z. B. 'w=1745&h=982&theme=hell'
  [int]$W = 1745, [int]$H = 982,                 # Fenstergröße in DIP = Bildgröße (Maßstab 1:1)
  [string]$Page = 'leiste.html',                 # Seite im Ordner des Prototyps
  [int]$Port = 8771,                             # Port des laufenden server.ps1 (nur 127.0.0.1)
  [string]$Out = (Join-Path $env:TEMP 'hdrpilot-leiste-shots'),   # Zielordner (nicht im Repository)
  [int]$Budget = 6000,                           # virtuelle Zeit in ms, bis das Bild entsteht
  [switch]$Edge,
  [switch]$Panel,                                # ganze Seite mit Bedienfeld (ohne shot=1), Browserfenster -VW × -VH
  [int]$VW = 1600, [int]$VH = 1000,
  [int]$RealTime = 0,                            # > 0: echte Zeit in ms statt virtueller Zeit (live laufende Bewegung, requestAnimationFrame)
  [string]$Prof = ''                             # eigenes Chrome-Profil (Standard: $Out\prof)
)
# Bildschirmfoto einer Seite des Prototyps mit headless Chrome/Edge; eigenes Profil unter $Out\prof.
# Ohne -Panel bekommt die Seite shot=1 (nur das Fenster, 1:1) sowie w und h; sie schreibt „MESS {…}“ in die Konsole.
# Beispiel: .\leiste-shot.ps1 -Name breit -W 1745 -H 982 -Hash 'collapsed=1&neu=1' -Out "$env:TEMP\leiste"
$ErrorActionPreference = 'Stop'
$exe = if ($Edge) { 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe' } else { 'C:\Program Files\Google\Chrome\Application\chrome.exe' }
New-Item -ItemType Directory -Force $Out | Out-Null
$prof = if ($Prof) { $Prof } else { Join-Path $Out 'prof' }
$png = Join-Path $Out "$Name.png"
$log = Join-Path $Out "$Name.log"
$url = "http://127.0.0.1:$Port/$Page#" + $(if ($Panel) { "w=$W&h=$H" } else { "shot=1&w=$W&h=$H" }) + $(if ($Hash) { '&' + $Hash } else { '' })
$winW = if ($Panel) { $VW } else { $W }; $winH = if ($Panel) { $VH } else { $H }
$a = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1', "--window-size=$winW,$winH",
  $(if ($RealTime -gt 0) { "--timeout=$RealTime" } else { "--virtual-time-budget=$Budget" }), "--user-data-dir=$prof", '--no-first-run', '--no-default-browser-check',
  '--disable-extensions', '--enable-logging=stderr', '--v=0', "--screenshot=$png", $url)
if (Test-Path $png) { Remove-Item $png -Force }
$p = Start-Process -FilePath $exe -ArgumentList $a -NoNewWindow -Wait -PassThru -RedirectStandardError $log
$mess = Select-String -Path $log -Pattern 'MESS \{.*\}' | Select-Object -Last 1
$errs = Select-String -Path $log -Pattern 'Uncaught|ERROR:.*CONSOLE|console.*error' | Select-Object -First 5 | ForEach-Object { $_.Line }
$size = if (Test-Path $png) { (Get-Item $png).Length } else { 0 }
"$png  ($size B)"
if ($mess) { ($mess.Matches[0].Value) }
if ($errs) { $errs }
