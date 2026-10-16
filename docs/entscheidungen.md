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
angefasst (`ConfigOnlyAutostartService`), damit Prüfskripte den echten Autostart nicht verändern. Ein fehlender
Eintrag wird nicht neu angelegt (präzisiert mit E83): Den haben der Setup („Mit Windows starten“ aus) oder der Nutzer
entfernt, und die Einstellungen zeigen ohnehin den Stand der Registry.

**E58 Verknüpfungen heißen „<Spiel> (HDR).lnk“** (Desktop bzw. Startmenü\Programme) und rufen
`HdrPilot.exe --run <Quelle:ID>` auf, mit dem Symbol der Spiel-EXE. Der Zusatz „(HDR)“ verhindert Namensgleichheit mit
den Verknüpfungen der Launcher.

**E59 `System.GC.ConserveMemory = 5`** in der Laufzeit-Konfiguration der App: im Leerlauf ohne Fenster privat rund
77 statt 95 MB, Arbeitssatz unverändert um 42 MB. Die Leerlauf-CPU blieb gleich (je zwei Messungen über 90 s,
0,10–0,17 % eines Kerns mit und ohne).

## Phase 5: Auslieferung (2026-10-07)

**E60 Auslieferung als einzelne, eigenständige EXE ohne Kompression:** `HdrPilot.exe` (win-x64, SelfContained,
PublishSingleFile, ReadyToRun, native WPF-Bibliotheken eingebettet und beim ersten Start nach `%TEMP%\.net\HdrPilot\`
entpackt). Mit Kompression wäre die EXE 73 statt 168 MB groß, hielte aber alle Assemblys entpackt im Speicher: gemessen
privat 166 statt 81 MB im Leerlauf. Weil der Dauerbetrieb zählt, bleibt die App unkomprimiert. Die Kommandozeile
`cli\hdrpilot.exe` ist ein kurzlebiges Werkzeug und wird komprimiert. Symbole stecken in den Assemblys
(`DebugType=embedded`), damit keine losen .pdb-Dateien entstehen und Stapelverfolgungen im Protokoll Zeilen behalten.
Version 1.0.0. Erzeugt wird alles mit `tools\publish.ps1`, Prüfung in `tools\gates\phase5-publish.ps1`.

**E61 Kein Start ohne Start-Befehl:** Selbst eingetragene Ordner mit keiner oder mehreren EXEs haben keinen
Start-Befehl. „Mit HDR starten“ und die Verknüpfungs-Knöpfe sind dann gesperrt, mit Hinweis in den Details; ein
`--run` lehnt der Kern ab, bevor er schaltet (vorher: HDR kurz an und gleich wieder aus). Gefunden bei der
Nachprüfung der README.

**E62 Zweiter Aufruf mit nur `--minimized` reicht nichts weiter:** Startet der Autostart, während HdrPilot schon
läuft, öffnet sich kein Fenster mehr. Ein zweiter Start ohne Argument öffnet weiterhin die Spielebibliothek.

**E63 Lizenzhinweise in der Auslieferung:** `tools\publish.ps1` legt `THIRD-PARTY-NOTICES.txt` bei, zusammengesetzt aus
den Lizenztexten der Laufzeitpakete in der gebauten Version (.NET-Laufzeit, Windows Desktop, Microsoft.Extensions.*)
und den THIRD-PARTY-NOTICES der .NET-Laufzeit. Die Versionen kommen aus der deps.json der Veröffentlichung.

**E64 README gegen den Quelltext geprüft:** `phase5-readme.ps1` gleicht Abschnitte, App-Schalter, CLI-Hilfe,
Exit-Codes, Standardwerte, Ablageorte und alle fett gesetzten Oberflächen-Bezeichnungen mit Code und resx ab (mit
Gegenprobe). Die inhaltlichen Aussagen haben fünf unabhängige Prüfer gegen den Code gelesen; 55 Hinweise, alle
eingearbeitet (darunter E61, E62 und die korrigierte CLI-Hilfe zu `--config-dir`).

## Anti-Cheat-Prüfung (2026-10-07)

Auf Wunsch des Nutzers geprüft, ob HdrPilot ein Anti-Cheat auslösen kann: Code-Prüfung, Bestandsaufnahme der
installierten Spiele (21 mit Anti-Cheat: EA Javelin, 15× Easy Anti-Cheat, nProtect GameGuard, PunkBuster u. a.),
Recherche zu EAC, BattlEye, Javelin, Vanguard, FACEIT, Ricochet und vergleichbaren HDR-Werkzeugen, dazu ein Prüfer, der
gezielt Gegenbelege suchte. Ergebnis: sehr geringes Risiko, kein belegter Fall für Werkzeuge ohne Injektion, Hooks,
Overlay, Treiber oder künstliche Eingaben. Belegte Fälle harmloser Software betrafen DLL-Hooks (DisplayFusion,
Javelin), geänderte Priorität fremder Prozesse (Process Lasso, Vanguard) und künstliche Eingaben (AutoHotkey, reWASD).

**E65 EXE-Pfade ohne Handle:** `ProcessImagePathResolver` liest den Pfad zu einer PID über
NtQuerySystemInformation(SystemProcessIdInformation = 88) und rechnet den NT-Pfad über QueryDosDevice um (auch
verbundene Netzlaufwerke und UNC, Volumes ohne Buchstaben über ihren Einhängeordner, etwa C:\Games). HdrPilot öffnet
damit keinen fremden Prozess mehr, auch kein Spiel, und erkennt nebenbei geschützte Prozesse über ihren Pfad. Die Klasse ist undokumentiert, aber seit Vista vorhanden (Geoff Chappell,
phnt; lokal gemessen: Pfade für 277 von 278 Prozessen ohne Adminrechte). Nur wenn Windows die Klasse nicht kennt,
fällt HdrPilot auf OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) zurück.

**E66 Tastenkürzel-Notlösung nie während eines Spiels:** `IHdrController.SetHdrAsync` bekommt `allowHotkeyFallback`.
Erlaubt ist die Notlösung nur, solange kein Spiel läuft: kein Prozess irgendeines Spiels der Bibliothek (ausgewählt
oder nicht, eigener Abgleich über alle Einträge) und kein über HdrPilot gestartetes Spiel, dessen Prozess noch nicht
erkannt ist (das Zurückstellen nach der Startfrist läuft deshalb ohne Notlösung). Das gilt auch für das Umschalten von
Hand (Infobereich, Bibliothek, `--toggle-hdr`); nur `hdrpilot hdr --hotkey-fallback` entscheidet der Nutzer je Aufruf
selbst. Zusätzlich fragt der Controller unmittelbar vor dem Senden SHQueryUserNotificationState ab und sendet nicht,
wenn eine Vollbild-Anwendung läuft; das deckt auch Spiele außerhalb der Bibliothek ab. Künstliche Tastendrücke an ein
laufendes Spiel sind genau das, wogegen Anti-Cheats vorgehen. Der Hinweistext sagt das.

**E68 Wiederanlauf übernimmt auch nicht ausgewählte Spiele aus dem gemerkten Zustand:** Vor dem ersten Abgleich nach
dem Start liest HdrPilot die Spiele aus `session-state.json` und ordnet sie mit zu. Vorher stellte ein Neustart von
HdrPilot mitten in einem über die App gestarteten, nicht ausgewählten und gerade nicht als installiert markierten
Spiel HDR zurück. Gefunden bei der Nachprüfung der Anti-Cheat-Härtung (drei Prüfer, ein Gegenprüfer; 11 von 17
Hinweisen bestätigt und behoben, darunter E65-Volumes, E66-Lücken, Architekturtest und ein Test, der mit Adminrechten
übersprungen worden wäre).

**E67 Architekturtest gegen verbotene Techniken:** `AntiCheatSafetyTests` durchsucht `src` nach Speicherzugriff,
Einschleusen, Hooks, Debug-, Treiber- und Rechte-Schnittstellen, Modullisten, `Process.GetProcess*`, `WaitForExit`
und Eingabe-Mitlesen; OpenProcess nur im Rückfall der Pfadabfrage und nur mit dem kleinsten Recht, SendInput nur in
der Notlösung, Manifest `asInvoker`. Eine Gegenprobe zeigt, dass der Scanner Treffer findet.

Nicht umgesetzt: Signieren der EXE (kein Zertifikat vorhanden; hilft vor allem gegen SmartScreen), Auspacken der
WPF-DLLs neben die EXE statt nach %TEMP% (widerspräche „eine einzelne EXE“).

## Gemeinsamer Test mit echten Spielen (G4.5, 2026-10-07)

Veröffentlichte EXE in `%LOCALAPPDATA%\Programs\HdrPilot`, echte Ablage. Je Launcher ein Spiel mit eigenem HDR
(Recherche PCGamingWiki u. a.), Xbox nur mit Palworld (ohne eigenes HDR). Ergebnis: alle Starts über Knopf und
Verknüpfung mit HDR bestätigt vor dem Start-Befehl; automatische Erkennung bei Steam, Epic und Ubisoft bestätigt;
EA Javelin ohne Meldung; keine Warnung und kein Fehler im Protokoll des Tages. Beobachtungen:
- Dead Island 2 hing einmal beim automatischen Start (HDR-Umschaltung 0,7 s vor dem Engine-Log); die Wiederholung mit
  gleichem Timing lief. Ursache unbewiesen; Gegenmittel (Neustart-Puffer, Launcher-Vorabsignal, Steam-Hülle) sind für
  die Rückfrage G5.3 vorgemerkt.
- Palworld beendet sich manchmal nicht (Prozess ohne Fenster, ohne CPU). HdrPilot lässt HDR dann richtigerweise an.
  Automatisch beenden oder zurückstellen wäre riskant (Xbox-Spiele werden beim Minimieren angehalten); ein Hinweis ist
  für G5.3 vorgemerkt.
- Xbox: Andere Werkzeuge melden den Pfad unter `C:\Program Files\WindowsApps\…`, die Pfadabfrage ohne Handle (E65)
  unter `C:\XboxGames\…\Content\…` und passt damit zum eingelesenen Ordner.

**E69 Startfrist wartet auf ein offenes Launcher-Fenster:** Läuft beim Start über die App noch ein Programm aus dem
Spielordner (auch ein Hilfsprogramm wie der REDlauncher, der auf die Anmeldung wartet), beginnt die Startfrist neu,
höchstens 30 Minuten lang. Im Test schaltete HdrPilot HDR nach 120 s ab, während der Nutzer sich noch anmeldete.

**E70 Lesbare Verknüpfungsnamen:** „:“ wird zu „ - “, Schrägstriche zu „-“, Anführungszeichen zu Apostrophen, übrige
unzulässige Zeichen entfallen („Dying Light - The Beast (HDR)“ statt „Dying Light_ The Beast (HDR)“).

**E71 Doppelte Startanforderungen werden übergangen:** Läuft das Spiel schon oder kam der letzte Start vor weniger als
15 s, schickt HdrPilot keinen zweiten Start-Befehl, schaltet aber HDR wie gehabt ein. Beobachtet mit Battlefield 6
(doppelter Doppelklick); bei direkt gestarteten EXEs (GOG, eigene Einträge) gäbe es sonst eine zweite Instanz.

**E72 Prüfskripte bedienen nur ihre eigene Testinstanz:** `Find-Window` in `tools\gates\Phase4Common.ps1` verlangt die
Prozess-ID der Testinstanz. Vorher suchte es Fenster nur über die AutomationId; während des gemeinsamen Tests war das
echte HdrPilot des Nutzers mit offener Bibliothek geöffnet, und `phase4-ui.ps1` hat dort drei Einstellungen geändert
(Nachlaufzeit 15 s, Intervall 750 ms, Hinweise aus). Sofort zurückgestellt (Konfiguration per FileSystemWatcher neu
geladen); Gegenprobe mit einer Attrappe mit offenem Fenster: deren Konfiguration bleibt byte-gleich.

**E73 Netzwerk nur für den Abgleich der HDR-Liste (Entscheidung des Nutzers, 2026-10-07):** Der Auftrag verbot
Netzwerkzugriff. Auf Wunsch des Nutzers zeigt HDRpilot an, welche Spiele laut PCGamingWiki HDR unterstützen (Kennzeichen,
Sortierung, Filter), und gleicht diese Liste bei jedem Start ab (Rückfrage im Chat: „mit der Option bei jedem Start
abzugleichen“). Umfang: nur ein lesender Abruf der öffentlichen Liste über die Cargo-Abfrage von pcgamingwiki.com,
höchstens einmal pro Stunde, verzögert nach dem Start, abschaltbar, mit Knopf „Jetzt abgleichen“; es werden keine Daten
über die Spiele des Nutzers gesendet, der Abgleich läuft lokal. Ohne Netz: zuletzt geladene oder mitgelieferte Liste.
Daten unter CC BY-NC-SA 3.0 (Quellenangabe in Einstellungen und THIRD-PARTY-NOTICES). Der Steam-Kurator „HDR Games“
wird nicht genutzt (keine Schnittstelle, nur Auslesen der Store-Seite).

## Phase 6: Feedback nach dem gemeinsamen Test (2026-10-07)

**E74 Bibliothek nach Nutzer-Feedback:** Der Starten-Bereich (Mit HDR starten, Verknüpfungen, Steam-Startoption) steht
direkt unter dem Spielkopf, vor der Automatik. Meldungen erscheinen beim auslösenden Bereich (`DetailMessage` unter
Starten, `DetailExeMessage` unter den EXE-Dateien). Sortierung „Name“ oder „Zuletzt gespielt“ (`SortOrder`), gespeichert
als `AppSettings.LibrarySortOrder` (Zeichenkette, unbekannte Werte gelten als „Name“, Standard wird nicht geschrieben).
Liste und Details mit `GridSplitter` (`LibrarySplitter`, Pfeiltasten 24 px, Doppelklick setzt zurück); Anteil der
Details als `AppSettings.LibraryDetailsRatio` (null = 0,35), beim Öffnen auf das Fenster begrenzt; Mindestbreiten 400 und
340, Werkzeugleiste passt sich in drei Stufen an, Standardbreite 1320. Prüfung: `phase6-ui.ps1` (G6.1).

**E75 Steam-Startoption `"<exe>" --wrap -- %command%`:** Der Wrapper läuft vor Einzelinstanz, Fenster, Tray und Thema,
liest die rohe Befehlszeile über `GetCommandLineW` (Environment.CommandLine baut sie in .NET neu auf) und übernimmt
alles nach dem ersten alleinstehenden `--` unverändert. Läuft HdrPilot und ist `SteamAppId` gesetzt, reicht er
`--prepare Steam:<id> --ready-event <name>` an die Instanz weiter (WM_COPYDATA), wartet höchstens 8 s auf das benannte
Ereignis, dann 1,5 s Ruhezeit (Starts über HdrPilot hatten 2-3 s Vorlauf und nie Probleme), startet das Spiel mit
geerbter Umgebung und beendet sich sofort: kein Handle auf das Spiel, kein Warten (E65), Steam verfolgt Nachfahren-
Prozesse selbst. Ohne Instanz, ohne AppID oder bei unbekanntem Spiel startet das Spiel sofort ohne HDR; der Wrapper
startet nie das Tray-Programm (Steam würde es dem Spiel zurechnen). Je Lauf eine Zeile in `logs\wrap.log` (256 KB,
dann rotiert). Die Instanz schaltet über `SessionManager.PrepareAsync` (wie LaunchAsync ohne Start-Befehl, Auslöser
„Start über Steam-Startoption“). Prüfung: `phase6-wrap.ps1` (G6.2): HDR 1,85 s vor dem Prozessstart, Wrapper endet
32 ms nach dem Spielstart; ohne Instanz startet das Spiel nach 0,2 s.

**E76 „Zuletzt gespielt“ für alle Spiele:** Der Steam-Scanner liest `LastPlayed` aus den appmanifest-Dateien; beim
Einlesen gilt der spätere Wert. Zusätzlich vermerkt HdrPilot den Start jedes Bibliotheksspiels, das es laufen sieht
(einmal je Lauf, nach dem Schalten), nicht nur bei HDR-Sitzungen.

**E77 Neustart-Puffer und Hänger-Hinweis:** Endet eine Sitzung nach weniger als 5 Minuten und hatte HDRpilot HDR dafür
eingeschaltet (WasHdrOn = false, Zurückstellen an), gilt statt der Nachlaufzeit der Puffer (Standard 180 s,
`restartBufferSeconds` in config.json, 0 = aus); ein Neustart im Puffer setzt die Sitzung fort. Läuft beim Ablauf eine
Vollbild-Anwendung, wartet das Zurückstellen (höchstens 30 min). Der Tooltip zeigt die Restzeit. Hänger-Hinweis: nur
während laufender Sitzungen alle 30 s eine Abfrage `SystemProcessInformation` (Rechenzeit ohne Handle, Offsets nach
phnt/Chappell, gegen winternl.h geprüft) und bei Prozessen ohne Rechenzeit `EnumWindows`/`IsWindowVisible` (kein
Handle, kein Hook). Zwei Minuten ohne Fenster und ohne Rechenzeit: ein Hinweis je Sitzung, kein Beenden, kein
Zurückstellen. Prüfskripte schalten den Puffer in ihren Testablagen ab (`tools\gates\TestConfigSeed.ps1`).

**E78 Frühwarnung aus Launcher-Protokollen:** `LauncherSignalWatcher` liest EpicGamesLauncher.log (`LaunchWithOptions`,
`LogUriHandler … ?action=launch`) und Steams console_log.txt (`GameAction [AppID …] : LaunchApp changed task to`) nur
lesend mit (FileShare.ReadWrite|Delete, ab Dateiende, Rotation erkannt), ausgelöst von FileSystemWatcher und einer
Längenprüfung im vorhandenen Abfragetakt (gemessen 0,005 % eines Kerns). Für Spiele mit „HDR automatisch“ ohne
Sitzung ruft es `PrepareAsync(…, LauncherSignal)` auf: HDR 1-2 s vor dem Spielprozess. Rohzeilen werden nie
protokolliert (Austausch-Codes, Benutzernamen). Beim Launcher-Signal nie die Tastenkürzel-Notlösung (das Spiel startet
ohnehin gleich). `launcherSignals` in config.json, Standard an.

**E79 HDR-Liste umgesetzt (präzisiert E73):** Freie Cargo-Abfragen bei PCGamingWiki brauchen seit August 2026 ein
Konto mit Bot-Passwort. Ohne Konto (Standard, vom Nutzer gewählt) fragt HDRpilot die Wiki-Seiten seiner Spiele über die
normale MediaWiki-API ab; dabei gehen die Spielnamen an PCGamingWiki (der Nutzer wurde darauf hingewiesen). Je Start
höchstens 2 Seitenanfragen (100 Titel) und 2 Suchen mit 2 s Abstand, 30 s nach dem Start, höchstens stündlich, bekannte
Spiele alle 14 Tage neu; Zuordnung über Steam-AppID und GOG-ID, sonst über den Namen. Mit Konto (vom Nutzer gewünscht)
Anmeldung mit Bot-Passwort und vollständige Liste über cargoquery, kein Spielname wird gesendet; Passwort mit DPAPI
(CurrentUser) in `pcgw-account.dat`. Keine mitgelieferte Liste. Mit `--config-dir` gleicht HDRpilot nicht selbst ab.

**E80 Umbenennung in HDRpilot:** Sichtbar alles: EXE `HDRpilot.exe`, Produkt- und Firmenname, Fenstertitel, Texte,
Infobereich, Hinweise, Protokollzeilen, Startmenü-Ordner, Autostart-Wert, Datenordner, User-Agent, README und
Lizenzkopf. Die Ordner auf der Platte bleiben dieselben (Windows unterscheidet nicht nach Groß-/Kleinschreibung), ebenso
der Autostart-Wert in der Registry. Intern bleiben `HdrPilot`: Projekte, Namensräume und die Namen von Mutex, Fenster-
klasse und Ereignissen (Kernel-Objektnamen unterscheiden Groß-/Kleinschreibung; alte und neue Version erkennen sich so
weiter). Prüfung: `phase6-name.ps1` (G6.4).

**E81 Nachprüfung Phase 6:** Vier unabhängige Prüfer (Sitzungslogik, Wrapper und Signale, HDR-Liste, Oberfläche und
Name) und ein Gegenprüfer; 10 Funde bestätigt, 8 verworfen (begründet im Prüfbericht), alle bestätigten behoben, jeweils
mit Test und Gegenprobe (ohne Korrektur rot). Sitzungslogik: Der Hinweis „Spiel hängt“ wartet auch bei gesperrtem
Bildschirm oder Bildschirmschoner (dort zeigt das Tray nichts an; vorher ging der einzige Hinweis verloren). Ein Neustart
im Neustart-Puffer schaltet HDR wieder ein, falls der Nutzer es zwischendurch von Hand ausgeschaltet hat (wie jeder
erkannte Start, E41: nur bei „HDR automatisch“, nicht bei pausierter Automatik, und nur wenn der Prozess länger als die
Nachlaufzeit weg war; kürzere Lücken wie ein Selbstneustart des Spiels lassen eine Umschaltung von Hand unangetastet,
gefunden bei der Gegenprüfung der Korrektur). Nach einem Neustart von HDRpilot
übernommene Sitzungen gelten nie als kurz. Ein Launcher-Signal übergeht nur noch laufende Spiele; eine endende oder
noch wartende Sitzung wird wieder auf den Start eingestellt (Frist neu, keine Tastenkürzel-Notlösung), sonst ging HDR
mitten im neuen Start aus und an. Abfragepuffer nach gescheiterter Speicheranforderung nie weiterverwendet. Texte:
Pause-Tooltip und README nennen die Steam-Startoption als Ausnahme der Pause; Lizenzhinweise vollständig umbenannt und
von `phase6-name.ps1` ganz geprüft. HDR-Liste: Anmeldesperre von PCGamingWiki erkannt (MediaWiki meldet sie als
„Failed“ mit Begründung), unlesbares gespeichertes Konto lässt sich entfernen (danach keine veraltete Fehlerzeile mehr;
die Kontoanzeige folgt jedem Abgleich), Weiterleitungsketten werden verfolgt (höchstens 5 Schritte), Interwiki-Ziele
gelten als „nicht gefunden“. Beide Korrekturrunden wurden je von einem weiteren Prüfer gegengeprüft.

**E82 Tray-Menü schloss sich sofort (Fehlermeldung des Nutzers):** Nach einem Rechtsklick auf das Symbol öffnete sich das
Menü und war nach etwa 150 ms wieder zu (Windows 11, Symbol in der Taskleiste wie im Überlauf). Gemessen mit echtem
Rechtsklick (SendInput): HDRpilot holte erst das unsichtbare Hilfsfenster und nach dem Öffnen auch das Menü-Popup in den
Vordergrund. Das Popup ist nicht aktivierbar (WS_EX_NOACTIVATE); Windows gab den Vordergrund danach an das vorher aktive
Programm zurück, WPF schloss das Menü beim Verlust der Aktivierung. Lösung: nur noch das Hilfsfenster in den Vordergrund
(wie beim klassischen TrackPopupMenu-Muster). Nach der Gegenprüfung ergänzt: Das Hilfsfenster ignoriert WM_CLOSE
(Alt+F4, solange es vorn ist, hätte es sonst zerstört und alle späteren Menüs ohne Vordergrund geöffnet) und wird bei
Bedarf neu angelegt; nach einem abgebrochenen Menü geht der Vordergrund an die Taskleiste zurück (wie es die Shell-Doku
für Tray-Menüs vorsieht), statt bei einem unsichtbaren Fenster zu bleiben; ein Rechtsklick bei offenem Menü schließt es;
verweigert Windows den Vordergrund, steht das im Protokoll. Das Gate `phase6-traymenu.ps1` prüft mit echter Maus und
Tastatur an einer Testinstanz, jeweils aus einem anderen aktiven Programm heraus (eigenes Hilfsfenster des Skripts):
Menü bleibt offen, Symbolwechsel bei offenem Menü, Esc mit Rückgabe an die Taskleiste, WM_CLOSE an das Hilfsfenster,
Klick in ein anderes Programm, Klick auf einen Eintrag, Tastatur. Vor jedem Klick prüft es das Fenster unter dem Zeiger
(nur Taskleiste, Überlauf, Testinstanz, eigenes Fenster). Gegenproben: alter Code und fehlende Ergänzungen fallen
durch. Die bisherigen Tests öffneten das Menü nur ohne echten Rechtsklick und konnten den Fehler nicht sehen.

**E83 Installer (Wunsch des Nutzers, hebt „kein Installer“ aus dem Auftrag auf):** Der Nutzer wählte einen eigenen Setup
statt Inno Setup oder Velopack: kein Download, keine neue Abhängigkeit, keine Adminrechte, kein Netz. Die Setup-Datei
`HDRpilot-Setup-<Version>.exe` ist die normale `HDRpilot.exe` mit angehängtem Paket (Zip mit README, Lizenzhinweisen und
`cli\hdrpilot.exe`) und einem Abschluss (Kennung, Längen, SHA-256 der EXE); der .NET-Einzeldatei-Start stört sich an
angehängten Daten nicht (geprüft). Findet die EXE beim Start diesen Abschluss an sich selbst, läuft sie als Setup (eigenes
Fenster im Stil der App), sonst normal; die installierte EXE ist byte-gleich mit der portablen und hat keinen Abschluss.
Installiert wird für den angemeldeten Benutzer nach `%LOCALAPPDATA%\Programs\HDRpilot` (beim Nutzer derselbe Ordner wie
bisher, Windows unterscheidet nicht nach Groß-/Kleinschreibung; Autostart, Verknüpfungen und Steam-Startoptionen zeigen
weiter auf dieselbe EXE), Eintrag unter „Installierte Apps“ in HKCU, Startmenü-Verknüpfung, auf Wunsch Desktop-Verknüpfung
und Autostart. Aktualisieren: einen neueren Setup ausführen; er beendet die laufende App über den vorhandenen Befehlsweg
(`--exit`, kein Prozess-Handle, kein gewaltsames Beenden, E67), ersetzt die EXE mit Rückfall auf die alte bei Fehlern und
startet sie wieder. Einstellungen (`%APPDATA%\HDRpilot`) und Daten (`%LOCALAPPDATA%\HDRpilot`) fasst der Setup nie an.
Entfernen (`HDRpilot.exe --uninstall` aus „Installierte Apps“): läuft aus einer Kopie im Temp-Ordner, entfernt nur die
eigenen Dateien, Verknüpfungen, die auf die EXE zeigen, den eigenen Autostart-Wert und den Eintrag; stellt einen gemerkten
HDR-Zustand wieder her und verwirft danach `session-state.json` (die einzige Ausnahme); Einstellungen und Daten sonst nur
auf ausdrücklichen Wunsch; warnt vor Steam-Startoptionen, die auf HDRpilot zeigen (die Spiele starteten sonst nicht
mehr). Prüfläufe nur mit `--test-root` (alle Orte und ein eigener Registry-Zweig darunter), damit nie die echte
Installation berührt wird. Setup und Entfernen schließen sich über die Sperre `Local\HDRpilot.Setup` aus (neuer
Kernel-Name, daher schon HDRpilot; Prüfläufe hängen `.<Hash>` ihrer Testablage an, wie im Registry-Zweig). Version 1.1.0.
Nach der Gegenprüfung ergänzt: Der Setup startet die App über die Shell (sie erbt keine Handles, etwa die umgeleitete
Ausgabe eines Skripts), dabei ohne Zonenprüfung (eine Download-Markierung, die `File.Replace` von der alten EXE
übernimmt, oder die einer portablen Kopie löste sonst eine Sicherheitsabfrage oder SmartScreen aus), und nie, wenn er
mit Adminrechten läuft (sie liefe sonst ebenfalls erhöht; das Ergebnis sagt es, die Fortschrittsliste führt den Schritt
dann nicht). `--exit` geht genau einmal an die laufende Instanz; hat sie beim Start noch kein Nachrichtenfenster, sieht
der Setup innerhalb der Frist etwa jede Sekunde nach. „Mit Windows starten“ aus entfernt den Wert `HDRpilot` auch, wenn
er eine andere HDRpilot.exe startet (der Wertname gehört HDRpilot); Werte für andere Programme bleiben. Deshalb ist der
Haken beim Aktualisieren auch dann vorbelegt, wenn der Wert eine andere Kopie startet; an zeigt er danach auf die
installierte EXE. Schreibgeschützte Programmdateien werden ersetzt. Das Entfernen nennt Schritt und Datei seines ersten
Fehlers; ein nicht wiederhergestellter HDR-Zustand ist nur ein Hinweis. Die Kopie zum Entfernen löscht am Ende ihren
Temp-Ordner (cmd.exe ohne AutoRun und verzögerte Erweiterung, bis zu 30 Versuche im Sekundenabstand) und, sobald der weg
und sie damit beendet ist, einmal ihren Entpack-Ordner `%TEMP%\.net\HDRpilot-Uninstall` (nicht öfter: dort könnte
inzwischen eine neue Kopie entpackt haben); den Entpack-Ordner der App und den der Setup-Datei fasst sie nie an.

**E84 Container der Claude-App (Befund 2026-10-09):** Die Claude-Desktop-App ist ein MSIX-Paket. Prozesse, die aus
einer Sitzung darin gestartet werden (Skripte, Prüfläufe, auch HDRpilot und sein Setup), laufen in ihrem Container,
obwohl `GetCurrentPackageFullName` für sie kein Paket meldet. Windows leitet ihre Schreibzugriffe auf `HKCU\Software`
und auf Ordner unter `%APPDATA%` und `%LOCALAPPDATA%` in den privaten Speicher der App um
(`%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local|Roaming\…` und eine eigene Registry-Datei); im
Container sieht alles normal aus, von außen fehlt es. Nicht umgeleitet: `%LOCALAPPDATA%\Programs`, `%TEMP%`, Startmenü,
Projektordner und, wie die Gegenprüfung am 2026-10-10 maß, Neues in Ordnern unter AppData, die es nur außerhalb gibt
(`D3DSCache`, `CrashDumps`, auch ein darin neu angelegter Unterordner). Umgeleitet wird, was direkt in `%LOCALAPPDATA%`,
in einem neuen Ordner oder in einem Ordner mit Kopie im Container entsteht (`Microsoft`, `NuGet`, das alte `HdrPilot`).
Folgen vom 2026-10-07 bis 2026-10-09: Konfiguration und Protokolle der aus der Sitzung gestarteten
Instanzen lagen nur in der Container-Kopie (dort auch die bisherigen Einstellungen des Nutzers; die echte `config.json`
entstand erst am 2026-10-09 um 23:18); der Autostart-Wert war nur virtuell, deshalb startete HDRpilot nach einem Neustart
nicht von selbst; die Eintragung des Setups unter „Installierte Apps“ war ebenfalls nur virtuell. Der Nachweis zu G7.5
ist korrigiert. Erkennung (`ContainerDetector`): eine Probe-Datei mit GUID direkt in `%LOCALAPPDATA%` anlegen, unter
`%LOCALAPPDATA%\Packages\*\LocalCache\Local` am gleichen Namen suchen und immer löschen (mit ihr verschwindet die
umgeleitete Kopie); gemessen 76 ms bei 168 Apps. Fehler gelten als „kein Container“ (gesperrt wird nur, was sicher
erkannt ist). Zuerst lag die Probe im Datenordner; nach der Gegenprüfung geändert, weil ein schon vorhandenes
`%LOCALAPPDATA%\HDRpilot` ohne Kopie im Container sie unverdächtig aussehen ließ, obwohl Eintrag und Autostart trotzdem
nur virtuell geändert würden. Geprüft wird deshalb der Prozess, nicht der Datenordner. Entscheidungen des Nutzers: Setup
und Entfernen prüfen vor jeder Änderung, ob sie im Container einer anderen App laufen; dann ändern sie nichts, nennen im
Fenster die App und den richtigen Weg (Doppelklick im Explorer bzw. „Installierte Apps“) und enden still mit Exit-Code 7
(beim Entfernen wie Exit-Code 2 direkt vom ersten Aufruf, ohne `ERGEBNIS`-Zeile); die Kopie zum Entfernen räumt dabei
trotzdem ihren Temp-Ordner weg. Prüfläufe mit `--test-root` überspringen die Prüfung (sie laufen auch im Container
vollständig darin ab), außer die Umgebungsvariable `HDRPILOT_TEST_CONTAINER_CHECK=1` verlangt sie. Die App prüft einmal
je Start im Hintergrund und schreibt im Container eine Warnung ins Protokoll (das je nach Datenordner echt oder in der
Container-Kopie liegt). Arbeitsregel: Aktionen am echten System (installieren, starten, Einstellungen, Registry und
Dateien des Nutzers prüfen) nur von außerhalb des Containers ausführen, etwa per WMI (`Win32_Process`), und von außen
nachprüfen.

**E85 Neustart-Puffer einstellbar, Standard 1 Minute (Wunsch des Nutzers nach dem Helldivers-2-Test):** Der Nutzer
testete Helldivers 2 mit der installierten Version: Nach einer kurzen Sitzung war HDR knapp 2 Minuten nach dem Schließen
des Spiels noch an, weil der Neustart-Puffer lief (E77, damals 3 Minuten). Er entschied: Standard 1:00 statt 3:00, in
den Einstellungen mit Minuten und Sekunden einstellbar (0:00 bis 30:00, 0:00 = aus). Begründung: Der Puffer soll nur
einen schnellen Neustart nach Hänger oder Absturz abfangen; nach einem gewollten Beenden wirken 3 Minuten HDR wie ein
Fehler. Wer länger für einen Neustart braucht (Dead Island 2: 96 s, E77), stellt den Wert selbst höher. Die Grenze für
kurze Sitzungen (5 Minuten) und das Warten auf eine Vollbild-Anwendung bleiben. Ist der Puffer nicht länger als die
Nachlaufzeit, wirkt er wie 0:00: nur die Nachlaufzeit, kein Warten auf eine Vollbild-Anwendung (Gegenprüfung: vorher
galt dann die Nachlaufzeit als Puffer samt diesem Warten, mit dem Regler hätte so schon 0:05 HDR bis zu 30 Minuten
angehalten). Umsetzung: Der Standard `SessionManagerOptions.RestartBuffer` = 60 s gilt, solange `restartBufferSeconds`
in config.json fehlt (Werte über 1800 gelten als 30:00, negative als aus). Die Einstellung „Neustart-Puffer“ steht
direkt nach der Nachlaufzeit: zwei Regler wie bei den übrigen Zahlenwerten (Minuten 0 bis 30, Sekunden 0 bis 59,
AutomationIds `RestartBufferMinutes` und `RestartBufferSeconds`), Anzeige „1:00 min“ bzw. „aus“; 30 Minuten plus
Sekunden wird auf 30:00 begrenzt (der Sekunden-Regler springt auf 0 zurück). Gespeichert wird mit „Speichern“ als
`restartBufferSeconds`; ein fehlender Wert bleibt ungeschrieben, solange der Nutzer den Standard nicht ändert (so gilt
ein späterer neuer Standard auch für ihn, G7.8). Die Sitzungslogik liest den Wert bei jedem Spielende aus demselben
ConfigService wie die Einstellungen: Eine Änderung gilt ohne Neustart ab dem nächsten Spielende, ein schon laufender
Puffer behält sein Ende. Der Countdown im Tooltip bleibt unverändert. Prüfung: G7.6 (Unit-Tests mit `RestartBuffer` im
Namen, auch Einstellungen und Sitzungslogik über einen gemeinsamen ConfigService; `phase4-ui.ps1` stellt an einer
Testinstanz 30:05 ein, prüft die Begrenzung auf 30:00, speichert 0:45 und prüft `restartBufferSeconds` = 45, auch nach
einem Neustart); `phase5-readme.ps1` gleicht den Standard in der Einstellungstabelle der README und in jedem Absatz von
„So funktioniert es“, „Bekannte Grenzen“ und „Fehlersuche“, der den Puffer nennt, mit dem Quelltext ab.

**E86 Spielsymbole aus lokalen Quellen und eigenes Symbol (Wunsch des Nutzers, G8.2/G8.3):** Die Messung an der
Bibliothek des Nutzers (647 Spiele, 2026-10-10, `docs/recherche/symbole`) ergab 53 Spiele ohne gutes Symbol: 19 ohne
Symbol (9 ohne gefundene EXE, 5 EXEs ohne Symbol, 4 falsche EXEs wie `P4PopulateDepot.exe` und `elementviewer.exe`,
Palworld mit gesperrter EXE) und 34 mit einem Standardsymbol (EasyAntiCheat 12, Unreal-„U“ 14, Epic/EOS-Starter 4,
Godot 3, Unity 1), dazu 17 unscharfe mit höchstens 32 px. Beanstandet hatte der Nutzer, dass manche Spiele das Symbol
des Anti-Cheats statt des Spiels zeigten (Gears of War: E-Day, The Isle). Er entschied: nur lokale Quellen, nichts aus
dem Netz; bei Steam zuerst das offizielle Steam-Symbol, die EXE nur, wenn es fehlt, ein Standardsymbol ist oder deutlich
unschärfer (Steam unter 48 px, EXE ab 48 px); ein eigenes Symbol per Rechtsklick, das allem vorgeht.

Quellen: Die Scanner liefern Symbol-Hinweise (`GameEntry.IconCandidates`, beste zuerst, höchstens 4, nur vorhandene
Dateien mit Laufwerksbuchstaben, nie UNC-Pfade). Steam (`SteamIconLocator`): `steam\games\<clienticon>.ico` laut
appinfo.vdf `common/clienticon` (Leser für v27 bis v29 erweitert), sonst `DisplayIcon` von „Steam App <ID>“ unter HKLM
in der 64-Bit-Sicht (gemessen: alle 798 Einträge dort, keiner unter WOW6432Node), sonst `IconFile` einer
`.url`-Verknüpfung (Startmenü, Desktop eine Ebene tief), sonst `appcache\librarycache\<ID>\<icon>.jpg` (immer 32 px);
Namen, die keine 40-stelligen SHA-1-Werte sind, werden nie als Pfad benutzt. GOG: `goggame-<ID>.ico`, dann
`*_glx_square_icon_v2.webp` aus dem Zwischenspeicher von GOG Galaxy. Xbox: Logos aus MicrosoftGame.config
(Square480x480Logo zuerst) und appxmanifest.xml, auch nur mit Zusatz wie `.scale-200` vorhanden (die größte Variante
in Pixeln gewinnt: `targetsize-48` sind 48 px, `scale-100` die Grundgröße aus dem Namen wie 150 bei Square150x150Logo,
sonst 44 wie beim kleinen App-Symbol, das als einziges `targetsize-` kennt; Kontrast-Varianten nie, nie außerhalb des
Paketordners). Ubisoft: `DisplayIcon` von „Uplay Install <ID>“ (32-Bit-Sicht).
Epic, EA, Battle.net und eigene Spiele: nur die EXE. Die Hinweise stehen in config.json (`iconCandidates`), damit die
Bibliothek schon vor dem Ende des Einlesens beim Start das richtige Symbol zeigt; gemessen sind das bei 650 Spielen
89 KB (18 % der Datei), fast immer ein Pfad je Spiel. Alte Konfigurationen laden unverändert (fehlende Felder: keine
Hinweise, kein eigenes Symbol).

Wahl (`GameIconService`): eigenes Symbol, dann die Hinweise in ihrer Reihenfolge, dann die EXE, die nur gesucht wird,
wenn kein Hinweis gut ist. Gut ist ein Symbol, das kein Standardsymbol ist und in der Datei mindestens 48 px groß ist;
die echte Größe kommt aus dem Verzeichnis der .ico bzw. der ersten Symbolgruppe der EXE (die Windows für Index 0
nimmt), gelesen mit wenigen Dateizugriffen statt der ganzen EXE. Das erste gute gewinnt; gibt es keins, das erste ohne
Standardsymbol, notfalls ein Standardsymbol. Die Standardsymbole erkennt eine feste Liste von zehn Fingerabdrücken: die
acht gemessenen (EasyAntiCheat, Unreal schwarz und umrandet, Epic/EOS, Godot 3 zweimal, Godot 4, Unity), dazu Unreal 3
(UDK.exe) und das Windows-Programmsymbol. Fingerabdruck: SHA-256 über die 48-px-Pixel (BGRA, durchsichtige Pixel auf 0)
und ein Wahrnehmungsabdruck (Helligkeitsverlauf 9×8, Helligkeit 8×8, Deckkraft 8×8, mittlere Farbe / 15); Standardsymbol
bei gleichem Hash oder Abstand bis 8. Gemessen: Aus den Spieldateien neu gelesen ergaben alle acht denselben Hash wie in
der Recherche; von den 598 verschiedenen EXE-Symbolen der Bibliothek hat das ähnlichste echte den Abstand 29
(Necrosmith und Space Marine 2 zu Godot 4), keins wird fälschlich erkannt, Boltgun (drei Einträge mit einem gemeinsamen
echten Symbol) hat 68. Eine Regel „mehrere Spiele teilen ein Symbol“ gibt es deshalb nicht. Beim Lesen der Symbolgröße
umfasst eine Sektion der EXE wie beim Loader von Windows ihre VirtualSize (nur ohne sie SizeOfRawData): Godot 4 legt die
Spieldaten in eine Sektion `pck` mit VirtualSize 8, deren Rohdaten über `.rsrc` hinausreichen; mit der größeren von
beiden galt das 256-px-Symbol von Buckshot Roulette, Overlooting, s.p.l.i.t und Spin Hero als unbekannt groß, und die
Liste zeigte ihr 32-px-Steam-Symbol.

EXE-Wahl (`IconExecutableResolver`): eine Ausschlussliste nur für Symbole (`IconExcludedExecutables`, nicht in
`HelperExecutables`, wo `start_protected_game.exe` und `*EAC.exe` bewusst als früher Start-Hinweis für HDR fehlen):
EasyAntiCheat (`start_protected_game`, `*EasyAntiCheat*`, „EAC“ als eigenes Wortteil wie in `GZWClientEAC`, nicht in
`BeachInvasion`), BattlEye (`*_BE`, `BEService*`, `BELauncher`, `*BattlEye*`), EA Anti-Cheat (`EAAntiCheat.*`, alles mit
„AntiCheat“ im Namen), GameGuard/nProtect (`GameMon*`, `GameGuard*`, `*nProtect*`, Installer `GGSetup`, Deinstaller
`gguninst`), PunkBuster (`pbsvc*`, `PnkBstr*`), Epic Online Services (`EOSBootstrapper`, `EpicOnlineServices*`,
`EOSOverlayRenderer*`), `LaunchGame`, `GodotWorkshopUtility`, Absturzmelder, Redistributables, Installer und
Deinstaller (`CrashReport*`, `UnityCrashHandler*`, `BsSndRpt*`, `*ErrorReporter*`, `UE*PrereqSetup*`, `vc_redist*`,
`PhysX*SystemSoftware*`, `oalinst`, `VulkanRT*`, die Installer von Ubisoft Connect, Social Club und Rockstar,
`dxsetup`, `unins*`, `*uninst*` …; auch alle Fremdbestandteile aus der Standardliste der Hilfsprozesse, weil eine in
den Einstellungen geänderte Liste sie ganz ersetzt), Werkzeuge (`P4PopulateDepot`, `elementviewer`, `*editor*`,
`java`, `vconsole2`, `qc_eyes`, die Unreal-3-Werkzeuge in `Binaries` wie `UnrealFrontend`, `ShaderKeyTool`,
`StatsViewer` und die des Source SDK in `bin` wie `hlfaceposer`, `hlmv`, `studiomdl`, `hammer`; ohne Steam-Symbol bekam
Portal sonst das von `hlfaceposer.exe`) sowie alles in den Ordnern `BattlEye`, `GameGuard`, `EasyAntiCheat`,
`_CommonRedist`, `__Installer` und `Redist` unter dem Spielordner. Gegen alle EXEs der Bibliothek geprüft (2026-10-12):
Die Erweiterung um GameGuard-Installer, PunkBuster, Absturzmelder, Installer und Source-SDK-Werkzeuge schließt 57
weitere EXEs aus, alle Fremdbestandteile oder Werkzeuge, keine Spiel-EXE. Im Spielordner kommen Unreal-Projekte zuerst
(`[<Projekt>\]Binaries\Win64\*.exe`, `*-Shipping.exe` vorn, `Engine` nie).
Mit Symbolprobe (höchstens 24 EXEs) werden EXEs ohne Symbol oder mit Standardsymbol übersprungen; es gilt die erste
Gruppe (erkannt, vom Launcher genannt, Spielordner) mit einem echten Symbol, darin die erste EXE mit gutem Symbol, sonst
die erste ohne Standardsymbol. Eine schärfere EXE aus einer späteren Gruppe verdrängt eine erkannte oder genannte nie:
Gemessen bekäme SWAT 3 ohne sein GOG-Symbol sonst statt des 32-px-Symbols von `swat.exe` das des Mod-Starters
`Last_Resort.exe`. Die tiefere Suche
(bis drei Ebenen, höchstens 300 Ordner, 300 EXEs und 1 s; gemessen ohne Grenze im Mittel 66 ms, schlimmstenfalls 12 s)
läuft nur, wenn es sonst gar kein echtes Symbol gibt: Ein richtiges 32-px-Steam-Symbol wird nie gegen eine fremde EXE
aus der Tiefe getauscht. Alles läuft auf dem Arbeits-Thread der Symbole, nur für sichtbare Zeilen und nur bei offener
Bibliothek.

Gemerkte Wahl: `<Datenordner>\icon-cache.json` merkt sich je Spiel Datei, Art der Quelle, Größe und Änderungszeit der
Datei und ob das Symbol gut war; ein Kaltstart liest dann nur diese eine Datei (die EXE-Suche dauerte kalt 12,8 s für
alle Spiele). Neu gewählt wird, wenn sich das Spiel (Ordner, installiert oder nicht, Hinweise, EXEs, Liste der
Hilfsprozesse) oder der Stand der Regeln (`IconSignature.RulesVersion`) ändert, die Datei eine andere Größe oder
Änderungszeit hat oder die EXE-Wahl sie nicht mehr zulässt (etwa nach einer erweiterten Ausschlussliste oder bei einer
Datei außerhalb des Spiels), außerdem bei einer Wahl ohne gutes Symbol nach 7 Tagen. Eine Wahl ohne gutes Symbol wird
nicht gemerkt, solange der Spielordner fehlt (nicht installiert, Laufwerk getrennt): Nach der Installation am selben Ort
blieben die Eingaben gleich, und ohne Ordner kostet das erneute Wählen kaum etwas. Eine gemerkte Datei wird nur gelesen,
wenn sie lokal ist (nie UNC, auch wenn config.json und icon-cache.json von Hand dorthin zeigen). Geschrieben wird
gesammelt, wenn keine Symbole mehr anstehen; ist die Datei kaputt, wird nur neu gewählt. Fragt eine Zeile neu an (neue
Hinweise aus dem Einlesen, eigenes Symbol), während ihre alte Anfrage noch wartet, wird die alte übersprungen und ihr
Ergebnis verworfen, damit es das neue Symbol nicht überschreibt. Verknüpfungen „Mit HDR starten“ zeigen dasselbe Symbol
wie die Liste, soweit es eine .ico oder .exe ist: ein eigenes Symbol als .ico (siehe unten), sonst die gemerkte Wahl
(etwa das Steam-Symbol statt eines Starters mit EasyAntiCheat-Symbol), ohne gemerkte Wahl dieselbe Wahl wie in der
Liste, aber ohne tiefere Suche; ist sie ein Bild (Xbox-Logo, WebP, librarycache), die EXE wie in der Liste. Gemessen an
allen 651 Spielen ohne gemerkte Wahl (2026-10-12): 649 Verknüpfungen zeigen dieselbe Datei wie die Liste, die zwei
übrigen sind Xbox-Spiele mit einem Logo als Bild und ohne lesbare EXE (Symbol von HDRpilot); vorher bekamen 17
Steam-Spiele dabei das Unreal-, UDK-, Godot- oder Unity-Symbol ihrer EXE statt des Steam-Symbols. Angelegt wird die
Verknüpfung abseits des UI-Threads, weil die Wahl ohne gemerkte Wahl bis zu 24 EXE-Symbole liest.

Eigenes Symbol (G8.3): Rechtsklick auf ein Spiel in der Liste oder Klick auf das große Symbol im Detailbereich öffnet ein
Menü mit „Symbol ändern…“ (`ChangeIconMenuItem`) und „Symbol zurücksetzen“ (`ResetIconMenuItem`, nur mit eigenem Symbol
frei). Der Dateidialog („Bilder und Programme“) nimmt PNG, JPG, WebP, BMP, GIF, ICO oder EXE; daraus wird außerhalb des
UI-Threads eine PNG mit 96 px (Seitenverhältnis bleibt, Rest durchsichtig) unter
`<Datenordner>\icons\<Schlüssel>_<Prüfsumme>.png` (mit `--config-dir` in der Testablage), die gewählte Datei bleibt
unverändert. In config.json steht nur der Dateiname (`customIcon`); ein Pfad oder ein anderer Name als eine schlichte
PNG wird beim Laden verworfen. Gelöscht wird nur die PNG, deren Name zum Spiel gehört (`CustomIcons.BelongsTo`), beim
Zurücksetzen und beim Entfernen eines eigenen Spiels: „Symbol zurücksetzen“ löscht nie fremde Dateien, auch nicht die
eines anderen Spiels, dessen Namen eine von Hand geänderte config.json nennt. Das eigene Symbol geht allem vor; fehlt
die Datei (etwa auf einem anderen PC: config.json liegt im roamenden Profil, der Datenordner nicht) oder ist sie
unlesbar, gilt das automatische. Die Liste zeigt die Änderung sofort. Der Dateidialog ist austauschbar
(`ILibraryDialogs.PickIconFile`); weil UI-Automation keinen Dateidialog bedienen kann, nimmt er nur mit `--config-dir`
die Datei aus der Umgebungsvariable `HDRPILOT_TEST_ICON_FILE`. Für Verknüpfungen entsteht beim ersten Anlegen neben der
PNG eine .ico gleichen Namens, die die PNG unverändert enthält (PNG in ICO, seit Windows Vista): Eine Verknüpfung kann
keine PNG zeigen, und „das eigene Symbol geht allem vor“ gilt auch für sie. Die .ico wird nur geschrieben, wenn sie fehlt
oder nicht mehr zur PNG passt, bei „Symbol ändern…“ gleich mit erneuert und nie gelöscht, auch nicht von „Symbol
zurücksetzen“: Verknüpfungen verweisen dauerhaft auf ihre Symboldatei und behalten so ihr Symbol, neue zeigen danach
wieder das automatische. Die .ico bleibt bis zum Entfernen samt Datenordner liegen (höchstens eine je Spiel).

Robustheit, Messung und Prüfung: Fehlende Decoder (WebP, HEIF auf anderen PCs), kaputte oder gesperrte Dateien gelten
als „kein Symbol“ und landen nur im Debug-Protokoll; Bilddateien über 32 MB oder mit mehr als 36 Megapixeln werden nicht
dekodiert. Messung danach (2026-10-12, Bibliothek nur lesend eingelesen, 651 Spiele): alle 651 mit Symbol, 626 gut
(mindestens 48 px, kein Standardsymbol), kein Standardsymbol mehr; 25 bleiben unter 48 px (24 Steam-Spiele, deren
einziges Symbol 32 px hat, und Dead Island 2 mit 44 px). Quelle: 418-mal ein Hinweis des Launchers (alle 636
Steam-Spiele haben ein Symbol unter `steam\games`), 233-mal die EXE (Steam-Symbol 32 px und EXE ab 48 px, oder Spiele
ohne Hinweise). Gears of War: E-Day und The Isle zeigen ihr 256-px-Steam-Symbol, Palworld das Square480x480Logo,
SWAT 3 und 4 die 256-px-goggame-.ico. Die Wahl aller 651 Spiele nacheinander ohne gemerkte Wahl dauerte im ersten Lauf
des Tages (Datenträger kalt, Stand vor der Korrektur der Sektionsgrenzen) 29,4 s, Median 6 ms, ein Spiel 10,5 s (Aliens:
Dark Descent); warm rund 1 s. Das läuft auf dem Arbeits-Thread der Symbole, nie auf dem UI-Thread. Prüfung: G8.2
(`test.ps1 -Filter "FullyQualifiedName~Icon"`),
G8.3 (`-Filter "FullyQualifiedName~CustomIcon"` und `tools\gates\phase8-icons.ps1` an einer Testinstanz: Menü über das
große Symbol, „Symbol zurücksetzen“ gesperrt, „Symbol ändern…“ mit vorgegebener Datei legt eine 96-px-PNG an und setzt
`customIcon`, Zurücksetzen entfernt beides; die Liste hat ihre Wahl in `icon-cache.json` gemerkt).

**E87 Versionsnummer sichtbar (Wunsch des Nutzers, G8.1):** Die Version steht an genau einer Stelle (`AppVersion`):
Major.Minor.Build der HDRpilot-Assembly aus `Directory.Build.props`, nach derselben Regel wie Setup und Eintrag unter
„Installierte Apps“ (`SetupSupport.Normalize`); der Setup übernimmt sie als eigene Version, die Zahl steht nie fest im
Text. Angezeigt wird sie rechts im Kopf der Einstellungen als „HDRpilot 1.3.0“ (zurückgenommenes Info-Symbol statt eines
farbigen Punkts, der in HDRpilot für einen Status steht) und im Tray-Menü als graue, nicht anklickbare erste Zeile unter
dem Kopf. Dort zeigte die erste Fassung „HDRpilot 1.2.0“ direkt unter dem Kopf „HDRpilot“; der Nutzer wählte am
2026-10-12 „Version 1.2.0“ (`AppVersionLabelFormat`). Die Zeile ist nicht fokussierbar, die Pfeiltasten springen an ihr
vorbei (Unit-Test mit echter WPF-Tastaturnavigation und `phase6-traymenu.ps1` am echten Desktop mit Zustimmung des
Nutzers). Die Sichtbarkeit der Tray-Zeile prüft der Render-Test über den Vergleich mit derselben Zeile ohne Text: reine
Farbpixel hingen bei der dünnen Schrift an den Ziffern der Version (29 bei „Version 1.3.0“ gegen die alte Schwelle 30).
Das Startprotokoll schreibt die Version weiter vierstellig (Core kennt `AppVersion` nicht); das ist nur ein Format.

**E88 Windows Auto HDR je Spiel (Wunsch des Nutzers, G9; nach dem Livetest G9.6 neu gefasst):** „Windows Auto HDR“
(Windows rechnet das SDR-Bild eines Spiels ohne eigenes HDR in HDR um) ist eine eigene Option je Spiel
(`GameConfig.AutoHdr`, JSON `autoHdr`, Standard aus; ältere config.json laden unverändert) und klar getrennt von „HDR
automatisch“ (HDRpilot schaltet Windows-HDR beim Spielstart ein und danach zurück). Platz nach Wahl des Nutzers: im
Detailbereich ein eigener Abschnitt „BILD“ unter „AUTOMATIK“ mit dem Schalter „Windows Auto HDR“ (`DetailAutoHdr`), der
Erklärung und dem immer sichtbaren Hinweis, dass es nur beim Start über HDRpilot wirkt (Starten, Verknüpfung,
Steam-Startoption; `DetailAutoHdrStartHint`); in der Liste nur das Kennzeichen „AUTO HDR“ direkt hinter dem Namen (nur
Anzeige; violett getönt, damit es sich vom rosa umrandeten „HDR“ laut PCGamingWiki und vom Schalter abhebt), kein
zweiter Schalter und keine neue Spalte. Kennzeichen und Tray-Eintrag teilen ein eigenes Symbol (Funkeln,
`AppGlyphs.AutoHdr`), das sonst nirgends vorkommt: nicht die Sonne von „HDR automatisch“/„HDR jetzt“, nicht die Palette
von „HDR-fähig“. Die Option ist unabhängig von „HDR automatisch“; ist das aus, steht unter dem
Schalter immer der Hinweis, dass Windows-HDR schon vor dem Spielstart an sein muss (die Starts über HDRpilot schalten es
mit ein). Spiele mit eigenem HDR laut PCGamingWiki (`Native` oder `Limited`, dieselbe Regel wie das Kennzeichen
„HDR“/„HDR*“) zeigen Titel und Schalter blasser (Deckkraft 0,7, deutlich heller als ein gesperrter Schalter mit 0,4),
aber voll bedienbar, mit dem Hinweis, besser das HDR des Spiels zu nehmen; Erklärung und Hinweise bleiben voll lesbar.
Das Schalten selbst ändert in Windows nichts.

Livetest am 2026-10-12 mit dem Nutzer (G9.6, Windows 11 26H2, Build 26300; Werte per WMI außerhalb des Containers
gesetzt, danach byte-gleich zurückgestellt): Auto HDR wirkt nur, wenn Windows-HDR und der globale Schalter (Token
`AutoHDREnable=1` im Wert `DirectXUserGlobalSettings` unter `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`,
derselbe Schalter wie „Auto HDR“ in den Windows-Einstellungen) schon beim Start des Spielprozesses an sind (TUNIC,
DirectX 11: Regler „HDR-Intensität anpassen“ in der Game Bar bedienbar). Der Wert je EXE (`AutoHDREnable=2097`) wirkt
bei global ausgeschaltetem Auto HDR nicht; den globalen Schalter oder HDR erst einzuschalten, während das Spiel läuft,
wirkt erst beim nächsten Start. The Plucky Squire und Aliens: Dark Descent zeigten den Regler grau (nicht freigegeben
oder ein alter Stand: Das Einstellungsfenster der Game Bar muss man ganz schließen und neu öffnen); Sekiro hat eigenes
HDR.

Entscheidungen des Nutzers danach, so gebaut: Nur noch der globale Schalter, und zwar als Standard der Option, nicht als
Einstellung; die frühere Notlösung (`autoHdrGlobalFallback`) und „Auto-HDR-Einträge zurücknehmen“ entfallen, ein
Eintrag `autoHdrGlobalFallback` aus einer Vorabversion wird beim Laden übergangen. Der Weg je EXE ist samt
Zustandsdatei `autohdr-state.json` entfernt (G9.2 gestrichen; Entwurf und Code stehen in `feature/autohdr-core`,
34c83cf); HDRpilot schreibt keine Werte je EXE. Gesetzt wird der Schalter nur bei Starts, die HDRpilot vorher kennt: Die
Sitzungslogik kündigt jeden solchen Start als Erstes an (`IAutoHdrSessionHooks.OnStartAnnounced`), vor dem Schalten von
HDR und vor dem Start-Befehl, bei „Mit HDR starten“ und „(HDR)“-Verknüpfungen (`LaunchAsync`), bei der
Steam-Startoption (`PrepareAsync`) und bei der Frühwarnung von Steam und Epic (für Spiele mit „HDR automatisch“ über
`PrepareAsync`, sonst nur die Ankündigung, ohne HDR zu schalten). Ein bloß erkanntes Spiel setzt nichts (Windows hat den
Wert dann schon gelesen); eine Meldung nach einem Start ohne HDRpilot gibt es bewusst nicht. Aus demselben Grund kündigt
die Sitzungslogik nichts an, solange das Spiel schon läuft (zweiter Klick auf „Mit HDR starten“, die Verknüpfung oder
ein spätes Launcher-Signal): gesetzt hielte der Schalter bis zum Spielende nur für andere Spiele. Ebenso setzt der neue
Versuch nach einem Fehler beim Setzen (Merker nicht gespeichert, Wert nicht lesbar oder nicht schreibbar; nach 30 s)
nur, solange ein angekündigter Start noch aussteht; läuft das Spiel schon, bleibt der Schalter bis zum nächsten
angekündigten Start aus. HDRpilot setzt nur das Token (vorhanden an seiner Stelle, sonst angehängt; alle anderen Paare,
ihre Reihenfolge und der Abschluss bleiben; ein fehlender Wert wird `AutoHDREnable=1;`) und fasst nur saubere REG_SZ
an. Stand es schon auf `1` (Wahl des Nutzers),
ändert HDRpilot nichts und stellt nichts zurück. Jeder neue Start liest den Schalter neu, auch während HDRpilot ihn
hält: Steht das Token dann nicht mehr auf `1` (der Nutzer hat in Windows oder im Tray umgestellt), ist das seine Wahl;
der Merker entfällt, der neue Start setzt den Schalter für sein Spiel und merkt sich diesen Stand als den, der danach
zurückkommt. Den Wert von vorher hält `<Datenordner>\autohdr-global.json` (vor dem Schreiben angelegt, nach dem
Zurückstellen gelöscht). Lässt die Datei sich nicht löschen, etwa weil ein Virenscanner sie ohne Freigabe zum Löschen
offen hält, lässt sie sich unter NTFS auch nicht ersetzen; das Kennzeichen „erledigt“ steht deshalb in einer eigenen
Datei daneben, `autohdr-global.done` (Zeitpunkt und Text des Setzens). Einen Merker, zu dem es passt, stellen weder der
nächste Start noch das Entfernen über den Setup je zurück, sie löschen beide Dateien; die App wiederholt das Löschen
alle 30 s und beim Beenden (zuerst den Merker, nie solange ein neuer Start den Schalter hält). Ein Merker eines
späteren Setzens passt nicht (anderer Zeitpunkt); ist das Kennzeichen nicht lesbar oder defekt, bleibt alles, wie es ist (die App
lässt Auto HDR in diesem Lauf aus, der Setup meldet es; „Daten löschen“ lässt einen gesperrten Merker samt Kennzeichen liegen).
Die Ankündigung gilt, solange die HDR-Sitzung auf den
Spielprozess wartet (Startfrist samt Verlängerung für
Launcher-Fenster), ohne Sitzung bis zur Startfrist; scheitert schon der Start-Befehl, nimmt
`IAutoHdrSessionHooks.OnStartAbandoned` sie sofort zurück. Läuft das Spiel, endet es wie der HDR-Zustand: Nachlaufzeit,
nach einem Lauf unter 5 Minuten der Neustart-Puffer, ein Neustart in dieser Zeit läuft weiter. Mehrere Spiele teilen
sich den Schalter, zurückgestellt wird nach dem letzten; Option aus oder Spiel entfernt stellt sofort zurück.
Zurückgestellt wird nach denselben Regeln wie zuvor: Steht noch genau der geschriebene Text da, kommt der
Originalzustand zurück (fehlte der Wert, wird er gelöscht); ist anderes geändert, das Token aber noch `1`, nur das Token
(alles andere bleibt, wie es jetzt ist; bleibt nichts übrig, wird der Wert gelöscht statt das Original zurückzuholen);
ist das Token geändert oder der Wert weg oder nicht mehr
sauber, bleibt er stehen („extern geändert“, die Wahl des Nutzers). Schaltet der Nutzer in Windows aus und gleich wieder
ein, ist das nicht zu unterscheiden und wird zurückgestellt. Beim Beenden von HDRpilot wird sofort zurückgestellt, nach
einem Absturz beim nächsten Start (laufende Spiele mit der Option werden übernommen, nie als kurz); das Entfernen über
den Setup stellt einen verbliebenen Schalter zurück (Schritt „Auto HDR zurückstellen“, Hinweis bei einem Fehler) und
löscht den Merker auch ohne „Daten löschen“. Während HDRpilot den Schalter hält, gilt Auto HDR für jedes Spiel, das in
dieser Zeit startet.

Tray-Eintrag „Auto HDR“ (G9.9, Wunsch des Nutzers, `TrayAutoHdr`) direkt unter „HDR jetzt …“: abhakbar, zeigt den
Windows-Schalter, gelesen beim Öffnen des Menüs (`IAutoHdrService.ReadGlobalSwitch`, ein Registry-Wert, kein Zeitgeber),
und schaltet nur das Token (`SetGlobalSwitchByUser`, andere Tokens bleiben). Tooltip „gilt ab dem nächsten Spielstart,
auch ohne HDRpilot“; ist der Wert nicht lesbar oder in fremdem Format, ist der Eintrag gesperrt (die Pfeiltasten
überspringen ihn) und nennt den Grund. Hält HDRpilot den Schalter gerade für einen Spielstart
(`ITrayHost.AutoHdrSwitchHeld`), sagt der Tooltip, dass er danach wieder aus ist, damit das Häkchen nicht wie eine
dauerhafte Einstellung aussieht. Eine Änderung dort ist die Wahl des Nutzers: Hält HDRpilot den Schalter gerade, gibt es
ihn ab, stellt danach nichts zurück und setzt ihn für die laufenden Sitzungen nicht neu; erst ein neuer Start über
HDRpilot liest ihn wieder (siehe oben) und stellt nach seinem Spiel diese Wahl wieder her. Schaltet der Nutzer ihn vor
einem Start ein, schaltet HDRpilot ihn nie aus. Versionszeile und Pfeiltasten bleiben wie in E87 (Pfeil nach unten
trifft „HDR jetzt …“, Pfeil nach oben vom Start „HDRpilot beenden“).

Steam-Startoption beim Einschalten (G9.7): Schaltet der Nutzer „Windows Auto HDR“ für ein Steam-Spiel ein und ist das
gespeichert, prüft HDRpilot außerhalb des UI-Threads und nur lesend mit dem Leser der Warnung beim Entfernen
(`SteamLaunchOptionScanner`: `localconfig.vdf` aller Konten), ob für die AppID eine Startoption mit dem EXE-Namen von
HDRpilot und `--wrap` eingetragen ist (`SteamLaunchOptionStatus`). Fehlt sie oder ist Steam nicht lesbar, fragt es
(„Steam-Startoption kopieren?“), ob es sie kopieren soll: dieselbe Funktion wie „Steam-Startoption kopieren“ (E75) mit
denselben Bedingungen (Programmpfad bekannt, Zwischenablage frei), Rückmeldung unter dem Abschnitt „BILD“
(`DetailAutoHdrMessage`). Eine Funktion, die die Startoption in Steam einträgt (etwa bei beendetem Steam), gibt es nicht;
HDRpilot schreibt nie in Steams Dateien, eintragen muss der Nutzer sie selbst. Ablehnen oder ein gescheitertes Kopieren
ändert nichts, Auto HDR bleibt an. Nicht gefragt wird bei anderen Quellen, beim Ausschalten, bei gesetzter Startoption,
nach gescheitertem Speichern und wenn der Schalter inzwischen wieder aus oder ein anderes Spiel gewählt ist; nie zwei
Rückfragen übereinander.

Nur HKCU, nie HKLM, keine Administratorrechte; der Anti-Cheat-Architekturtest bleibt unverändert. Testinstanzen
erreichen den echten Schlüssel nie: Mit `--config-dir` schreibt HDRpilot unter `HKCU\Software\HDRpilot.Tests\AutoHdr\<Name>`,
Name aus `--autohdr-test-root` oder aus dem Ablageordner abgeleitet (wie die Testwurzel des Setups); Unit-Tests nutzen
eine Attrappe oder einen eigenen Testschlüssel. Prüfung: G9.1 (`test.ps1 -Filter "FullyQualifiedName~AutoHdr"` und
`tools\gates\phase9-autohdr-ui.ps1` an einer Testinstanz: Hinweise, Kennzeichen, Einstellungen ohne Auto HDR, „Mit HDR
starten“ mit einem Fühler als Testspiel, der beim Prozessstart `AutoHDREnable=1` in der Testwurzel sieht, danach genau
zurück; HDR wird dabei nie geschaltet), G9.3 (Filter `AutoHdrGlobal`), G9.4 (`phase7-setup.ps1` mit nachgestelltem
Absturz), G9.7 (Filter `AutoHdrSteam`, Steam als Attrappe oder Testordner), G9.9 (Filter `AutoHdrTray`, der echte
Dienst über einem Schlüssel im Speicher; `phase6-traymenu.ps1` prüft den Eintrag an einer Testinstanz mit Testwurzel,
läuft aber nur am echten Desktop mit Zustimmung). RTX HDR gehört nicht zu HDRpilot und bleibt zurückgestellt (`docs/recherche/auto-hdr-rtx-hdr.md`), ebenso die
Intensität (`AutoHDRStrength`) und das Erzwingen für nicht zugelassene Spiele (`D3DBehaviors`).

**E89 Neugestaltung „Showcase Hero“ (Wunsch des Nutzers 2026-10-14/15, G10):** Der Nutzer hat die Richtung „Showcase
Hero“ gewählt und den Prototyp v4 abgenommen (`docs/design/showcase-hero/`); seine vier Anmerkungen (Raster im Vollbild,
Reihenfolge Liste/Raster/Regal, Nit-Skala bis zur echten HDR-Spitze, Anordnung der Seite „Darstellung“) gehören zu den
Kriterien. Umgesetzt wird in drei parallelen Teilen auf einem gemeinsamen Gerüst (Verträge, Konfigurationsfelder,
Verdrahtung, Bildschirmfotos): Bilder und Bildschirmwerte, Darstellung und Einstellungen, Bibliothek und Effekte.

Bilder (G10.3) kommen nur lesend aus den lokalen Launcher-Caches: Steam `appcache\librarycache` (deutsche Varianten
zuerst), GOG Galaxy `webcache`, Ubisoft `cache\assets`, Xbox-Paketbilder und für Epic und EA das Symbol aus der EXE. Fehlt
ein Bild, erzeugt HDRpilot ein Ersatzbild (Farbfeld aus dem Symbol mit dem Namen als Umriss-Muster, Cover, Monogramm);
verschmierte Steam-Kapseln ersetzt ein Ausschnitt aus dem Titelbild, Logos werden auf ihren sichtbaren Teil zugeschnitten.
Umgebungsfarben und die helle „HDR“-Fassung werden je Bild einmal berechnet und unter `%LOCALAPPDATA%\HDRpilot\art\`
gespeichert (Verzeichnis `index.json`). Neue Bilder erscheinen von selbst; die Cache-Ordner werden nur beobachtet,
solange ein Fenster offen ist. Eigene Bilder („Bild wählen…“) stehen je Bildart als Kennzeichen in `games[].artwork`
(`gen`, `alt:<Datei>`, `custom:<Datei>`, nur schlichte Dateinamen, sonst automatisch) und liegen in `art\custom\`.

SteamGridDB (G10.4) ist die einzige Ausnahme von „nur lokal“: standardmäßig aus (`settings.artwork.steamGridDb`), nur mit
eigenem API-Schlüssel (mit DPAPI verschlüsselt in `steamgriddb-key.dat`, wie das PCGamingWiki-Bot-Passwort), füllt nur
lokal fehlende Bilder und zeigt den Netzwerkzugriff in den Einstellungen an. Ist es aus oder fehlt der Schlüssel, gibt es
keinen Netzwerkzugriff dafür; mit `--config-dir` läuft es nie von selbst.

Bildschirmwerte (G10.7) kommen über DXGI `IDXGIOutput6::GetDesc1` (Spitze, Vollbild, Minimum, Farbtiefe, HDR aktiv),
gelesen nur nach einem HDR- oder Bildschirmwechsel und nur, solange ein Fenster sie zeigt. Das Maximum jeder Nit-Skala ist
die höchste Nit-Zahl des Bildschirms bei eingeschaltetem HDR (beim MAG 341C 456), zuletzt mit HDR gemessen und in
`display-luminance.json` gemerkt, nie ein fester Wert wie 1000.

Darstellung (G10.1, G10.2, G10.8): Duoton und Tritonus mit acht Vorlagen (Standard „Abendrot“) und eigenen Farben, Thema
Automatisch, Hell oder Dunkel, gespeichert in `settings.appearance` (Werte als Text, Unbekanntes gilt als Standard). Alle
Farb-Ressourcen werden aus der Palette abgeleitet und vorab gemischt (Schlüssel in `Theming/ThemeKeys.cs`, Rechnung in
OKLab); Text auf Akzentflächen hält mindestens 4,5:1, Bedienelemente 3:1. Tray- und Fenstersymbol tragen die
Palettenfarben und werden nur bei Farb-, Zustands- oder Taskleisten-Themawechsel neu gezeichnet. Die Seite „Darstellung“
ordnet ihre Bereiche in einem Raster mit Höchstbreite je Bereich und mehreren Spalten bei breiten Fenstern an; ihre
Änderungen gelten sofort.

Bibliothek (G10.5): Ansichten Liste (Standard), Raster und Regal, überall in dieser Reihenfolge (Umschalter, Strg+1/2/3,
Menüs), gemerkt in `settings.libraryView`. Das Raster berechnet Spaltenzahl und Kachelgröße aus der verfügbaren Breite
(nichts gestreckt oder abgeschnitten, auch bei 3440×1440). Alle bisherigen Funktionen, AutomationIds und Tastenkürzel
bleiben.

Effekte (G10.6, G10.9) sind allgemeine Ebenen, die mit jedem Bild funktionieren, keine Animation je Spiel. Stufen Voll,
Reduziert (Übergänge bis 0,3 s, keine Dauer-Animation) und Aus; ist die Windows-Option „Animationseffekte“ aus, gilt
mindestens „Reduziert“. Dauer-Effekte laufen mit höchstens 30 fps und stehen bei minimiertem oder inaktivem Fenster und
während eines Spiels still; bei geschlossenem Fenster läuft nichts. Der HDR-Moment dauert höchstens 2,5 s (höchstens 160
Partikel), danach ist keine Bildschleife mehr angemeldet. Kein BlurEffect, kein DropShadowEffect, keine Mischmodi:
Leuchten und weiche Flächen sind vorab berechnete Bilder oder Verläufe. Schriften sind Windows-Schriften (Bahnschrift,
Segoe UI Variable, Cascadia Mono); eine eingebettete OFL-Schrift nur mit Zustimmung des Nutzers und Eintrag in den
Lizenzhinweisen.

Prüfung: Bildschirmfotos über `--snapshot` mit `--snapshot-sizes` (`review` = 1280×800, 1920×1080, 3440×1440),
`--snapshot-scenes` und `--snapshot-themes` (Dateien `<fenster>-<zustand>-<B>x<H>-<thema>.png`, auch größer als der
Bildschirm); Abdeckungsmessung über `--artwork-report`; `--test-hdr-moment` lässt eine Testinstanz den HDR-Moment ohne
HDR-Schaltung spielen. Neue Gates `phase10-ui.ps1`, `phase10-settings-ui.ps1` und `phase10-gpu.ps1` (misst die
Grafikkartenlast der Testinstanz; ohne aktiven Bildschirm meldet es UMGEBUNG und gilt nicht als bestanden).

Umsetzung (Zusammenführung der drei Teile, G10.10), wo sie vom Entwurf abweicht oder ihn genauer fasst:
- Bilder: Reihenfolge je Bildart eigene Wahl → Launcher-Bild → SteamGridDB (nur eingeschaltet, nur wenn das Bild fehlt)
  → erzeugtes Bild. Das erzeugte Titelbild (1920×620) enthält kein Symbol, weil der Titelbereich je Ansicht 120 bis
  660 px hoch ist; die Bibliothek zeigt das Symbol (höchstens 1,5-fach vergrößert) links vom Namen. Ein Xbox-Kachelbild
  wird dagegen rechts der Mitte eingezeichnet. Zusätzlich gibt es ein erzeugtes Kopfbild (460×215) als Vorschaubild der
  Liste. Die helle Fassung entsteht beim ersten Zeigen eines Titelbilds in Stufenbreiten bis 2560 px (JPEG, `art\boost`,
  höchstens 300 MB); dekodierte Bilder im Speicher höchstens 192 MB. Kennung je Spiel per SHA-256 (gekürzt) über Wahl,
  Symbol-Hinweise, Installationsordner sowie Pfad, Größe und Änderungszeit jeder gefundenen Datei. Ein zweites eigenes
  Bild derselben Art ersetzt das erste unter demselben Namen; scheitert das Speichern der Konfiguration, bleibt jede
  Datei liegen, auf die sie noch zeigt. Gemessen an einer Kopie der Bibliothek (651 Spiele): Steam Titelbild 631/636,
  Logo 628, Cover 628 genutzt plus 3 verschmierte Kapseln (Blue Estate, Dustforce, Homesick), GOG 5/5, Ubisoft
  vollständig, Xbox Kachelbild 2/2, Epic und EA Symbol aus der EXE; erster Lauf 10,4 s, danach unter 1 s.
- SteamGridDB: Schlüsselanteil der DPAPI `HDRpilot.SteamGridDB.v1`; gefragt wird nur nach unbewegten Bildern ohne
  NSFW- oder Spaß-Markierung, eine verschmierte Kapsel zählt als fehlendes Cover.
- Bildschirmwerte: Nach `Invalidate` liest HDRpilot DXGI nur, wenn sich Bildschirme, HDR-Zustand oder Zielbildschirm
  wirklich geändert haben. Meldet DXGI 0 nits (etwa bei ausgeschaltetem Bildschirm), gilt der Wert als unbekannt („—“)
  und überschreibt keine gemerkte Spitze.
- Einstellungen: eine scrollende Seite mit Navigation; Raster je Gruppe mit Bereichen höchstens 760 px breit, Spalten
  mindestens 520 px, Abstand 24 px, höchstens drei Spalten (1280×800 eine, 1920×1080 zwei, 3440×1440 drei Spalten).
  „Abbrechen“ nimmt Änderungen der Darstellung nicht zurück. Farbwechsel blenden bei offenem Fenster 0,9 s über
  („Reduziert“ 0,3 s, „Aus“ sofort).
- Symbole: „HDR aus“ ist die Sonne als Linie im Verlauf Akzent → Atmosphäre ohne Kachel (je Taskleiste auf 3:1
  gebracht), „HDR an“ die gefüllte Kachel, „Automatik pausiert“ mit gelbem und „Spiel läuft“ mit grünem Abzeichen
  (Pause geht vor); unter 24 px eine am Pixelraster ausgerichtete, vereinfachte Glyphe.
- Bibliothek: Der Detailbereich behält den Trenner, ist aber höchstens 560 px breit (Doppelklick: 23 %); der
  Titelbereich im Regal ist höchstens Fensterhöhe − 462 px hoch (in sehr hohen Fenstern genau so hoch, damit unter der
  Kachelreihe kein leeres Band bleibt) und gibt in niedrigen Fenstern bis 440 px nach (Logo
  bzw. Name werden dann kleiner), damit die Kachelreihe ganz über der Statuszeile und die Werkzeugleiste unter der
  Automatik-Karte steht; erst unter 712 px Fensterhöhe werden die Kacheln kleiner als 200 px (höchstens bis 100 px).
  Die Werkzeugleiste faltet in Stufen, die Filter zuletzt. Der Startknopf heißt sichtbar „Starten“, für Bildschirmleser
  weiter „Mit HDR starten“. Neu im Spielmenü: „Installationsordner öffnen“. Die Schublade im Regal ist deckend (keine
  Unschärfe; schon 3 % Durchsicht ließen die Kopfzeile als Geisterschrift durchscheinen); die Polarlichter des Prototyps
  sind nicht umgesetzt. Im Dialog „Bild wählen…“ verschwindet die Liste des vorigen Reiters beim Wechsel sofort, damit
  eine Wahl nie für die falsche Bildart gilt.
- Sichtprüfung (Bildschirmfotos aller Ansichten, hell und dunkel, 1100×680 bis 3440×1440): Der schmale Titelbereich von
  Liste und Raster zeigt wie im Prototyp rechts „Starten“ und „Beim Start …“; wird es eng, weicht erst die Angabe, dann
  der Knopf, danach bricht ein langer Name um bzw. wird das Logo kleiner, und erst unter 300 px fallen Kennzeile und
  Angaben weg (nie abgeschnitten). Im Regal entfällt „Beim Start …“, wenn die Knopfzeile nicht neben die Automatik-Karte
  passt. Ist die Liste so breit, dass „Spiel“ über 640 px bekäme (Vollbild), wachsen „Quelle“, „Zuletzt gespielt“ und
  „HDR“ gleichmäßig mit. Das Spielmenü hat oben Vorschau, Name und „Quelle · zuletzt gespielt“ wie im Prototyp. Die
  Lichtlinie des HDR-Moments hat einen weichen Schein und verglimmt rasch. Für Bildschirmfotos bei ausgeschaltetem
  Monitor setzt `--snapshot-display "Name;Spitze;Vollbild;Minimum;Bit"` feste Bildschirmwerte ein; die Zustände
  `nativ-hdr` und `kontextmenue` (das Menü wird ohne Popup über das Fenster gezeichnet) kamen dazu.
