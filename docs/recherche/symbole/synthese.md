**Ersatz-Symbole für Spiele in HDRpilot: Messung an deiner Bibliothek und Vorschlag**

Mit Daten, die schon auf deinem PC liegen, bekommen alle 647 Spiele ein Symbol, das wirklich zum Spiel gehört. Internet ist dafür nicht nötig. 618 dieser Symbole sind von Haus aus scharf (mindestens 48 px). 29 sind zwar richtig, liegen aber nur in 32 px (bei Dead Island 2 in 44 px) vor und wirken hochgerechnet etwas unscharf. Eine Online-Quelle bringt bei dir also wenig.

Alle Zahlen stammen aus den vier Teilberichten und einem kleinen Abgleich zwischen ihnen. Der Abgleich hat nur Dateien gelesen und nichts geschrieben.

## 1. Ausgangslage heute

| Quelle | Spiele | eigenes Symbol | kein Symbol | Standardsymbol | davon unscharf (≤ 44 px) |
|---|---|---|---|---|---|
| Steam | 633 | 581 | 18 | 34 | 14 |
| Epic | 6 | 6 | 0 | 0 | 1 (Dead Island 2, 44 px) |
| GOG | 5 | 5 | 0 | 0 | 2 (SWAT 3 und 4, 32 px) |
| EA / Ubisoft | 1 / 1 | 1 / 1 | 0 | 0 | 0 |
| Xbox | 1 | 0 | 1 (Palworld) | 0 | 0 |
| **Summe** | **647** | **594** | **19** | **34** | **17** |

Battle.net und manuell eingetragene Spiele: keine. Alle 647 Spiele sind installiert. Damit haben **53 Spiele kein gutes Symbol**, davon 52 bei Steam und 1 bei Xbox.

**Kein Symbol (19 Spiele):**
- **Keine EXE gefunden (9):** Bei 7 Spielen liegt die EXE tiefer als eine Ordnerebene. Crimson Desert hat mehr Unterordner als die erlaubten 32. Midnight Walkers Playtest hat gar keine EXE.
- **EXE ohne Symbol (5):** 5 Spiele, deren EXE kein Symbol enthält (Namen vor der Veröffentlichung entfernt).
- **Falsche EXE gewählt (4):** 3× `P4PopulateDepot.exe`, ein Perforce-Hilfsprogramm; bei Portal `elementviewer.exe`, ein Entwicklerwerkzeug.
- **Palworld:** Windows verweigert den Zugriff auf die Spiel-EXE, auch außerhalb des Claude-Containers geprüft.

**Standardsymbol statt Spielsymbol (34 Spiele):**
- EasyAntiCheat: 12
- Unreal-„U“: 14 (10 + 4 in zwei Varianten)
- Epic/EOS-Startprogramm: 4
- Godot: 3
- Unity: 1

Die drei Boltgun-Einträge teilen sich zwar ein Symbol, das ist aber ihr echtes Spielsymbol. Sie zählen deshalb nicht als Standardsymbol.

Bei 8 weiteren Spielen sieht die gewählte EXE verdächtig aus, zum Beispiel `deadbolt_map_editor.exe` oder `EDITOR.exe`.

**Wie gemessen:** Die App-DLLs selbst haben `IconExecutableResolver.Resolve` und `ExtractIcon(…, 48)` ausgeführt. Jedes Symbol bekam einen Pixel-Fingerabdruck (SHA-256), und alle 628 gefundenen Symbole wurden zusätzlich von Auge auf Übersichtsbildern geprüft. Bei 48 px kann dabei vereinzelt etwas übersehen worden sein.

## 2. Ersatzkette ohne Internet

**Regel für „gut“:** Ein Symbol ist vorhanden, steht nicht auf der Liste der Standardsymbole und ist in der Datei mindestens 48 px groß. Ist keine Quelle gut, nimmt die App die größte vorhandene.

**Steam:**
1. Spiel-EXE wie heute, mit eigener Ausschlussliste nur für Symbole (siehe Abschnitt 4).
2. `steam\games\<clienticon>.ico`; den Namen liefert `appinfo.vdf`. Vorhanden bei 609 von 633.
3. Deinstallations-Eintrag `HKLM\…\Uninstall\Steam App <id>` → `DisplayIcon`, in der 64-Bit-Ansicht (nicht `WOW6432Node`). Datei vorhanden bei 632.
4. Startmenü-Verknüpfung (`.url`) → `IconFile`. Vorhanden bei 632.
5. `appcache\librarycache\<appid>\<icon>.jpg`. Vorhanden bei 628, immer nur 32 px und ohne Transparenz.

