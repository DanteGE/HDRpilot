# Entscheidungen

Getroffene Entscheidungen mit Begründung. Neueste zuletzt. „(gemessen)“ heißt: am Zielrechner geprüft.

## Rahmen

**E1 Projektordner `%USERPROFILE%\Projekte\HdrPilot`.** Vom Nutzer gewählt; nicht OneDrive-synchronisiert.

**E2 Werkzeuge installiert:** .NET SDK 10.0.401 und Git per winget, wingdi.h aus dem NuGet-Paket
`Microsoft.Windows.SDK.CPP` (nur entpackt, nicht installiert). Alle drei fehlten; der Nutzer hat jeweils zugestimmt.

**E3 Git-Identität nur lokal im Repository** (`user.name` = Dante_GE, `user.email` = Konto-Adresse), weil keine globale
Git-Konfiguration existierte. Keine globale Einstellung verändert.

**E4 Zwei EXE-Namen in getrennten Ordnern.** Die Kommandozeile heißt wie im Auftrag `hdrpilot.exe`, die App
`HdrPilot.exe`. Windows unterscheidet Groß-/Kleinschreibung nicht; beide dürfen deshalb nie im selben Ordner liegen.
Build-Ausgaben liegen ohnehin getrennt; bei der Veröffentlichung kommt die Kommandozeile in einen Unterordner `cli\`.

**E5 Tests mit xUnit v3 (Apache-2.0)** statt der Vorlage xUnit 2: v3 kann Tests zur Laufzeit überspringen
(`Assert.SkipWhen`). Das braucht der Header-Abgleich, wenn wingdi.h auf einem anderen Rechner fehlt. Das Gate
`test.ps1` wertet Übersprungenes als Fehler, damit hier nie unbemerkt übersprungen wird.

**E6 Gate-Skripte in PowerShell** statt Node: Node ist nicht installiert, PowerShell 5.1 ist auf jedem Windows vorhanden.

**E7 Protokollierung über `Microsoft.Extensions.Logging.Abstractions` (MIT).** Einzige Laufzeit-Abhängigkeit des Kerns.
Die Dateiausgabe (rollierend) wird selbst geschrieben, kein Serilog/NLog nötig.

**E8 Kommandozeilentexte stehen im Code, nicht in Ressourcen.** Die CLI ist ein Diagnosewerkzeug; die Vorgabe
„Texte in Ressourcendateien“ gilt für die Oberfläche (Phase 4).

**E9 JSON-Ausgaben der CLI sind ASCII-sicher** (Standard-Escaping von System.Text.Json, etwa `\u0026`). Grund: Die
Konsole nutzt Codepage 850; Sonderzeichen in Spielnamen (™, Japanisch) kämen sonst als `?` an.

## HDR-Steuerung

**E10 P/Invoke mit blittierbaren `unsafe`-Strukturen und `LibraryImport`.** Struktur- und Feldnamen sind exakt die aus
wingdi.h. Dadurch kann `WingdiHeaderConformanceTests` jede Struktur automatisch gegen den Header prüfen (Reihenfolge,
Offsets, Größen, Union-Mitglieder), dazu alle verwendeten Konstanten und Bitpositionen. Eine Negativkontrolle prüft,
dass vertauschte, fehlende und zu breite Felder erkannt werden. Der Abgleich hat beim ersten Lauf zwei falsch
**von Hand** berechnete Erwartungswerte in `NativeLayoutTests` aufgedeckt (Offsets in `DISPLAYCONFIG_PATH_TARGET_INFO`);
die C#-Strukturen selbst waren korrekt.

**E11 Bedeutung von „HDR an“ auf dem neuen Weg (Typ 15):** gemeldet wird `highDynamicRangeUserEnabled` (Bit 5), also
genau der Schalter in den Einstellungen. Als **bestätigt** gilt ein Schaltvorgang erst, wenn Bit 5 dem Ziel entspricht
und `activeColorMode` dazu passt (HDR = 2 beim Einschalten, ≠ 2 beim Ausschalten). Steht der Schalter richtig, aber
Windows aktiviert den Modus nicht (Richtlinie, Akku), lautet das Ergebnis „fehlgeschlagen“ mit dieser Begründung.

**E12 Deutung des älteren Wegs (Typ 9):** `advancedColorEnabled` ist auch bei Wide Color / automatischer
Farbverwaltung gesetzt (gemessen: `0x7` bei HDR aus und WCG an). HDR gilt deshalb nur als an, wenn
`advancedColorEnabled` gesetzt und `wideColorEnforced` nicht gesetzt ist. HDR-fähig: `advancedColorSupported` und nicht
`advancedColorForceDisabled`. Grenze: Typ 9 kann einen reinen WCG-Bildschirm nicht von einem HDR-Bildschirm
unterscheiden; ein Schaltversuch endet dort nach der Bestätigungsschleife als „fehlgeschlagen“, nie als Erfolg.

**E13 Älterer Weg am Zielrechner geprüft (gemessen):** `hdrpilot hdr on/off --api legacy` schaltet auch unter Build 26200
korrekt, und die automatische Farbverwaltung (`AutoColorManagementEnabled`) bleibt nach dem Ausschalten an.

**E14 Bestätigung:** bis zu 10 Lesevorgänge im Abstand von 200 ms (höchstens 2 s). Gemessen dauert ein vollständiger
Wechsel einschließlich Prozessstart 422 bis 486 ms. Jeder Lesevorgang ermittelt Pfade und Ziel-IDs neu und sucht den
Bildschirm über den monitorDevicePath, falls der Treiber beim Umschalten die Ziel-ID ändert (per Test abgesichert).

**E15 Hauptbildschirm** = Quelle, deren Quellmodus im virtuellen Desktop bei (0,0) liegt (aus `QueryDisplayConfig`,
ohne zusätzliche API).

**E16 Schaltvorgänge sind serialisiert** (eine Sperre im Controller), damit Tray-Umschalter und Sitzungslogik nicht
gegeneinander schalten.

**E17 Tastenkürzel-Notlösung:** nur wenn per Einstellung an (Standard aus), nur nachdem die API gescheitert ist. Vorher
wird der Zustand erneut gelesen: Ist er schon richtig, wird nichts gesendet. Steht der Schalter richtig, aber der Modus
ist nicht aktiv, wird ebenfalls nichts gesendet, weil Umschalten es verschlimmern würde. Danach wird erneut bestätigt.

**E18 Unabhängige Gegenprobe für „stimmt mit Einstellungen überein“:** Die Einstellungen-App legt den HDR-Schalter als
`HDREnabled` unter `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\MonitorDataStore\<EDID-Schlüssel>` ab
(gemessen: Wert ändert sich sofort mit jedem Wechsel). Die Gates vergleichen den gemeldeten Zustand damit, statt nur mit
derselben API, die auch schaltet.

**E19 Bild-in-Bild-Modus des Bildschirms (gemessen):** Der MAG 341C OLED meldet dann 60 Hz, 8 bit und kein HDR
(`highDynamicRangeSupported` = 0). HdrPilot behandelt das wie einen nicht HDR-fähigen Bildschirm: nichts schalten,
melden, Spiel nicht blockieren.

**E20 Exit-Codes der CLI:** 0 ok (auch „war schon so“), 1 unerwarteter Fehler, 2 Aufruf falsch (auch mehrdeutige
Bildschirmauswahl), 3 Bildschirm nicht gefunden/nicht verbunden, 4 Bildschirm ohne HDR, 5 Schalten fehlgeschlagen.

## Spielbibliothek (Phase 2)

Die vollständigen Entscheidungen je Quelle mit Belegen stehen in `docs/scanner/<quelle>.md` (Abschnitt 4). Hier die
wichtigsten, die das Verhalten der App prägen:

**E21 Parallele Entwicklung in Git-Arbeitskopien.** Vier Gruppen (Steam+Ubisoft, Epic+GOG, Xbox+Manuell, EA+Battle.net)
wurden getrennt gebaut, je von einem unabhängigen Prüfer am echten System gegengeprüft und dann zusammengeführt.

**E22 Scanner werden per Reflexion gefunden** (`ScannerCatalog`, fester Konstruktor-Vertrag). Ein neuer Scanner braucht
keine zentrale Liste. Alle Scanner laufen parallel; ein Absturz wird als Fehler dieses Scanners gemeldet.

**E23 Steam liest Manifeste, nicht den `apps`-Block** von `libraryfolders.vdf`; der hinkt nach (gemessen). Nicht-Spiele
werden über eine feste, pflegbare Liste (228980, 250820, 431960, 993090, 1366800, 3477300) und den App-Typ aus Steams
`appinfo.vdf` gefiltert. Grund: Wallpaper Engine oder Lossless Scaling laufen dauerhaft und hielten sonst HDR an.

**E24 Startwege:** Steam `steam://rungameid/<id>`, Epic `com.epicgames.launcher://…?action=launch&silent=true`,
Xbox `shell:AppsFolder\<PFN>!<AppId>` (App-Id aus `appxmanifest.xml`), Ubisoft `uplay://launch/<id>/0`,
EA `origin2://game/launch/?offerIds=<contentID>&authCode=&cmdParams=` (so ruft das Spiel selbst die EA app auf;
Anmeldung, Updates und Anti-Cheat bleiben bei der EA app), GOG direkt über die EXE (wie gefordert),
Battle.net `battlenet://<Produktcode>` (ungeprüft, kein Spiel installiert). Alle Protokolle sind am System registriert.

