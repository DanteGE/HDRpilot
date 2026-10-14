param([string]$Name, [string]$Hash = '', [int]$W = 1600, [int]$H = 1000, [int]$Budget = 4000, [switch]$Edge)
# Bildschirmfoto des Prototyps mit headless Chrome/Edge (eigenes Profil unter proto\_shots\prof)
$dir = $PSScriptRoot
$exe = if ($Edge) { 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe' } else { 'C:\Program Files\Google\Chrome\Application\chrome.exe' }
$out = Join-Path (Split-Path $dir -Parent) "shots\$Name.png"
$url = "http://127.0.0.1:8765/#shot=${W}x${H}" +$(if ($Hash) { '&' + $Hash } else { '' })
$args = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1', "--window-size=$W,$H", "--virtual-time-budget=$Budget", "--user-data-dir=$dir\prof", '--no-first-run', '--no-default-browser-check', '--enable-logging=stderr', '--v=0', "--screenshot=$out", $url)
$p = Start-Process -FilePath $exe -ArgumentList $args -NoNewWindow -Wait -PassThru -RedirectStandardError "$dir\last.log"
Select-String -Path "$dir\last.log" -Pattern 'CONSOLE|Uncaught|ERROR:' | Where-Object { $_.Line -notmatch 'gpu|GPU|dbus|sandbox|DEPRECATED|cert|fontations|SharedImage' } | Select-Object -First 12 | ForEach-Object { $_.Line }
"$out  ($((Get-Item $out -ErrorAction SilentlyContinue).Length) B)"