Zusammen hat jedes der 633 Steam-Spiele mindestens eine `.ico`-Datei.

**GOG:** EXE → `goggame-<id>.ico` im Spielordner (5 von 5, 256 px) → Galaxy-Zwischenspeicher `*_glx_square_icon_v2.webp` (5 von 5, 112 px; braucht den WebP-Decoder).

**Xbox/Store:** EXE → Logos aus `MicrosoftGame.config` (Square480x480Logo, StoreLogo, `SmallLogo.ico`; 1 von 1 lesbar) bzw. `AppxManifest`. Die Logo-Dateinamen müssen per Platzhalter gesucht werden: Bei 61 von 77 Store-Paketen existiert die Datei nur mit Zusatz, z. B. `.scale-200`.

**Ubisoft:** EXE → `DisplayIcon` des Eintrags `Uplay Install <id>`, eine 256-px-`.ico` (1 von 1).

**Epic, EA, Battle.net:** Die EXE bleibt die einzige verlässliche Quelle. Die Bild-Zwischenspeicher dieser Launcher sind interne Chromium-Formate; davon rate ich ab.

**Gemessene Wirkung, wenn die EXE zuerst geprüft wird:**
- **52 schlechte Steam-Spiele:** Die Steam-`.ico` ist bei 35 mindestens 48 px groß, bei 25 davon sogar ≥ 256 px. Bei 17 ist sie richtig, aber nur 32 px groß. Für 5 dieser 17 findet eine tiefere EXE-Suche vermutlich ein scharfes EXE-Symbol (Größe nicht gemessen).
- **14 Steam-Spiele mit kleinem EXE-Symbol:** 3 werden durch die `.ico` scharf.
- **Palworld** bekommt das Xbox-Logo, **SWAT 3 und 4** die 256-px-`goggame.ico`. **Dead Island 2** bleibt bei 44 px.
- **Ergebnis:** 647 von 647 Spielen haben ein eigenes Symbol, davon 618 scharf (mit tieferer EXE-Suche bis zu 623).
- **Unsicher:** Die Steam-`.ico` der 34 Standardsymbol-Spiele habe ich nicht angesehen. Es ist möglich, dass ein Entwickler auch dort das Unreal-„U“ hochgeladen hat.

**Bessere EXE-Wahl (gemessen an den 53 schlechten Spielen):** Eine Suche bis 3 Ordnerebenen tief findet für 30 eine andere EXE. 24 davon sind richtig, vor allem die Unreal-Spiele über `Binaries\Win64\*-Shipping.exe`. 6 sind falsch (`java.exe`, `vconsole2.exe`, `qc_eyes.exe`, 3× das Unreal-3-Standardsymbol). Die Suche dauert im Mittel 66 ms pro Spiel, im schlimmsten Fall 12 s.

**Zeitbedarf:**
- Alle 633 `.ico`-Dateien auslesen: 69 ms zusammen.
- `appinfo.vdf` lesen und auswerten: 4 ms plus 64 ms.
- 628 librarycache-Bilder dekodieren: 352 ms zusammen.
- Zum Vergleich die heutige EXE-Suche bei kaltem Datenträger: 12,8 s für alle Spiele zusammen.

## 3. Optional online

**Empfehlung:** ein einziger Schalter, standardmäßig aus: „Ersatz-Cover von PCGamingWiki laden“. Er greift nur bei Spielen, die nach der lokalen Kette kein oder nur ein unscharfes Symbol haben. Bei dir betrifft das 0 Spiele ohne Symbol und etwa 29 unscharfe.

- **Bildart:** Ein Cover ist ein Hochformat (z. B. 1200×1600) und muss für 48 px zugeschnitten werden. Ob das besser aussieht als ein 32-px-Symbol, ist offen.
- **Was gesendet wird:**
  - Im anonymen Modus werden die Spielnamen ohnehin schon gesendet. Das Cover steht bereits im abgerufenen Wiki-Text (`|cover = …`), es kommt also nur eine Bildanfrage pro Spiel dazu (64-px-Vorschau, etwa 5 KB).
  - Im Konto-Modus würden die Titel der betroffenen Spiele erstmals an PCGamingWiki gehen. Das muss im Zustimmungstext stehen.