**E25 Xbox-Installationsordner ist `<Spiel>\Content`**, nicht der WindowsApps-Pfad: Dieser ist eine Junction dorthin,
und die Prozesse laufen unter dem XboxGames-Pfad. `gamelaunchhelper.exe` bleibt bewusst erkennbar (früher Hinweis auf
den Spielstart, gut für HDR).

**E26 Schutz vor zu weiten Installationsordnern:** Alle Scanner lehnen Laufwerkswurzeln als Installationsordner ab;
zusätzlich ignoriert die Prozesszuordnung Windows-, Programme- und Profilordner (Phase 3), sonst zählte jeder Prozess
dort als Spiel.

**E27 Ausschlussliste für Hilfsprozesse:** 66 Muster, jedes mit realem Beispiel aus einem Inventar aller 2 110 EXE-Dateien
in den Spielordnern dieses Rechners belegt (`docs/scanner/hilfsprozesse.md`). Bewusst keine breiten Muster wie `*crash*`
oder `*launcher*`, weil sie echte Spiele treffen (Crashlands.exe, Buggos.exe). Start-Helfer (gamelaunchhelper,
EAAntiCheat.GameServiceLauncher, PlayRDR2) bleiben erkennbar; reine Launcher-Fenster (REDprelauncher, LariLauncher)
werden ignoriert.

