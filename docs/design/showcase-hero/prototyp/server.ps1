<#
  server.ps1 – winziger, nur lesender HTTP-Server für den Showcase-Hero-Prototyp
  Windows PowerShell 5.1, ohne Adminrechte (TcpListener statt HttpListener, keine URL-ACL nötig).

  Lauscht ausschließlich auf 127.0.0.1:<Port> (Standard 8765). Nur GET. Schreibt nirgendwohin.
    /                         -> index.html aus dem Ordner dieses Skripts
    /<datei>                  -> Datei aus dem Ordner dieses Skripts (nur bekannte Endungen, kein Verlassen des Ordners)
    /steam/<appid>/<datei>    -> Datei aus  C:\Program Files (x86)\Steam\appcache\librarycache\<appid>\  (rekursive Suche nach dem Dateinamen)
    /ico?path=<pfad>          -> Symbol-/Bilddatei, NUR unterhalb von C:\Program Files (x86)\Steam\ ; alles andere wird abgelehnt (403)
    /file?path=<pfad>         -> Bilddateien von GOG, Ubisoft und Xbox: NUR exakt die Pfade aus data.json.allow (von make-data.ps1
                                 eingetragen, z. B. GOG Galaxy\webcache, Ubisoft Game Launcher\cache\assets, <Spiel>\Resources).
                                 Die Liste wird beim Start und bei jeder Änderung von data.json neu gelesen. Alles andere: 403.
    /art/<datei>.png          -> aus EXE-Dateien gelesene Symbole (liegen im Ordner dieses Skripts, statisch)

  Beenden: Strg+C im Konsolenfenster oder den Prozess beenden. Der Socket wird im finally-Block geschlossen.
  Start:   powershell -NoProfile -ExecutionPolicy Bypass -File server.ps1  [-Port 8765]
#>
param([int]$Port = 8765, [switch]$Quiet)
$ErrorActionPreference = 'Stop'

$Root      = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
$SteamRoot = 'C:\Program Files (x86)\Steam\'
$Cache     = $SteamRoot + 'appcache\librarycache\'
$Types = @{
  '.html' = 'text/html; charset=utf-8'; '.css' = 'text/css; charset=utf-8'; '.js' = 'text/javascript; charset=utf-8'
  '.json' = 'application/json; charset=utf-8'; '.svg' = 'image/svg+xml'; '.png' = 'image/png'; '.jpg' = 'image/jpeg'
  '.jpeg' = 'image/jpeg'; '.webp' = 'image/webp'; '.ico' = 'image/x-icon'; '.woff2' = 'font/woff2'; '.txt' = 'text/plain; charset=utf-8'
}
$StaticExt = @('.html', '.css', '.js', '.json', '.svg', '.png', '.jpg', '.webp', '.ico', '.woff2')
$IcoExt    = @('.ico', '.png', '.jpg', '.jpeg')
$SteamExt  = @('.jpg', '.png')
$FileExt   = @('.png', '.jpg', '.jpeg', '.webp', '.ico')
$Lookup = @{}   # Zwischenspeicher appid/dateiname -> voller Pfad (nur im Speicher)
$Allow = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$AllowStamp = [DateTime]::MinValue
function Load-Allow {   # Allowlist für /file aus data.json (nur lesen)
  $dj = $Root + 'data.json'
  try { $st = [IO.File]::GetLastWriteTimeUtc($dj) } catch { return }
  if ($st -eq $script:AllowStamp) { return }
  try {
    $txt = [IO.File]::ReadAllText($dj, [Text.Encoding]::UTF8)
    $m = [regex]::Match($txt, '"allow":\[(.*?)\]')
    $script:Allow.Clear()
    if ($m.Success) { $arr = ('[' + $m.Groups[1].Value + ']') | ConvertFrom-Json; foreach ($p in $arr) { try { [void]$script:Allow.Add([IO.Path]::GetFullPath([string]$p)) } catch {} } }
    $script:AllowStamp = $st
    Log ("Allowlist /file: {0} Dateien" -f $script:Allow.Count)
  } catch { Log ('Allowlist nicht lesbar: ' + $_.Exception.Message) }
}

function Log([string]$m) { if (-not $Quiet) { Write-Host ('{0:HH:mm:ss} {1}' -f (Get-Date), $m) } }

