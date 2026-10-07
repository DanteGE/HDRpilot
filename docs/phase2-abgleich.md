# Phase 2: Abgleich der Spielbibliothek mit den Launchern

Stand: 2026-10-07, Zielrechner, `hdrpilot scan` (646 Spiele, 0 Fehler, 0,6 s).
Einzelheiten und Belege stehen in `docs/scanner/<quelle>.md`; die vollständigen Tabellen sind dort vor der
Veröffentlichung durch erfundene Beispielzeilen ersetzt, Namen früher installierter Spiele entfernt (E92).

Die Bibliotheksansicht der Launcher-Oberflächen kann HdrPilot nicht selbst lesen. Abgeglichen wurde deshalb mit den
Daten, aus denen die Launcher ihre Ansicht aufbauen (zweite und dritte Quelle je Launcher). Jede Prüfung wurde von
einem Entwickler-Agenten gemacht und von einem unabhängigen Prüfer-Agenten am echten System wiederholt. Zusätzlich gab
es eine eigene Stichprobe im Hauptzweig: 637 Steam-Manifeste gezählt, genau die 6 bewusst gefilterten fehlen im Scan.
Der Blick in die Launcher-Oberflächen selbst ist Teil von Gate G2.2 und braucht den Nutzer.

| Quelle | Im Scan | Verglichen mit | Ergebnis |
|---|---|---|---|
| Steam | 631 | `libraryfolders.vdf` (Block `apps`, 636 IDs), `HKCU\Software\Valve\Steam\Apps\<id>` (Installed=1), Uninstall `Steam App <id>`, 637 `appmanifest_*.acf`, 1 571 Steam-Verknüpfungen | alle Abweichungen begründet (siehe unten) |
| Epic Games | 7 | `LauncherInstalled.dat` (7), Epic-Verknüpfungen `Desktop\Games\*.url` (7) | identisch |
| GOG Galaxy | 5 | Uninstall `<id>_is1` (6), `goggame-<id>.info`, Startmenü-Verknüpfungen | 1 Abweichung begründet |
| Xbox-App | 1 | `Get-AppxPackage` (1 Spielepaket), `Get-StartApps`, Shell-Namensraum `shell:AppsFolder` | identisch (Name begründet) |
| Ubisoft Connect | 1 | Uninstall `Uplay Install <id>`, Produkt-Cache, Ubisoft-Verknüpfungen | Abweichungen begründet |
| EA app | 1 | Uninstall (Publisher Electronic Arts), `HKLM\SOFTWARE\EA Games`, Laufwerkssuche nach `installerdata.xml` | Abweichungen begründet |
| Battle.net | 0 | `product.db`, `aggregate.json`, `Battle.net.config`, Uninstall | identisch: kein Blizzard-Spiel installiert |

## Abweichungen mit Begründung

### Steam
- **6 Einträge bewusst nicht im Scan:** 228980 Steamworks Common Redistributables, 250820 SteamVR (Typ Tool),
  431960 Wallpaper Engine, 993090 Lossless Scaling, 1366800 Crosshair X, 3477300 Scope X (Typ Application).
  Sie laufen dauerhaft oder neben Spielen; als „Spiel“ würden sie HDR ständig anhalten.
- **4338410 Warhammer 40,000: Boltgun 2 Demo nur im Scan:** am selben Tag installiert, `libraryfolders.vdf` zuletzt
  einige Stunden vorher geschrieben. Steam aktualisiert den Block `apps` verzögert; der Scanner liest deshalb die Manifeste.
- **173 zusätzliche `Installed=1` in der Registry:** 6 gefilterte Werkzeuge, 68 DLCs, 99 Reste deinstallierter Spiele
  ohne Manifest und ohne Ordner.
- **7 abweichende Namen in der Registry:** die Registry hält ältere Store-Namen; der Scan nimmt den Manifest-Namen,
  der dem aktuellen Steam-Namen entspricht.
- **76 Uninstall-Einträge mit anderem Ordner:** Spiele wurden in eine andere Bibliothek verschoben; der alte Pfad
  existiert nicht mehr, der gescannte schon.
- **104 Ordner in `steamapps\common` ohne Manifest:** Reste deinstallierter Spiele; Steam zählt sie selbst nicht als
  installiert.

### GOG Galaxy
- **1256837418 Cyberpunk 2077: Phantom Liberty fehlt bewusst:** Zusatzinhalt (`dependsOn` = 1423049311, keine EXE,
  gleicher Ordner wie das Hauptspiel).

### Xbox-App
- **Name „Wolkenhof“ statt „Hof“ aus dem Startmenü:** Store und Xbox-App zeigen „Wolkenhof“ (`DefaultDisplayName`).
- **Ordner `C:\XboxGames\Wolkenhof\Content` statt `C:\Program Files\WindowsApps\…`:** der WindowsApps-Pfad ist eine
  Verbindung (Junction) auf genau diesen Ordner; die Spielprozesse laufen unter dem XboxGames-Pfad.

### Ubisoft Connect
- **Name aus dem Produkt-Cache statt „AFOP“:** der Uninstall-Eintrag nennt nur das Kürzel.
- **1081 nicht im Scan:** Steam-Ausgabe von Assassin's Creed Shadows, ohne Installationsordner bei Ubisoft; erscheint
  als Steam-Spiel 3159330.
- **Desktop-Verknüpfung auf ein weiteres Ubisoft-Spiel:** nicht installiert (Name vor der Veröffentlichung entfernt).

### EA app
- **Nur Battlefield 6:** übrige Uninstall-Einträge sind Launcher (EA app, Origin) oder ein Steam-Spiel; Einträge in
  `InstallData`, `Origin Games` und alte Verknüpfungen früherer Spiele (Namen vor der Veröffentlichung entfernt) zeigen
  auf Ordner, die es nicht mehr gibt.

## Bekannte Grenzen (Auswahl, Details in den Einzeldokumenten)
- Steam: ein Spiel mitten in einem Update ohne StateFlag „installiert“ fehlt bis zum Update-Ende im Scan. Seine
  Einstellungen gehen nicht verloren (Eintrag bleibt als „nicht installiert“, siehe Phase 3).
- Epic: Filter für DLC, Engines und unvollständige Installationen sind nur mit nachgebauten Manifesten getestet, weil
  am Zielrechner keine existieren.
- EA: der in der EA app eingestellte Standardordner liegt verschlüsselt vor; EA-Spiele werden über Uninstall-Einträge,
  `HKLM\SOFTWARE\EA Games` und Suchordner gefunden.
- Battle.net: der Start-Befehl `battlenet://<Produktcode>` ist ohne installiertes Blizzard-Spiel ungeprüft.
- Start-Befehle wurden in Phase 2 nicht ausgeführt (keine Spiele starten); das prüft G4.5 gemeinsam mit dem Nutzer.