**E28 Battle.net umgesetzt statt weggelassen,** weil Pfad und Aufbau von `product.db` (versteckte Datei, unter 1 KB,
Protobuf) am System verifiziert werden konnten. Eigener minimaler Protobuf-Leser, keine Fremdbibliothek.

**E29 EA-Bibliothek wird nicht geöffnet** (verschlüsselt, hardwaregebunden). Spiele werden an
`__Installer\installerdata.xml` erkannt, über Uninstall-Einträge, `HKLM\SOFTWARE\EA Games` und Suchordner;
DLC-Manifeste unter `__Installer\DLC` zählen nie als Spiel.

## Überwachung und Sitzungslogik (Phase 3)

**E30 Prozessliste über `NtQuerySystemInformation(SystemBasicProcessInformation)`** (winternl.h, ab Windows 11 24H2),
sonst `SystemProcessInformation`, im Notfall Toolhelp. Gemessen mit 313 Prozessen: Toolhelp 5,2 ms je Abfrage,
Basisklasse 0,053 ms (rund 100-mal billiger, keine Thread-Daten). Die Sequenznummer erkennt wiederverwendete PIDs.
Pfade (`QueryFullProcessImageNameW` mit `PROCESS_QUERY_LIMITED_INFORMATION`) nur für neue Prozesse, Handles sofort zu.
Leerlauf-Messung mit 646 Spielen: 0,21 % eines Kerns über 60 s, keine wachsenden Handles.