function Send([Net.Sockets.NetworkStream]$s, [int]$code, [string]$reason, [byte[]]$body, [string]$type, [string]$cache) {
  if (-not $body) { $body = [Text.Encoding]::UTF8.GetBytes("$code $reason"); $type = 'text/plain; charset=utf-8' }
  $h = "HTTP/1.1 $code $reason`r`nContent-Type: $type`r`nContent-Length: $($body.Length)`r`nConnection: close`r`nX-Content-Type-Options: nosniff`r`nCache-Control: $cache`r`n"
  if ($code -eq 405) { $h += "Allow: GET`r`n" }
  $hb = [Text.Encoding]::ASCII.GetBytes($h + "`r`n")
  $s.Write($hb, 0, $hb.Length)
  $s.Write($body, 0, $body.Length)
  $s.Flush()
}

function Under([string]$full, [string]$prefix) { return $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) }

function Serve-File([Net.Sockets.NetworkStream]$s, [string]$full, [string]$cache) {
  $ext = [IO.Path]::GetExtension($full).ToLowerInvariant()
  $type = $Types[$ext]; if (-not $type) { $type = 'application/octet-stream' }
  # FileShare.ReadWrite: Steam darf die Datei weiter benutzen; geöffnet wird nur lesend
  $fs = New-Object IO.FileStream($full, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
  try { $buf = New-Object byte[] $fs.Length; $o = 0; while ($o -lt $buf.Length) { $n = $fs.Read($buf, $o, $buf.Length - $o); if ($n -le 0) { break }; $o += $n } }
  finally { $fs.Dispose() }
  Send $s 200 'OK' $buf $type $cache
  return 200
}

function Handle([Net.Sockets.NetworkStream]$s, [string]$head) {
  $line = ($head -split "`r`n")[0]
  $parts = $line -split ' '
  if ($parts.Count -lt 3) { Send $s 400 'Bad Request' $null $null 'no-store'; return "400 $line" }
  $method = $parts[0]; $target = $parts[1]
  if ($method -ne 'GET') { Send $s 405 'Method Not Allowed' $null $null 'no-store'; return "405 $method $target" }
  $q = ''; $path = $target
  $qi = $target.IndexOf('?'); if ($qi -ge 0) { $path = $target.Substring(0, $qi); $q = $target.Substring($qi + 1) }
  try { $path = [Uri]::UnescapeDataString($path) } catch { Send $s 400 'Bad Request' $null $null 'no-store'; return "400 $target" }
  if ($path.Contains("`0") -or $path.Contains('\') -or $path -match '(^|/)\.\.(/|$)') { Send $s 403 'Forbidden' $null $null 'no-store'; return "403 $target" }

  # /steam/<appid>/<datei>
  if ($path -match '^/steam/(\d{1,10})/([A-Za-z0-9_.-]{1,96})$') {
    $appid = $Matches[1]; $name = $Matches[2]
    if ($SteamExt -notcontains [IO.Path]::GetExtension($name).ToLowerInvariant()) { Send $s 403 'Forbidden' $null $null 'no-store'; return "403 $path" }
    $key = "$appid/$name".ToLowerInvariant()
    if (-not $Lookup.ContainsKey($key)) {
      $dir = $Cache + $appid
      $hit = $null
      if ([IO.Directory]::Exists($dir)) { foreach ($f in [IO.Directory]::EnumerateFiles($dir, $name, [IO.SearchOption]::AllDirectories)) { $hit = $f; break } }
      $Lookup[$key] = $hit
    }
    $full = $Lookup[$key]
    if (-not $full -or -not (Under ([IO.Path]::GetFullPath($full)) $Cache)) { Send $s 404 'Not Found' $null $null 'no-store'; return "404 $path" }
    return "$(Serve-File $s $full 'max-age=86400') $path"
  }

  # /ico?path=<pfad>  – nur unterhalb des Steam-Ordners
  if ($path -eq '/ico') {
    $p = $null
    foreach ($kv in $q -split '&') { if ($kv -like 'path=*') { try { $p = [Uri]::UnescapeDataString($kv.Substring(5).Replace('+', ' ')) } catch { $p = $null } } }
    if (-not $p -or $p -notmatch '^[A-Za-z]:\\' -or $p.Contains("`0") -or $p -match '\\\.\.(\\|$)') { Send $s 403 'Forbidden' $null $null 'no-store'; return "403 ico" }
    try { $full = [IO.Path]::GetFullPath($p) } catch { Send $s 403 'Forbidden' $null $null 'no-store'; return "403 ico" }
    if (-not (Under $full $SteamRoot) -or ($IcoExt -notcontains [IO.Path]::GetExtension($full).ToLowerInvariant())) { Send $s 403 'Forbidden' $null $null 'no-store'; return "403 ico $full" }
    if (-not [IO.File]::Exists($full)) { Send $s 404 'Not Found' $null $null 'no-store'; return "404 ico" }
    return "$(Serve-File $s $full 'max-age=86400') ico"
  }

  # /file?path=<pfad>  – nur exakt erlaubte Dateien aus data.json
  if ($path -eq '/file') {
    Load-Allow
    $p = $null
    foreach ($kv in $q -split '&') { if ($kv -like 'path=*') { try { $p = [Uri]::UnescapeDataString($kv.Substring(5).Replace('+', ' ')) } catch { $p = $null } } }
    if (-not $p -or $p -notmatch '^[A-Za-z]:\\' -or $p.Contains("`0") -or $p -match '\\\.\.(\\|$)') { Send $s 403 'Forbidden' $null $null 'no-store'; return "403 file" }
    try { $full = [IO.Path]::GetFullPath($p) } catch { Send $s 403 'Forbidden' $null $null 'no-store'; return "403 file" }
    if (-not $Allow.Contains($full) -or ($FileExt -notcontains [IO.Path]::GetExtension($full).ToLowerInvariant())) { Send $s 403 'Forbidden' $null $null 'no-store'; return "403 file $full" }
    if (-not [IO.File]::Exists($full)) { Send $s 404 'Not Found' $null $null 'no-store'; return "404 file" }
    return "$(Serve-File $s $full 'max-age=86400') file"
  }

  # statische Dateien des Prototyps
  if ($path -eq '/' -or $path -eq '') { $path = '/index.html' }
  $rel = $path.TrimStart('/').Replace('/', '\')
  try { $full = [IO.Path]::GetFullPath($Root + $rel) } catch { Send $s 400 'Bad Request' $null $null 'no-store'; return "400 $path" }
  if (-not (Under $full $Root) -or ($StaticExt -notcontains [IO.Path]::GetExtension($full).ToLowerInvariant())) { Send $s 403 'Forbidden' $null $null 'no-store'; return "403 $path" }
  if (-not [IO.File]::Exists($full)) { Send $s 404 'Not Found' $null $null 'no-store'; return "404 $path" }
  return "$(Serve-File $s $full 'no-store') $path"
}

Load-Allow
$listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $Port)
$listener.Start()
Write-Host "HDRpilot-Prototyp: http://127.0.0.1:$Port/   (Strg+C beendet, nur lesend)"
$clients = New-Object Collections.ArrayList
try {
  while ($true) {
    $busy = $false
    while ($listener.Pending()) {
      $tc = $listener.AcceptTcpClient(); $tc.NoDelay = $true
      [void]$clients.Add(@{ tc = $tc; buf = (New-Object IO.MemoryStream); t = [DateTime]::UtcNow })
      $busy = $true
    }
    for ($k = $clients.Count - 1; $k -ge 0; $k--) {
      $c = $clients[$k]; $tc = $c.tc; $drop = $false
      try {
        $avail = $tc.Available
        if ($avail -gt 0) {
          $s = $tc.GetStream(); $b = New-Object byte[] $avail; $n = $s.Read($b, 0, $avail); $c.buf.Write($b, 0, $n); $c.t = [DateTime]::UtcNow; $busy = $true
          $txt = [Text.Encoding]::ASCII.GetString($c.buf.ToArray())
          if ($txt.Contains("`r`n`r`n")) {
            $r = Handle $s $txt
            Log $r
            try { $tc.Client.Shutdown([Net.Sockets.SocketShutdown]::Send) } catch {}
            $drop = $true
          } elseif ($c.buf.Length -gt 16384) { Send $s 431 'Request Header Fields Too Large' $null $null 'no-store'; $drop = $true }
        } elseif (($tc.Client.Poll(0, [Net.Sockets.SelectMode]::SelectRead) -and $tc.Available -eq 0) -or (([DateTime]::UtcNow - $c.t).TotalSeconds -gt 20)) {
          $drop = $true   # Gegenseite hat geschlossen oder Verbindung ist untätig (z. B. Vorab-Verbindung des Browsers)
        }
      } catch { $drop = $true; Log ('Fehler: ' + $_.Exception.Message) }
      if ($drop) { try { $tc.Close() } catch {}; $clients.RemoveAt($k) }
    }
    if (-not $busy) { Start-Sleep -Milliseconds $(if ($clients.Count) { 2 } else { 10 }) }
  }
} finally {
  foreach ($c in $clients) { try { $c.tc.Close() } catch {} }
  $listener.Stop()
  Write-Host 'Server beendet.'
}
