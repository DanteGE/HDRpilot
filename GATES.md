# Gates: HdrPilot

Scope: Windows-Tray-App (.NET 10, WPF, x64), die installierte Spiele aus Steam, Epic, Xbox, GOG, Ubisoft, EA,
Battle.net und manuellen Einträgen findet, für ausgewählte Spiele HDR vor bzw. bei Spielstart einschaltet und nach
Spielende den gemerkten Zustand wiederherstellt; in fünf Phasen mit je einem Commit.

Alle Skripte liegen in `tools\gates\`. Aufruf jeweils mit `powershell -NoProfile -ExecutionPolicy Bypass -File <skript>`
aus dem Projektordner (unten als `ps <skript>` abgekürzt). Ein Gate gilt nur als erfüllt, wenn der Befehl mit 0 endet
und die EXPECT-Marke ausgibt.

## Phase 1: HDR-Kern und Kommandozeile

- [x] G1.1: Neubau der Solution ohne Warnungen
  CHECK: ps tools\gates\build.ps1
  EXPECT: BUILD_OK_0_WARNINGS
  EVIDENCE: 2026-10-07 exit 0, "Warnungen=0 Fehler=0", BUILD_OK_0_WARNINGS

- [x] G1.2: Alle Unit-Tests grün, keiner übersprungen
  CHECK: ps tools\gates\test.ps1
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-07 exit 0, gesamt=155 erfolgreich=155 fehler=0 übersprungen=0, TESTS_OK 155

- [x] G1.3: `hdrpilot hdr status` listet alle aktiven Bildschirme mit Name, HDR-fähig ja/nein, HDR an/aus (gegen Windows-Bildschirmzahl und MonitorDataStore geprüft)
  CHECK: ps tools\gates\phase1-status.ps1
  EXPECT: PHASE1_STATUS_OK
  EVIDENCE: 2026-10-07 exit 0, "MAG 341C OLED (Hauptbildschirm) HDR-fähig: ja HDR: aus, aktiver Modus Wcg", hdrpilot=1 Windows=1, Registry HDREnabled=False, PHASE1_STATUS_OK

- [x] G1.4: `hdr on`/`hdr off` schalten den Bildschirm, Zustand stimmt mit der Einstellungs-Ablage (MonitorDataStore HDREnabled) überein, 20 Wechsel in Folge ohne Fehler
  CHECK: ps tools\gates\phase1-hdr-toggle20.ps1
  EXPECT: PHASE1_TOGGLE20_OK
  EVIDENCE: 2026-10-07 exit 0, 20/20 ok (Status und Registry je Wechsel gleich), 422–486 ms je Wechsel, Ausgangszustand wiederhergestellt, PHASE1_TOGGLE20_OK

- [x] G1.5: Bildschirm ohne HDR: klare Meldung, eigener Exit-Code (4), keine Ausnahme (Attrappe); dazu am echten System nicht vorhandener Bildschirm (3) und falscher Aufruf (2)
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~CliHdrTests" -MinTests 15
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-07 exit 0, 18/18, TESTS_OK 18

- [x] G1.5b: Negativfälle am echten System
  CHECK: ps tools\gates\phase1-negative.ps1
  EXPECT: PHASE1_NEGATIVE_OK
  EVIDENCE: 2026-10-07 exit 0, nicht vorhandener Bildschirm exit=3 mit Meldung, falscher Aufruf exit=2, PHASE1_NEGATIVE_OK

- [ ] G1.5c: Bildschirm ohne HDR am echten System (Bild-in-Bild-Modus des MAG 341C OLED meldet kein HDR; braucht den Nutzer)
  EVIDENCE: nicht am echten System geprüft
  ABANDON: G1.5c Vom Nutzer am 2026-10-07 gestrichen ("nutze Bild-in-Bild so selten"). Kein Bildschirm ohne HDR verfügbar; der Fall ist nur über Attrappen-Tests (G1.5) belegt.

- [x] G1.6: Unit-Tests für Strukturgrößen, Bitfelder und Build-Weiche
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~NativeLayoutTests|FullyQualifiedName~BitfieldTests|FullyQualifiedName~Build_switch|FullyQualifiedName~Force_api" -MinTests 60
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-07 exit 0, 71/71, TESTS_OK 71

- [x] G1.7: Jede P/Invoke-Struktur, Konstante und Bitposition stimmt mit der wingdi.h (SDK 10.0.26100.0) überein, inkl. Negativkontrolle
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~WingdiHeaderConformanceTests" -MinTests 45
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-07 exit 0, 49/49 (18 Strukturen, 16 Konstanten, 13 Bitpositionen, 2 Negativkontrollen), TESTS_OK 49

- [x] G1.8: Umgebung dokumentiert (Build, Bildschirme mit HDR-Fähigkeit, Launcher, .NET SDK)
  EVIDENCE: docs\umgebung.md: Build 26200.9550, MAG 341C OLED HDR-fähig, 8 Launcher, .NET SDK 10.0.401 (nachinstalliert)

- [x] G1.9 (zusätzlich): Älterer API-Weg (Typ 9/10) am echten System
  EVIDENCE: 2026-10-07 `hdr on --api legacy` exit 0 → Typ 15 meldet hdrEnabled=True, Modus Hdr, Registry HDREnabled=1; `hdr off --api legacy` exit 0 → Modus Wcg, AutoColorManagementEnabled bleibt 1

## Phase 2: Spielbibliothek

- [x] G2.1: `hdrpilot scan` gibt alle gefundenen Spiele als gültiges JSON aus; jeder Eintrag hat Quelle, ID, Name, vorhandenen Installationsordner und Start-Befehl; Fehler je Scanner stehen im JSON
  CHECK: ps tools\gates\phase2-scan.ps1
  EXPECT: PHASE2_SCAN_OK
  EVIDENCE: 2026-10-07 exit 0, 646 Spiele (Steam 631, Epic 7, GOG 5, Xbox 1, Ubisoft 1, EA 1, Battle.net 0), alle 7 Launcher erkannt, 0 Fehler, 631 ms, PHASE2_SCAN_OK; eigene Stichprobe: 637 Steam-Manifeste, im Scan fehlen genau die 6 gefilterten Werkzeuge

- [x] G2.2: Abgleich mit der Bibliothek jedes installierten Launchers, jede Abweichung mit Begründung in docs\phase2-abgleich.md
  EVIDENCE: docs\phase2-abgleich.md (zweite/dritte Quelle je Launcher, von Entwickler und unabhängigem Prüfer am System gemessen); Nutzer bestätigt am 2026-10-07 gegen die Launcher-Ansichten: "Passt so"

- [x] G2.3: Jeder Scanner hat Unit-Tests gegen Beispieldateien bzw. Registry-Attrappe, darunter defekte und unvollständige Dateien
  CHECK: ps tools\gates\phase2-scanner-tests.ps1
  EXPECT: PHASE2_SCANNER_TESTS_OK
  EVIDENCE: 2026-10-07 exit 0, 7 Scanner (BattleNet 28, EA 41, Epic 44, GOG 35, Steam 43, Ubisoft 37, Xbox 37 Tests; je Scanner fehlender Launcher, 8–12 defekt, 3–6 unvollständig), Scanner-Tests 408/408, PHASE2_SCANNER_TESTS_OK

- [x] G2.4: Nicht installierter Launcher ergibt leere Liste ohne Fehler (je Scanner ein Test), ein werfender Scanner stört die anderen nicht
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~Missing_launcher|FullyQualifiedName~LibraryScannerTests" -MinTests 9
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-07 exit 0, 18/18, TESTS_OK 18

- [x] G2.5: Build ohne Warnungen und alle Tests grün am Phasenende
  CHECK: ps tools\gates\build.ps1
  EXPECT: BUILD_OK_0_WARNINGS
  EVIDENCE: 2026-10-07 exit 0, Warnungen=0, BUILD_OK_0_WARNINGS; tools\gates\test.ps1: 768/768, TESTS_OK 768

## Phase 3: Überwachung und Sitzungslogik

- [x] G3.1: Sitzungslogik vollständig mit Attrappen (IHdrController, IProcessWatcher) und steuerbarer Uhr getestet: Zustand merken/wiederherstellen, mehrere Spiele, Nachlaufzeit, Wiederanlauf nach Absturz, nicht HDR-fähiger Bildschirm
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~Session" -MinTests 25
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-07 exit 0, 57/57 (SessionManagerTests und GameMatcherTests), TESTS_OK 57; Gesamtlauf 853/853

- [x] G3.2: Stellvertreter-EXE in einem als Spiel eingetragenen Ordner: Start schaltet HDR innerhalb von 2 s ein, Ende nach der Nachlaufzeit wieder aus
  CHECK: ps tools\gates\phase3-standin.ps1 -Scenario single
  EXPECT: PHASE3_SINGLE_OK
  EVIDENCE: 2026-10-07 11:52 exit 0, HDR an nach 1015 ms, aus 11042 ms nach Ende (Nachlaufzeit 10 s), Protokollzeilen mit Auslöser/Bildschirm/vorher/nachher/Ergebnis, PHASE3_SINGLE_OK

- [x] G3.3: Zwei gleichzeitige Stellvertreter: erst nach dem Ende des zweiten wird zurückgestellt
  CHECK: ps tools\gates\phase3-standin.ps1 -Scenario two
  EXPECT: PHASE3_TWO_OK
  EVIDENCE: 2026-10-07 11:53 exit 0, HDR bleibt 14 s nach Ende von A an, aus 11130 ms nach Ende von B, PHASE3_TWO_OK

- [x] G3.4: War HDR vorher an, bleibt es an
  CHECK: ps tools\gates\phase3-standin.ps1 -Scenario alreadyon
  EXPECT: PHASE3_ALREADYON_OK
  EVIDENCE: 2026-10-07 11:53 exit 0, HDR 15 s nach Ende weiter an, Protokoll "war schon an und bleibt", PHASE3_ALREADYON_OK

- [x] G3.5: App bei laufendem Stellvertreter hart beenden, Stellvertreter schließen, App neu starten: HDR wird zurückgestellt
  CHECK: ps tools\gates\phase3-standin.ps1 -Scenario crash
  EXPECT: PHASE3_CRASH_OK
  EVIDENCE: 2026-10-07 11:54 exit 0, Überwachung per TerminateProcess beendet, gemerkt WasHdrOn=False, HDR blieb an, nach Neustart aus (302 ms nach Startbefehl), Zustandsdatei gelöscht, PHASE3_CRASH_OK. Mit der App wiederholt in G4.6.

- [x] G3.6: Überwachung im Leerlauf sparsam: CPU-Zeit und Handle-Zahl über 60 s gemessen
  CHECK: ps tools\gates\phase3-idle.ps1
  EXPECT: PHASE3_IDLE_OK
  EVIDENCE: 2026-10-07 exit 0, 646 Spiele, CPU 125 ms in 60 s = 0,208 % eines Kerns, Handles 284 -> 281, 54 MB, PHASE3_IDLE_OK (vorher mit Toolhelp 0,599 %: Gate schlug fehl, siehe E30)

## Phase 4: Oberfläche und Tray

- [x] G4.1: Alle Punkte aus „Oberfläche“ bedienbar (UI-Automation: Hauptfenster, Suche, Quellenfilter, Auswahl, Detail-Schalter, Listen-Schalter, Einstellungen, weitergereichte Tray-Befehle)
  CHECK: ps tools\gates\phase4-ui.ps1
  EXPECT: PHASE4_UI_OK
  EVIDENCE: 2026-10-07 13:33 exit 0, alle AutomationIds vorhanden; Suche, Quellenfilter, Auswahl, Detail- und Listen-Schalter schreiben die Konfiguration; weitergereichte Befehle --open/--pause/--exit wirken, PHASE4_UI_OK. Sichtbares Tray-Menü: im gemeinsamen Test G4.5

- [x] G4.2: Einstellungen überleben einen Neustart
  CHECK: ps tools\gates\phase4-ui.ps1
  EXPECT: PHASE4_PERSIST_OK
  EVIDENCE: 2026-10-07 13:33 exit 0, nach Neustart Nachlaufzeit=15, Intervall=750, Benachrichtigungen=Off, Spiel-Schalter erhalten, PHASE4_PERSIST_OK

- [x] G4.3: Nur eine Instanz; zweiter Aufruf mit --run reicht an die laufende Instanz weiter; HDR ist vor dem Prozessstart aktiv (Registry-Zeitpunkt gegen Prozess-Startzeit, Protokoll-Zeitstempel)
  CHECK: ps tools\gates\phase4-run.ps1
  EXPECT: PHASE4_SINGLE_INSTANCE_OK
  EVIDENCE: 2026-10-07 13:39 exit 0, zweite Instanz beendet sich und reicht --run weiter; HDR an laut Registry 13:39:17.698, Prozessstart 13:39:18.077 (Vorlauf 379 ms); Protokoll HDR bestätigt 17.986 <= Start 17.986 <= erkannt 18.257; HDR aus 11011 ms nach Ende, PHASE4_SINGLE_INSTANCE_OK, PHASE4_RUN_OK

- [x] G4.4: Alle UI-Texte in Ressourcendateien (keine festen Texte in XAML, alle verwendeten Schlüssel vorhanden)
  CHECK: ps tools\gates\phase4-resources.ps1
  EXPECT: PHASE4_RESOURCES_OK
  EVIDENCE: 2026-10-07 13:33 exit 0, keine festen Texte in XAML (erlaubt: HdrPilot, HDR, Logo-Zeichen H), alle 331 Schlüsselverwendungen vorhanden, 2 Protokolltexte von Hand geprüft, PHASE4_RESOURCES_OK

- [x] G4.5: Start über die App und über die erzeugte Verknüpfung funktioniert für je ein Spiel pro installiertem Launcher; Protokoll belegt mit Zeitstempeln, dass HDR vor dem Prozessstart aktiv war (gemeinsamer Test mit dem Nutzer)
  EVIDENCE: 2026-10-07 15:59-16:56 gemeinsam mit dem Nutzer, veröffentlichte EXE (1.0.0+4fd2052) mit echter Ablage. Knopf und Verknüpfung je Launcher, Protokoll HDR bestätigt vor Start-Befehl vor erkanntem Prozess: Steam Dying Light: The Beast 16:01:30,547/,554/16:01:55,761 und Verknüpfung 16:04:32,434/,438/34,975; Epic Dead Island 2 16:08:30,280/,284/52,768 und 16:11:11,774/,779/14,991; Ubisoft Avatar 16:22:51,804/,810/59,205 und 16:27:19,739/,745/22,848; Xbox Palworld 16:30:42,686/,695/44,225 und 16:34:05,476/,482/06,579; GOG Cyberpunk 2077 16:43:14,066/,071/21,175 (erster Versuch nach 120 s Startfrist abgebrochen, Anmeldung im REDlauncher, behoben E69) und Verknüpfung 16:48:54,059/,063/03,835; EA Battlefield 6 16:50:32,813/,819/35,684 und 16:55:43,479/,484/46,154, Javelin ohne Meldung. Automatisch erkannt: Steam, Epic (1 Hänger von 2), Ubisoft. Jedes Mal HDR nach Spielende plus 10 s aus; 0 Warnungen/Fehler im Protokoll. Vom Nutzer bestätigt.

- [x] G4.6: Wiederanlauf mit der App (hart beenden, Spiel schließen, neu starten: HDR zurückgestellt) und Leerlauf der App ohne Fenster sparsam
  CHECK: ps tools\gates\phase4-crash.ps1
  EXPECT: PHASE4_CRASH_OK
  EVIDENCE: 2026-10-07 13:35 exit 0, HDR nach hartem Beenden an, nach Neustart zurückgestellt, Zustandsdatei gelöscht; Leerlauf ohne Fenster CPU 0,391 % eines Kerns (Gegenmessung 2x90 s: 0,10-0,17 %), Handles 618 -> 606, Arbeitssatz 50,9 MB, privat 76,9 MB (vorher 95 MB, E59), PHASE4_CRASH_OK

- [x] G4.7: Neubau ohne Warnungen, alle Tests aller Testprojekte grün
  CHECK: ps tools\gates\test.ps1
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-07 14:04 exit 0, build.ps1 BUILD_OK_0_WARNINGS, 2 Testprojekte 1075/1075 erfolgreich, TESTS_OK 1075 (neue Tests zu E61/E62 per Gegenprobe ohne Korrektur rot)

## Phase 5: Auslieferung

- [x] G5.1: Einzelne, eigenständige EXE veröffentlicht und lauffähig (ohne installierte .NET-Runtime-Abhängigkeit)
  CHECK: ps tools\gates\phase5-publish.ps1
  EXPECT: PHASE5_PUBLISH_OK
  EVIDENCE: 2026-10-07 14:07 exit 0, artifacts\publish\HdrPilot: HdrPilot.exe 168,3 MB und cli\hdrpilot.exe 39,0 MB (beide x64), README.md, THIRD-PARTY-NOTICES.txt (18 MIT-Texte); Version 1.0.0; Kopie in %TEMP% ohne erreichbares dotnet gestartet: 70 Module, keines aus einer installierten Runtime (Gegenprobe Debug-Build: 72 erkannt), WPF-Bibliotheken aus der EXE entpackt, Ende per --exit mit Exitcode 0, PHASE5_PUBLISH_OK

- [x] G5.2: README mit Einrichtung, Funktionsweise, bekannten Grenzen und Fehlersuche
  CHECK: ps tools\gates\phase5-readme.ps1
  EXPECT: PHASE5_README_OK
  EVIDENCE: 2026-10-07 14:05 exit 0, alle Abschnitte, 10 App-Schalter, 9 CLI-Aufrufe und 7 Exit-Codes, Standardwerte, Ablageorte und fett gesetzte Bezeichnungen stimmen mit Code und resx (0 Probleme); Gegenprobe 7/7 eingebaute Fehler erkannt; inhaltlich von 5 unabhängigen Prüfern gelesen, 55 Hinweise eingearbeitet (E64), PHASE5_README_OK

- [x] G5.3: Rückfrage beim Nutzer zu WMI-Überwachung, Neustart bei verspätetem HDR, Steam-Hülle, englischer Oberfläche
  EVIDENCE: 2026-10-07 17:15 Rückfrage beim Nutzer (Auswahl im Chat): Steam-Hülle per Feedback beauftragt ("Steam-Startoption kopieren"); WMI-Überwachung, Neustart bei verspätetem HDR und englische Oberfläche ohne Vorliebe, also nicht umgesetzt (WMI und Neustart wurden nicht empfohlen). Zusätzlich aus dem gemeinsamen Test beauftragt: Neustart-Puffer, Frühwarnung aus Epic-/Steam-Protokollen, Hinweis bei hängendem Spielprozess; dazu Feedback zur Oberfläche (Starten-Bereich oben, Sortierung zuletzt gespielt, verschiebbare Spalten) und Umbenennung in HDRpilot (alles Sichtbare inkl. EXE). Umsetzung in Phase 6 (siehe dort).

- [x] G5.4: Kein Eingriff in Spielprozesse: keine verbotenen Techniken im Quelltext, EXE-Pfade ohne Handle auf fremde Prozesse, Tastenkürzel-Notlösung nie während eines Spiels (Anti-Cheat-Prüfung, E65–E67)
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~AntiCheatSafetyTests|FullyQualifiedName~ProcessImagePathResolverTests|FullyQualifiedName~Hotkey" -MinTests 20
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-07 15:45 exit 0, TESTS_OK 43: Architekturtest (30 Regeln, jede mit Gegenprobe), Pfade ohne Handle (services.exe ohne OpenProcess gelesen), Notlösung gesperrt bei laufendem Bibliotheksspiel, unerkanntem gestarteten Spiel, Nachschalten während des Starts und Vollbild-Anwendung; Gegenproben: ohne jede Korrektur rot. Nach der Härtung erneut grün: PHASE3_SINGLE/TWO/ALREADYON/CRASH_OK, PHASE3_IDLE_OK (0,312 %), PHASE4_RUN_OK, PHASE4_CRASH_OK (0,286 %), PHASE4_UI_OK, PHASE5_PUBLISH_OK, PHASE5_README_OK, TESTS_OK 1114

## Phase 6: Feedback und Erweiterungen nach dem gemeinsamen Test

- [x] G6.1: Bibliothek: Starten-Bereich über der Automatik, Sortierung „Zuletzt gespielt“ (gespeichert), Spalten mit Trenner verschiebbar und Breite gespeichert, Knopf „Steam-Startoption kopieren“ nur bei Steam-Spielen (UI-Automation an einer Testinstanz)
  CHECK: ps tools\gates\phase6-ui.ps1
  EXPECT: PHASE6_UI_OK
  EVIDENCE: 2026-10-07 20:34 exit 0, PHASE6_UI_OK (Testinstanz mit eigener Ablage, UI-Automation nur an deren PID, E72)

- [x] G6.2: Steam-Startoption (`--wrap -- %command%`): HDR ist bestätigt an, bevor der gestartete Prozess existiert; ohne laufende Instanz startet das Spiel trotzdem; der Wrapper hält kein Handle und beendet sich sofort
  CHECK: ps tools\gates\phase6-wrap.ps1
  EXPECT: PHASE6_WRAP_OK
  EVIDENCE: 2026-10-07 20:34 exit 0, PHASE6_WRAP_OK am echten Bildschirm (MAG 341C OLED); zuvor 19:53 abgebrochen mit exit 3, solange der Bildschirm aus war

- [x] G6.3: Neustart-Puffer, Frühwarnung aus Epic-/Steam-Protokollen und Hänger-Hinweis per Unit-Tests belegt (mit Gegenproben)
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~SessionManagerTests|FullyQualifiedName~LauncherSignal" -MinTests 150
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-07 20:40 exit 0, TESTS_OK 200. Filter korrigiert: der alte (`~RestartBuffer|~HangHint`) traf nur 13 Tests, weil die Tests zu Puffer und Hänger-Hinweis in der Klasse SessionManagerTests liegen (Dateiname nicht im Testnamen). Neue Tests der Nachprüfung E81 per Gegenprobe ohne Korrektur rot (3 Runden: 9, 1 und 5 erwartete Fehlschläge)

- [x] G6.4: Name „HDRpilot“ überall sichtbar: Oberfläche, Hinweise, README, Produktname, EXE `HDRpilot.exe`; keine sichtbaren Reste „HdrPilot“
  CHECK: ps tools\gates\phase6-name.ps1
  EXPECT: PHASE6_NAME_OK
  EVIDENCE: 2026-10-07 20:35 exit 0, PHASE6_NAME_OK inkl. ganzer Lizenzhinweise (Skript und ausgelieferte Datei); Gegenprobe: die zuvor ausgelieferte Datei fiel mit 5 Resten durch

- [x] G6.5: Neubau ohne Warnungen, alle Tests grün, alle früheren Gates erneut grün, Auslieferung neu veröffentlicht
  CHECK: ps tools\gates\test.ps1
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-07 20:28 exit 0, BUILD_OK_0_WARNINGS, TESTS_OK 1610 (2 Projekte). Danach am Stand df313c3 erneut grün: PHASE1_STATUS_OK, PHASE1_NEGATIVE_OK, PHASE1_TOGGLE20_OK, PHASE2_SCANNER_TESTS_OK (417), PHASE2_SCAN_OK (646 Spiele, 0 Fehler), PHASE3_SINGLE/TWO/ALREADYON/CRASH_OK, PHASE3_IDLE_OK (0,234 %), PHASE4_RUN_OK, PHASE4_CRASH_OK (0,130 %), PHASE4_UI_OK, PHASE4_RESOURCES_OK, PHASE5_README_OK, G5.4 TESTS_OK 47, PHASE5_PUBLISH_OK (HDRpilot.exe 169,2 MB, SHA256 6B21F76D…, 0 Module aus installierter Runtime)

- [x] G6.6: Installierte Version beim Nutzer ersetzt und neu gestartet; Autostart und Verknüpfungen zeigen weiter auf die EXE
  EVIDENCE: 2026-10-07 20:36 alte Instanz (PID 29612) per --exit in 0,5 s beendet, alte EXE als HdrPilot-vorher.exe.bak gesichert, HDRpilot.exe (Hash gleich der Veröffentlichung) mit README und Lizenzhinweisen nach %LOCALAPPDATA%\Programs\HdrPilot kopiert, mit --minimized gestartet (PID 12844). Protokoll: „HDRpilot 1.0.0.0 startet. Konfiguration …\Roaming\HDRpilot\config.json“, 646 Spiele, Frühwarnung Epic/Steam aktiv, HDR-Liste 20:36:53 abgeglichen (ohne Konto, 4 Anfragen, 99 Spiele geprüft, 14 HDR-fähig). Autostart-Wert und alle 6 Desktop-Verknüpfungen zeigen auf eine vorhandene Datei (Pfad ohne Unterschied in Groß-/Kleinschreibung)

- [x] G6.7: HDR-Liste von PCGamingWiki: Kennzeichen, Sortierung und Filter in der Bibliothek; Abgleich bei jedem Start (höchstens stündlich, abschaltbar), ohne Netz gilt die zwischengespeicherte Liste (keine mitgelieferte Liste, präzisiert in E79); Zuordnung über Steam-ID, GOG-ID und exakten Namen; Konto mit Bot-Passwort optional; Quellenangabe
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~HdrSupport|FullyQualifiedName~HdrList|FullyQualifiedName~Pcgw" -MinTests 140
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-07 20:40 exit 0, TESTS_OK 157 (Core und App, Filter um die App-Tests der Einstellungen erweitert); live beim Nutzer abgeglichen (siehe G6.6). Kontomodus nur mit Attrappe getestet, nicht live (kein Konto eingerichtet)

- [x] G6.8: Tray-Menü bleibt nach echtem Rechtsklick offen und lässt sich bedienen (Fehlermeldung des Nutzers: schloss sich sofort, E82): offen nach 2 s, Symbolwechsel bei offenem Menü, Esc mit Rückgabe an die Taskleiste, Alt+F4/WM_CLOSE an das Hilfsfenster, Klick in ein anderes Programm, Mausklick auf Eintrag, Tastatur; beim Nutzer an der installierten Version nachgemessen
  CHECK: ps tools\gates\phase6-traymenu.ps1
  EXPECT: PHASE6_TRAYMENU_OK
  EVIDENCE: 2026-10-07 21:16 (nach der Gegenprüfung, Stand mit WM_CLOSE-Schutz und Rückgabe an die Taskleiste) exit 0, PHASE6_TRAYMENU_OK, alle Prüfungen aus einem anderen aktiven Programm heraus; Gegenproben 21:15 ohne WM_CLOSE-Schutz und ohne Rückgabe: genau diese 2 Prüfungen rot; 21:16 mit dem ursprünglichen Fehler: 9 Prüfungen rot (Vordergrund zurück beim zuvor aktiven Programm). BUILD_OK_0_WARNINGS, TESTS_OK 1610, PHASE4_RESOURCES_OK, PHASE6_NAME_OK, PHASE5_PUBLISH_OK (HDRpilot.exe SHA256 73FE2EB0…). Eingespielt 21:18, am echten Symbol in der Taskleiste: offen nach 146 ms, nach 3000 ms noch offen, nach Esc Taskleiste vorn Erster Stand (vor der Gegenprüfung): 2026-10-07 20:53 exit 0, PHASE6_TRAYMENU_OK (alle 6 Prüfungen, Symbol der Testinstanz im Überlauf; vier Läufe grün, der letzte mit dem später ergänzten Symbolwechsel). Gegenprobe mit dem alten Code: exit 1 (Menü nach 2 s zu, HDRpilot nicht im Vordergrund). Vorher an der installierten Version gemessen: offen nach 64 ms, zu nach 202 ms, Vordergrund zurück beim zuvor aktiven Programm. Nach dem Einspielen (20:55, HDRpilot.exe SHA256 472830B8…, PHASE5_PUBLISH_OK) am echten Symbol in der Taskleiste: offen nach 147 ms, nach 3000 ms noch offen, Esc schließt. BUILD_OK_0_WARNINGS, TESTS_OK 1610