**E31 Zuordnung nur für neue Prozesse:** Jeder Prozess wird einmal beim ersten Auftauchen zugeordnet (Zwischenspeicher
je PID); nach einer Konfigurationsänderung einmal komplett neu. Verschachtelte Installationsordner: der genaueste gewinnt.
Windows-, Programme- und Profilordner sowie Laufwerkswurzeln gelten nie als Installationsordner.

**E32 Gemerkter Zustand je Bildschirm** (`%LOCALAPPDATA%\HdrPilot\session-state.json`, maschinenbezogen, atomar):
Er wird vor dem Einschalten geschrieben, mit allen Spielen, die den Bildschirm gerade brauchen. Zurückgestellt wird erst,
wenn das letzte Spiel endet. Spielt ein zweites Spiel mit, bleibt der ursprüngliche Zustand (vor dem ersten Spiel)
maßgeblich.

**E33 „Nach dem Spiel zurückstellen“ bei mehreren Spielen:** Zurückgestellt wird nur, wenn alle beteiligten Spiele es
wollen. Hat eines „nein“, bleibt HDR an. Das ist die vorsichtige Lesart von „nach diesem Spiel HDR anlassen“.

**E34 Wiederanlauf:** Beim Start liest HdrPilot den gemerkten Zustand. Läuft ein beteiligtes oder ein anderes
ausgewähltes Spiel für denselben Bildschirm, wird die Sitzung übernommen. Sonst wird zurückgestellt. Ist der Bildschirm
gerade nicht da, bleibt der Zustand gemerkt und wird bei der nächsten Bildschirmänderung oder beim nächsten Start
nachgeholt. Beendet der Nutzer HdrPilot während eines Spiels, bleibt HDR an und der Zustand gemerkt (wie bei einem
Absturz); der nächste Start räumt auf.

**E35 Bildschirmänderung (WM_DISPLAYCHANGE)** holt nur Ausstehendes nach: verschobenes Zurückstellen und Bildschirme, die
beim Spielstart fehlten oder kein HDR konnten (etwa Bild-in-Bild). Ein von Hand während des Spiels umgeschaltetes HDR
wird nicht überschrieben.

**E36 Start über die App (Weg B)** schaltet HDR ein, wartet die Bestätigung ab und startet dann das Spiel, auch bei
pausierter Automatik und auch für Spiele ohne „HDR automatisch“ (ausdrücklicher Wunsch). Bis der erste Spielprozess
erscheint, gilt eine Startfrist (Standard 120 s), danach wird zurückgestellt. Das Protokoll hält Zeitpunkte für
„HDR-Schritt abgeschlossen“, „Start-Befehl ausgeführt“ und „Spielprozess erkannt“ auf die Millisekunde fest.

**E37 Gemerkte Spiel-EXEs** werden beim ersten erkannten Lauf aufgezeichnet (alle EXE-Pfade dieses Laufs) und danach
nicht mehr verändert. Vom Nutzer entfernte EXEs zählen für dieses Spiel nicht mehr, ergänzte zählen auch außerhalb des
Installationsordners. Ist ein Prozesspfad nicht lesbar, wird nur bei eindeutigem Namen zugeordnet.

**E38 Ausgewählte Spiele werden auch zugeordnet, wenn sie beim letzten Einlesen fehlten** (etwa mitten in einem
Steam-Update, siehe Grenze in `docs/scanner/steam.md`). Verschwundene Spiele behalten ihre Einstellungen.

**E39 Pause der Automatik** verhindert neue Sitzungen; laufende enden normal und stellen zurück.

