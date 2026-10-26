# Startwerte für Test-Ablagen der Prüfskripte (per Dot-Sourcing geladen).
# Die Skripte prüfen Nachlaufzeit, Weg A und Weg B mit kurzen Stellvertreter-Sitzungen. Deshalb ohne
#   - Neustart-Puffer (kurze Sitzungen würden HDR sonst 1 min anlassen, Standard seit E85; phase4-ui.ps1 stellt ihn
#     über die Einstellungen auf 0:45),
#   - Frühwarnung aus den echten Launcher-Protokollen (würde auf Spielstarts des Nutzers reagieren),
#   - Abgleich der HDR-Liste mit PCGamingWiki (kein Netz in Prüfläufen).
# Spätere Importe und "games add" übernehmen diese Einstellungen unverändert.
# Sprache (E95): fest Deutsch, weil die Prüfskripte deutsche Namen der Bedienungshilfen suchen; ohne config.json folgte
# HDRpilot sonst der Anzeigesprache von Windows. phase16-english-ui.ps1 legt mit -Language en eine englische Ablage an.
function Initialize-TestConfig([string]$dir, [string]$Language = 'de') {
    New-Item -ItemType Directory -Force $dir | Out-Null
    $seed = '{ "version": 1, "settings": { "language": "' + $Language + '", "restartBufferSeconds": 0, "launcherSignals": false, "hdrListSync": false }, "games": [] }'
    [IO.File]::WriteAllText((Join-Path $dir 'config.json'), $seed, (New-Object Text.UTF8Encoding $false))
}