- **Server:** Die Bilder kommen von `thumbnails.pcgamingwiki.com` bzw. `images.pcgamingwiki.com`, nicht von `www`.
- **Lizenz:** Der Wiki-Text steht unter CC BY-NC-SA 3.0. Die Cover sind aber Herstellergrafik ohne Lizenz. Eine kleine Vorschau nur lokal anzuzeigen ist ein geringes Risiko, aber keine Erlaubnis (keine Rechtsberatung). Dazu gehört ein Hinweis wie „Cover: PCGamingWiki / © Hersteller“.
- **Regeln der Wiki:** höchstens 60 Anfragen pro Minute, eigene Kennung mit Kontaktangabe, bei Fehler 429 eine Minute Sperre.
- **Nicht empfohlen:**
  - SteamGridDB: eigenes Konto und Schlüssel nötig, Bilder von Nutzern mit unklarem Urheberrecht.
  - IGDB: Twitch-Konto und Geheimschlüssel pro Nutzer nötig.
  - Steam-, GOG- und Epic-Server: wären neue Server, über die du entscheiden müsstest. Bei dir bringen sie kaum etwas: Das Steam-Symbol im Netz ist dasselbe wie die lokale Datei, GOG ist lokal schon vorhanden, Epic liefert nur Hochformat-Cover.

## 4. Entwurf für HDRpilot

**Scanner liefern Symbol-Hinweise:**
- `GameEntry` (`GameModels.cs`) bekommt ein neues Feld `IconCandidates`: eine geordnete Liste von Dateipfaden.
- `LibraryMerger.cs` übernimmt es in `GameConfig`, genauso wie heute `KnownExecutables` (Zeilen 34 und 56). `ConfigStore.cs` (Zeile 108) bekommt die übliche Absicherung gegen leere Werte.
- **Steam:** `SteamAppInfoReader` liefert zusätzlich `common/clienticon` und `common/icon` (heute nur Typ und Name). `SteamScanner` erzeugt daraus die `.ico`-, Registry- und Bildpfade.
- **GOG, Xbox, Ubisoft:** `GogGameInfo` liefert die `goggame-<id>.ico`. `MicrosoftGameConfig`/`AppxManifest` liefern die Logos samt Platzhaltersuche. Der Ubisoft-Scanner liefert den `DisplayIcon`-Pfad.

**`GameIconService.Load`:**
- Geht die Kandidaten der Reihe nach durch: zuerst EXE, dann die Hinweise.
- `.exe`/`.ico` laufen über das vorhandene `Icon.ExtractIcon`; `.png`/`.jpg`/`.webp` über den WPF-Bilddecoder mit Zielbreite 48.
- Prüft jeden Kandidaten mit der Regel aus Abschnitt 2. Für die echte Größe wird bei `.ico` der Dateikopf gelesen, bei EXEs die Symbolgruppe in der Datei.
- Der Zwischenspeicher wird nach Spiel geführt statt nach EXE.
- Dekodierfehler werden abgefangen, weil WebP-, HEIF- und AV1-Decoder auf anderen PCs fehlen können.

**`IconExecutableResolver`:**
- Eine eigene Ausschlussliste nur für Symbole: `start_protected_game.exe`, `*EAC*.exe`, `EOSBootstrapper.exe`, `LaunchGame.exe`, `GodotWorkshopUtility.exe`, `P4PopulateDepot.exe`, `elementviewer.exe`, `*editor*.exe`.
- Diese Namen gehören nicht in `HelperExecutables`. `start_protected_game.exe` dient dort absichtlich als früher Start-Hinweis für HDR.
- Unreal-Muster `<Projekt>\Binaries\Win64\*-Shipping.exe` bevorzugen.
- Die tiefere Suche nur als Ersatz, im Hintergrund und mit Zeit- und Mengenbegrenzung (wegen des 12-s-Falls).