**E40 Kommandozeile für Phase-3-Tests:** `hdrpilot watch` betreibt dieselbe Automatik wie die App (gemeinsamer
`AutomationHost`), `games add/set/list/import` pflegt die Konfiguration, `--config-dir` trennt Testläufe von der echten
Ablage. `hdrpilot diag poll` misst die Kosten eines Abfragezyklus.

## Nachprüfung der Sitzungslogik (unabhängige Prüfer, 2026-10-07)

Drei Prüfer mit getrennten Blickwinkeln (Zustand, Technik, Auftrag) und drei Gegenprüfer. 15 Funde, davon 13 bestätigt
und behoben (je mit Test, der ohne Korrektur fehlschlägt; zwei per Mutation nachgewiesen), 2 widerlegt.

**E41 Erneuter Start über die App** während der Nachlaufzeit oder der Startfrist stellt die Sitzung wieder auf „wartet
auf den Spielprozess“, beginnt die Startfrist neu und schaltet HDR sicher ein (auch wenn es von Hand ausgeschaltet wurde).

**E42 Speicherfehler beim gemerkten Zustand:** einmal nach 250 ms erneut versuchen; scheitert es wieder, wird trotzdem
geschaltet (das Spiel hat Vorrang), aber per Tray-Hinweis gemeldet, dass ein Absturz HDR jetzt nicht zurückstellen würde.

**E43 Neue Sitzungen lösen eine Neuzuordnung aus,** damit auch nicht installierte, nicht ausgewählte Spiele beim Start über
die App erkannt werden. Revision und Stand der Konfiguration werden in dieser Reihenfolge gelesen (kein Wettlauf).

**E44 Gemerkte EXEs über die Nachlaufzeit:** Der „erste erkannte Lauf“ endet wie eine Sitzung erst nach der Nachlaufzeit;
bei Starter-Ketten (PlayRDR2 → RDR2) wird so auch die eigentliche Spiel-EXE gemerkt.

**E45 Zuordnung:** genau bekannte EXE-Pfade zählen immer (auch in sonst zu weiten Ordnern wie dem Desktop); bei gleichem
Installationsordner gewinnt das ausgewählte, dann das installierte Spiel; per Pfad ausgeschlossene EXEs gelten auch bei
unlesbarem Prozesspfad. `games add` weist auf einen schon vergebenen Ordner hin.

**E46 Protokoll übersteht einen im Betrieb gelöschten Ordner** (Neuanlage beim nächsten Dateiwechsel, Schreiber wird
verworfen statt in entsorgtem Zustand zu bleiben); Fehler beim Protokollieren können die Überwachung nicht mehr beenden.

**E47 Konfiguration über Prozessgrenzen:** Vor jedem Schreiben wird `config.json` neu gelesen, wenn ein anderer Prozess
sie geändert hat (Sperre `Local\HdrPilot.Config.<hash>`); die laufende Automatik lädt fremde Änderungen per
FileSystemWatcher nach. Halb geschriebene Dateien werden ignoriert.

**E48 Meldungstexte des Kerns in `Resources/CoreStrings.resx`** (Tray-Hinweise, Schaltergebnisse, Startmeldungen), damit
Englisch später ergänzt werden kann. Protokolltexte bleiben im Code (nicht Teil der Oberfläche).

Widerlegt (kein Fehler): „Zurückstellen“-Einstellung während einer Sitzung ändern wirkt ab dem nächsten Spielstart
(bewusste Lesart); Wettlauf beim Neuaufbau der Zuordnung war nicht erreichbar (trotzdem gehärtet, E43).

## Phase 4: Oberfläche und Tray (2026-10-07)

**E49 Eigenes Testprojekt `HdrPilot.App.Tests`:** Die App heißt `HdrPilot.dll`, die Kommandozeile `hdrpilot.dll`; im
selben Ausgabeordner überschreiben sie sich (Dateisystem ohne Groß-/Kleinschreibung). Kern- und CLI-Tests bleiben in
`HdrPilot.Tests`, App-Tests laufen getrennt; `test.ps1` zählt beide Projekte.