**Erkennung von Standardsymbolen:**
- Eine feste Liste der 8 gemessenen Fingerabdrücke (siehe `baseline\generic-refs.txt`), ergänzt um das Unreal-3-Symbol und das Windows-Programmsymbol.
- Zusätzlich ein Ähnlichkeitsvergleich, falls Windows Symbole leicht anders zeichnet. Messung: Das nächstliegende echte Spielsymbol hat Abstand 29, die Grenze für „Standardsymbol“ läge bei 8.
- Die Regel „mindestens 3 Spiele haben dasselbe Symbol“ taugt allein nicht. Sie hätte Boltgun fälschlich erwischt.

**Zwischenspeicher:**
- Bilder bleiben wie heute im Speicher und werden nur für sichtbare Zeilen geladen. Das passt zu deinem Wunsch nach fast keiner Hintergrundlast.
- Neu wäre eine kleine Datei in `%LOCALAPPDATA%\HDRpilot`, die sich je Spiel die gewählte Quelle samt Größe und Änderungszeit merkt. Damit entfällt die langsame Ordnersuche beim Kaltstart.
- Nur für Online-Bilder: eine PNG-Datei mit etwa 96 px pro Spiel. Fehlversuche werden 14 Tage gemerkt, wie bei der bestehenden HDR-Prüfung (`RecheckAfter`).

**Einstellung:**
- Neue Einstellung `OnlineIconSource` mit „Aus“ (Standard) oder „PCGamingWiki“, mit Zustimmungstext.
- Im `PcgwClient`: das Feld `cover` auslesen (Muster in `PcgwWikitext`, neues Feld `PcgwPage.Cover`), es in `HdrSupportEntry` speichern und eine Methode `GetImage` ergänzen. Diese erlaubt nur `*.pcgamingwiki.com`, nur Bilddateien und höchstens 512 KB.

**Aufwand (grobe Schätzung, unsicher):**
- Lokale Kette für Steam, GOG, Ubisoft und Xbox mit Qualitätsprüfung und Tests: 2–3 Tage.
- Bessere EXE-Wahl: 0,5–1 Tag.
- PCGamingWiki-Schalter: etwa 1 Tag.

**Risiken:**
- Steam ändert seine Dateiformate. Das librarycache-Format hat sich schon einmal geändert; der vorhandene Leser kann `appinfo` in den Versionen 27–29.
- Bei 21 Spielen hat sich der Symbolname in Steam geändert. Die alte `.ico` ist noch da, das kann sich aber ändern.
- Ob die PNG-Bilder in den GOG-`.ico`-Dateien mit `System.Drawing` unter .NET 10 richtig dargestellt werden, ist nicht getestet.
- Die Liste der Standardsymbole muss gepflegt werden.
- Die tiefere EXE-Suche kann im Einzelfall langsam sein.

## 5. Offene Fragen an dich

1. **Reihenfolge bei Steam:** Wie gewünscht zuerst das EXE-Symbol und Steam nur als Ersatz? Oder zuerst das offizielle Steam-Symbol? Das wäre schneller und wählt nie eine falsche EXE, kann aber das Aussehen von bis zu 345 Spielen verändern (nicht verglichen).
2. **Unscharfe 32-px-Symbole:** Sollen sie als ausreichend gelten, oder lieber durch ein zugeschnittenes Cover ersetzt werden?
3. **Online überhaupt:** Willst du den PCGamingWiki-Schalter haben, obwohl er bei dir nur etwa 29 unscharfe Symbole betrifft?
4. **PCGamingWiki-Server:** Gilt deine Freigabe für PCGamingWiki auch für `thumbnails.` und `images.pcgamingwiki.com`?
5. **Konto-Modus:** Ist es in Ordnung, dass dann die Titel der betroffenen Spiele an PCGamingWiki gehen?
6. **Weitere Server:** Sollen Steam-, GOG- oder Epic-Server nutzbar sein? Meine Empfehlung ist nein, weil sie bei dir kaum etwas bringen.
7. **Eigenes Symbol:** Möchtest du zusätzlich per Rechtsklick selbst ein Bild oder eine EXE als Symbol wählen können?

Alle Unterlagen liegen unter `<Notizordner>\icons-research\` in den Ordnern `baseline\`, `steam-local\`, `nonsteam-local\` und `online\`. Die meistgenutzten Dateien:
- `baseline\out\results.csv`
- `baseline\out\c-clusters-contact-sheet.png`
- `baseline\generic-refs.txt`
- `steam-local\out\per-game.json`
- `nonsteam-local\contact-sheet.png`