**E50 Einzelinstanz über benannten Mutex plus Nachrichtenfenster:** `Local\HdrPilot.Instance.<id>` entscheidet, ein
unsichtbares Nachrichtenfenster `HdrPilot.Commands.<id>` nimmt weitergereichte Befehle (`--run`, `--open`, `--pause`,
`--exit`) per WM_COPYDATA an. Keine Named Pipe, also keine Netzwerk-Oberfläche und keine Zugriffsrechte-Fragen. Die
`<id>` hängt am Konfigurationsordner, damit Prüfläufe mit `--config-dir` die echte Instanz nicht stören.

**E51 Aussehen:** WPF-Fluent (`ThemeMode="System"`) als Grundlage, darüber eigene Farb-Token (`Tokens.Dark/Light.xaml`),
die der ThemeManager nach der Windows-Einstellung „App-Modus“ tauscht, auch im laufenden Betrieb. Eigene Fensterrahmen
(ChromeWindow) mit abgerundeten Ecken und dunkler Titelleiste über DWM.

**E52 Gamer-Oberfläche mit Ressourcen-Regeln:** Beim Start entsteht kein Fenster, nur das Tray-Symbol. Fenster werden
bei Bedarf gebaut und beim Schließen ganz verworfen (Symbol-Zwischenspeicher geleert, GC und Arbeitssatz-Rückgabe).
Dauer-Animationen laufen nur, solange das Fenster sichtbar, aktiv und nicht minimiert ist.

**E53 Animationen folgen „Animationseffekte“ von Windows** (`SystemParameters.ClientAreaAnimation`) und brauchen
Grafik-Beschleunigung (RenderCapability.Tier > 0); sonst springen Zustände ohne Übergang. Gilt auch für die gemeinsamen
Steuerelemente (Schalter, Akzent-Knopf) über `Motion.Instance`.

**E54 Bildschirmfoto-Modus** `HdrPilot.exe --snapshot <ordner> --config-dir <ordner>` rendert die Fenster unsichtbar
als PNG (für die Durchsicht der Oberfläche ohne Bildschirm-Aufnahme). Kein Teil der normalen Bedienung.

**E55 Speicherfehler der Konfiguration sind sichtbar:** `ConfigService` wirft `ConfigSaveException` und übernimmt die
Änderung nicht; Oberfläche und Tray melden „nicht gespeichert“ statt still einen anderen Zustand anzuzeigen.

**E56 Bibliothek wird bei jedem Start im Hintergrund neu eingelesen** (nach dem Start der Automatik), damit neue Spiele
markiert erscheinen, ohne dass jemand „Neu einlesen“ drückt. Danach wird der Speicher zurückgegeben, wenn kein Fenster
offen ist (das Einlesen liest z. B. Steams rund 15 MB große appinfo.vdf).

**E57 Autostart über `HKCU\…\Run`:** Beim Start wird ein vorhandener, aber auf einen alten Pfad zeigender Eintrag
korrigiert (EXE verschoben), nur wenn Autostart in der Konfiguration an ist. Mit `--config-dir` wird die Registry nie
angefasst (`ConfigOnlyAutostartService`), damit Prüfskripte den echten Autostart nicht verändern.

**E58 Verknüpfungen heißen „<Spiel> (HDR).lnk“** (Desktop bzw. Startmenü\Programme) und rufen
`HdrPilot.exe --run <Quelle:ID>` auf, mit dem Symbol der Spiel-EXE. Der Zusatz „(HDR)“ verhindert Namensgleichheit mit
den Verknüpfungen der Launcher.

**E59 `System.GC.ConserveMemory = 5`** in der Laufzeit-Konfiguration der App: im Leerlauf ohne Fenster privat rund
77 statt 95 MB, Arbeitssatz unverändert um 42 MB. Die Leerlauf-CPU blieb gleich (je zwei Messungen über 90 s,
0,10–0,17 % eines Kerns mit und ohne).
