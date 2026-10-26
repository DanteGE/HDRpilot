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
  EVIDENCE: 2026-10-07 20:28 exit 0, BUILD_OK_0_WARNINGS, TESTS_OK 1610 (2 Projekte). Danach am Stand fc1fe47 erneut grün: PHASE1_STATUS_OK, PHASE1_NEGATIVE_OK, PHASE1_TOGGLE20_OK, PHASE2_SCANNER_TESTS_OK (417), PHASE2_SCAN_OK (646 Spiele, 0 Fehler), PHASE3_SINGLE/TWO/ALREADYON/CRASH_OK, PHASE3_IDLE_OK (0,234 %), PHASE4_RUN_OK, PHASE4_CRASH_OK (0,130 %), PHASE4_UI_OK, PHASE4_RESOURCES_OK, PHASE5_README_OK, G5.4 TESTS_OK 47, PHASE5_PUBLISH_OK (HDRpilot.exe 169,2 MB, SHA256 6B21F76D…, 0 Module aus installierter Runtime)

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

## Phase 7: Installer (Wunsch des Nutzers: installierbar und aktualisierbar, eigener Setup, E83)

Setup = die normale `HDRpilot.exe` mit angehängtem Paket (README, Lizenzhinweise, Kommandozeile) und Prüfsumme; installiert
für den angemeldeten Benutzer ohne Adminrechte nach `%LOCALAPPDATA%\Programs\HDRpilot`. Einstellungen und Daten liegen
getrennt (`%APPDATA%\HDRpilot`, `%LOCALAPPDATA%\HDRpilot`) und werden vom Setup nie angefasst; beim Entfernen nur auf
ausdrücklichen Wunsch. Einzige Ausnahme: Das Entfernen stellt einen gemerkten HDR-Zustand wieder her und verwirft danach
`session-state.json`. Prüfläufe nutzen ausschließlich `--test-root` (eigene Ordner und eigener Registry-Zweig).

- [x] G7.1: Setup-Logik per Unit-Tests belegt (mit Gegenproben): Paketformat lesen und prüfen (beschädigt, abgeschnitten, fremde Datei), Erstinstallation, Aktualisierung mit Rückfall bei Fehler (alte EXE bleibt lauffähig), gleiche und ältere Version, laufende App beenden ohne Prozess-Handle, Verknüpfungen und Autostart nur für die eigene EXE, Eintrag unter „Installierte Apps“, Entfernen nur der eigenen Dateien und Verknüpfungen, Daten bleiben außer auf Wunsch, Steam-Startoptionen mit HDRpilot werden gefunden, Befehlszeile des Setups
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~Setup" -MinTests 40
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-09 23:05 exit 0, TESTS_OK 266 (Engine: Paketformat, Installieren, Aktualisieren mit Rückfall, gleiche/ältere Version, Stoppen über --exit ohne Handle, Autostart und Verknüpfungen nur eigene, Registry-Eintrag mit DWORDs, Entfernen nur eigener Dateien, Daten nur auf Wunsch, HDR-Rückstellung, Steam-Startoptionen; Oberfläche: Zustände, Fehlerschritte, Hinweise, Befehlszeile, Startweiche, Selbstaufräumen). Gegenproben: Engine 10 + 4, Oberfläche 7 + 8, Prüfungskorrekturen 27 + 7 Mutationen, alle rot erkannt

- [x] G7.2: Ende-zu-Ende mit der echten Setup-Datei in einer Testablage: Erstinstallation (installierte EXE byte-gleich mit der portablen, Startmenü-Verknüpfung, Registry-Eintrag mit Version, Größe, Deinstallations-Befehl), App startet danach; Neuinstallation (gleiche Version) über die laufende App (wird sauber beendet und neu gestartet, Einstellungen unverändert); Aktualisieren einer älteren, nicht laufenden EXE (Stand des Nutzers ohne Eintrag: EXE und Beigaben byte-gleich ersetzt, Autostart bleibt, Eintrag neu, Einstellungen unverändert); Entfernen behält die Einstellungen, mit `--remove-data` sind sie weg; die Kopie zum Entfernen räumt sich selbst weg; der Setup läuft ohne Adminrechte
  CHECK: ps tools\gates\phase7-setup.ps1
  EXPECT: PHASE7_SETUP_OK
  EVIDENCE: 2026-10-09 23:06 exit 0, PHASE7_SETUP_OK (Stand 1.1.0, e888fc8): Erstinstallation, Neuinstallation über die laufende App, Aktualisieren einer älteren nicht laufenden EXE ohne Eintrag (Stand des Nutzers), Optionen, Entfernen mit und ohne Daten, Kopie räumt sich weg; Einstellungen byte-gleich bei allen Schritten ohne App-Start; echter Stand unverändert (8 Angaben)

- [x] G7.3: Oberfläche von Setup und Entfernen (UI-Automation an der Testablage): zeigt Version alt → neu und Zielordner, sagt, dass Einstellungen erhalten bleiben, Optionen Autostart, Desktop-Verknüpfung, danach starten; Fortschritt und Ergebnis; Entfernen fragt nach den Daten (Standard: behalten) und warnt vor Steam-Startoptionen; alle Texte aus Ressourcen, Name HDRpilot
  CHECK: ps tools\gates\phase7-setup-ui.ps1
  EXPECT: PHASE7_SETUP_UI_OK
  EVIDENCE: 2026-10-09 23:07 exit 0, PHASE7_SETUP_UI_OK: Setup-, Aktualisieren- und Entfernen-Fenster per UI-Automation, Steam-Warnung mit Spielname, Ergebnis-Texte; echter Stand unverändert (8 Angaben). Fenster hell/dunkel als Bild geprüft

- [x] G7.4: Auslieferung erzeugt `HDRpilot-Setup-<Version>.exe` neben dem portablen Ordner; Version 1.1.0; README beschreibt Installation, Aktualisierung und Entfernen; Neubau ohne Warnungen, alle Tests und alle früheren Gates grün. Auslieferung und README belegen außer dem Testlauf `tools\gates\phase5-publish.ps1` (PHASE5_PUBLISH_OK) und `tools\gates\phase5-readme.ps1` (PHASE5_README_OK)
  CHECK: ps tools\gates\test.ps1
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-09 23:13 BUILD_OK_0_WARNINGS, TESTS_OK 1877; PHASE5_PUBLISH_OK (HDRpilot-Setup-1.1.0.exe 203.9 MB, SHA256 2CF2394A549F16C3…, EXE-Teil gleich der portablen EXE 92915DA3FB78EA9C…), PHASE5_README_OK (11/11 Gegenproben), PHASE4_RESOURCES_OK, PHASE6_NAME_OK; alle früheren Gates erneut grün: PHASE1_STATUS/NEGATIVE/TOGGLE20_OK, PHASE2_SCANNER_TESTS_OK (417), PHASE2_SCAN_OK, PHASE3_SINGLE/TWO/ALREADYON/CRASH_OK, PHASE3_IDLE_OK (0,260 %), PHASE4_RUN/CRASH/UI_OK, PHASE6_UI_OK, PHASE6_WRAP_OK, PHASE6_TRAYMENU_OK, G5.4 47, G6.3 200, G6.7 157 Tests

- [x] G7.5: Beim Nutzer: bestehende Installation per Setup aktualisiert (nur mit Zustimmung), danach unter „Installierte Apps“ geführt, Einstellungen und Spieleliste unverändert (config.json byte-gleich über den Setup-Lauf ohne App-Start; nach dem ersten Einlesen der neu gestarteten App inhaltlich gleich: alle Einstellungen, Spielezahl, Wahl je Spiel; Zeitstempel des Einlesens ausgenommen), App läuft, Autostart und Verknüpfungen funktionieren
  EVIDENCE: KORRIGIERT 2026-10-09 23:35 (E84): Mein Lauf um 23:15 und alle meine Prüfungen „beim Nutzer“ liefen im MSIX-Container der Claude-App. Echt waren nur die Dateien im Programmordner (HDRpilot.exe 1.1.0 = portable EXE, README, Lizenzhinweise, cli\hdrpilot.exe; die Sicherungsdatei des Nutzers blieb); Eintrag, Autostart und meine Vergleiche von config.json trafen die Container-Kopie. Der Nutzer führte den Setup um 23:17:52 selbst per Doppelklick aus (echt): Protokoll „Stand: Reinstall … Registered = False“ (der Container-Eintrag war von außen unsichtbar), „Erfolg: 1.1.0 -> 1.1.0, gestartet: True“. Von außerhalb des Containers gelesen (WMI, 23:33): Eintrag HKCU\…\Uninstall\HDRpilot (1.1.0, InstallLocation, UninstallString --uninstall, QuietUninstallString), Run-Wert auf die installierte EXE, Startmenü- und Desktop-Verknüpfung, App 1.1.0 läuft echt (PID 9412) mit %APPDATA%\HDRpilot\config.json. Einstellungen: Kein Setup-Lauf hat Einstellungen angefasst (Container-Kopie byte-gleich; echte config.json entstand erst 23:18 durch die App, weil die bisherigen Einstellungen des Nutzers nur in der Container-Kopie lagen). Nutzer entschied: nichts übernehmen.

- [x] G7.6: Neustart-Puffer in den Einstellungen mit Minuten und Sekunden einstellbar (Wunsch des Nutzers nach dem Helldivers-2-Test, E85): 0:00 = aus, höchstens 30:00, Standard 1:00 (vorher 3:00); wird in `restartBufferSeconds` gespeichert und sofort wirksam; per Unit-Tests (mit Gegenproben) und UI-Automation an einer Testinstanz belegt
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~RestartBuffer" -MinTests 15
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-10 00:30 (zusammengeführter Stand 2cd7863, Version 1.2.0) exit 0, TESTS_OK 49 (Einstellungen: Laden/Speichern/Grenzen/HasChanges; Sitzungslogik: Standard 60 s, eingestellter Wert ohne Neustart ab dem nächsten Spielende, 0:00 aus, Puffer nicht länger als die Nachlaufzeit wirkt wie aus); 10 + 4 Mutationen rot erkannt; PHASE4_UI_OK und PHASE4_PERSIST_OK (Testinstanz: 30:05 auf 30:00 begrenzt, 0:45 gespeichert als restartBufferSeconds 45, nach Neustart wieder 0:45); PHASE5_README_OK (Standard in Tabelle und Fließtext geprüft)

- [x] G7.7: Container-Erkennung (E84): Setup und Entfernen brechen ab, wenn Windows ihre Schreibzugriffe in den Container einer anderen App umleitet (Hinweis im Fenster, still mit eigenem Exit-Code, nichts geändert); die App schreibt beim Start eine Warnung ins Protokoll; per Unit-Tests mit nachgebildetem Packages-Ordner (mit Gegenproben) und am echten Container dieser Sitzung belegt
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~Container" -MinTests 8
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-10 00:30 exit 0, TESTS_OK 28 (Erkennung mit nachgebildetem Packages-Ordner, Probe immer gelöscht, Setup/Entfernen/Kopie brechen ab ohne Änderung, Exit-Code 7, Warnung der App einmal je Start, Prüfung bremst den Start nicht); 8 + 2 Mutationen rot. Am echten Container dieser Sitzung (00:39): HDRpilot-Setup-1.2.0.exe --quiet ohne Testablage endete mit Exit-Code 7, Protokoll „Läuft im Container der App Claude_pzs8sxrjxfjjc … Nichts geändert“, installierte EXE unverändert 1.1.0, keine Probe-Reste

- [x] G7.8: Version 1.2.0 beim Nutzer installiert, von außerhalb des Containers (WMI) ausgeführt und von außen geprüft: App läuft echt, Eintrag 1.2.0, Einstellungen des Nutzers unverändert (echte config.json inhaltlich gleich, neuer Puffer-Standard nur wirksam, weil `restartBufferSeconds` dort nicht gesetzt ist)
  EVIDENCE: 2026-10-10 00:40 von außerhalb des Containers (WMI) ausgeführt und geprüft: HDRpilot-Setup-1.2.0.exe (203.9 MB, SHA256 62F3B8F4CD2434F7…) --quiet, Exit 0; vorher 1.1.0 (PID 9412), nachher 1.2.0.0 (PID 2124), echtes Protokoll 

## Phase 8: Nachträge nach Version 1.2.0

- [x] G8.1: Versionsnummer sichtbar (Wunsch des Nutzers): im Kopf der Einstellungen „HDRpilot <Major.Minor.Build>“ und als graue, nicht anklickbare erste Zeile im Tray-Menü unter dem Kopf „Version <Major.Minor.Build>“ (Wunsch des Nutzers 2026-10-12: der Kopf zeigt den Namen schon), aus der Assembly (nie fest im Text; gleiche Zahl wie Setup und „Installierte Apps“); per Unit-Tests (mit Gegenprobe) und UI-Automation an einer Testinstanz belegt
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~AppVersion" -MinTests 4
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-12 auf phase5/release nach dem Zusammenführen von feature/version-display: exit 0, TESTS_OK 13.
    - Auf dem Zweig (b6a21b6): build.ps1 BUILD_OK_0_WARNINGS, test.ps1 TESTS_OK 1950, PHASE4_RESOURCES_OK,
      PHASE6_NAME_OK, PHASE5_README_OK, PHASE4_UI_OK/PHASE4_PERSIST_OK („HDRpilot 1.2.0“ im Kopf der Einstellungen =
      Dateiversion der EXE, frei von den Titelzeilen-Schaltflächen).
    - UI-Automation an der Testinstanz, am echten Desktop mit Zustimmung des Nutzers: phase6-traymenu.ps1 exit 0,
      PHASE6_TRAYMENU_OK. Versionszeile „Version 1.2.0“ = Dateiversion, nicht aktiv; Pfeil nach unten wählt
      „HDR jetzt …“, Pfeil nach oben springt an der Versionszeile vorbei zu „HDRpilot beenden“.
    - Gegenproben: 9 bei der Umsetzung, 2 für den Tastatur-Test, alter Tray-Text im Format → 2 Tests rot.

- [x] G8.2: Ersatz-Symbole ohne Netz (Wunsch des Nutzers, E86): Steam-Spiele zeigen zuerst das offizielle Steam-Symbol (`steam\games\<clienticon>.ico` aus appinfo.vdf, sonst `DisplayIcon` des Eintrags „Steam App <id>“ in der 64-Bit-Ansicht, `.url`-Verknüpfung, librarycache), die EXE nur, wenn das Steam-Symbol fehlt, ein Standardsymbol oder deutlich unschärfer ist; GOG `goggame-<id>.ico`/Galaxy-Symbol, Xbox-Logos aus `MicrosoftGame.config`/`AppxManifest` (mit `.scale-*`), Ubisoft `DisplayIcon`, Epic/EA EXE; bessere EXE-Wahl (Ausschlussliste nur für Symbole, Unreal `*-Shipping.exe`, begrenzte tiefere Suche); bekannte Standardsymbole (EasyAntiCheat, Unreal, Epic-Starter, Godot, Unity, Windows) werden erkannt. Nur lesend, keine Netzzugriffe, keine Last im Leerlauf. Per Unit-Tests mit Gegenproben und Messung an der echten Bibliothek belegt (Ziel: 647 von 647 mit eigenem Symbol, davon mindestens 618 mit ≥ 48 px; die Bibliothek hat inzwischen 651 Spiele, Ziel damit alle mit eigenem Symbol und mindestens 95 % mit ≥ 48 px)
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~Icon" -MinTests 30
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-12 auf phase5/release nach dem Zusammenführen von feature/icons (210c0cd): exit 0, TESTS_OK 275.
    - Messung an einer Kopie der echten Bibliothek (per WMI außerhalb des Containers kopiert, nur lesend eingelesen mit
      `hdrpilot games import --config-dir`, Wahl über den Code der App), Stand f804853:
      - 651 von 651 Spielen mit eigenem Symbol, 0 Standardsymbole, 626 mit ≥ 48 px (96,2 %). Die übrigen 25 haben
        lokal kein schärferes eigenes Symbol: 24 Steam-Spiele mit nur 32 px und Dead Island 2 mit 44 px.
      - Alle 53 Spiele ohne gutes Symbol vorher haben jetzt ein eigenes, 40 davon mit ≥ 48 px.
      - Gears of War: E-Day und The Isle zeigen ihr 256-px-Steam-Symbol statt des EasyAntiCheat-Symbols. Die EXE-Rückfälle
        sind `GoWEDay-Steam.exe` und `TheIsleClient-Win64-Shipping.exe`.
      - Alle Bildtafeln wurden angesehen: kein Anti-Cheat-, Engine- oder Standardsymbol; keine Rückschritte.
      - Wahl kalt im Median 1 ms; warm über `icon-cache.json` ohne jede Ordnersuche.
    - Gesamtlauf auf phase5/release: build.ps1 BUILD_OK_0_WARNINGS, test.ps1 TESTS_OK 2192, PHASE2_SCANNER_TESTS_OK,
      PHASE2_SCAN_OK, PHASE3_IDLE_OK, PHASE4_RESOURCES_OK, PHASE4_UI_OK/PHASE4_PERSIST_OK, PHASE5_README_OK,
      PHASE6_NAME_OK, PHASE6_UI_OK, PHASE7_SETUP_OK, PHASE7_SETUP_UI_OK, PHASE5_PUBLISH_OK (Setup 1.3.0).
    - Drei Prüfrunden mit unabhängigen Prüfern, alle Funde behoben oder begründet abgelehnt (E86).

- [x] G8.3: Eigenes Symbol per Rechtsklick in der Bibliothek („Symbol ändern…“ für Bild- oder EXE-Datei, „Symbol zurücksetzen“), verkleinert im Datenordner gespeichert, hat Vorrang vor allen automatischen Quellen; per Unit-Tests und UI-Automation an einer Testinstanz belegt
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~CustomIcon" -MinTests 6
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-12 auf phase5/release (210c0cd): exit 0, TESTS_OK 29.
    - UI-Automation an einer Testinstanz: `tools\gates\phase8-icons.ps1` exit 0, PHASE8_ICONS_OK. „Symbol zurücksetzen“
      ist zuerst gesperrt. „Symbol ändern…“ mit vorgegebener Datei legt eine 96-px-PNG an und setzt `customIcon`;
      Zurücksetzen entfernt beides.
    - Das Kontextmenü einer Listenzeile ist per Unit-Test an einer echten Zeile belegt. UI-Automation kann ohne echte
      Eingabe nicht rechtsklicken.
    - Verknüpfungen zeigen das eigene Symbol über eine `.ico` neben der PNG im Datenordner.
    - Gegenproben bei jeder Korrekturrunde (insgesamt über 30, alle rot).

## Phase 9: Windows Auto HDR pro Spiel (Wunsch des Nutzers 2026-10-12, E88)

Entscheidungen des Nutzers (2026-10-12):
- **Platz:** eigener Abschnitt „BILD“ im Spiel-Tab, klar getrennt von „HDR automatisch“. In der Liste nur ein Abzeichen
  „AUTO HDR“.
- **Unabhängig:** schaltbar ohne „HDR automatisch“, mit Hinweis, wenn „HDR automatisch“ aus ist.
- **Natives HDR laut PCGamingWiki:** ausgegraut, aber nutzbar, mit Hinweis.
- **Nach dem Livetest (G9.6):**
  - Nur noch der globale Auto-HDR-Schalter, den HDRpilot direkt vor dem Spielstart einschaltet und danach zurückstellt.
    Das ist der Standard, keine experimentelle Notlösung mehr.
  - Der Weg pro Spiel entfällt, weil er unter 26H2 nicht wirkt.
  - Am Schalter steht der Hinweis, dass Auto HDR nur beim Start über HDRpilot wirkt.
  - Beim Einschalten für ein Steam-Spiel bietet HDRpilot die Steam-Startoption an. Keine Meldung nach einem Start ohne
    HDRpilot.
- RTX HDR bleibt zurückgestellt.

- [x] G9.1: Option und Oberfläche: `GameConfig` speichert Auto HDR pro Spiel (alte Konfigurationen laden unverändert); Spiel-Tab mit Abschnitt „BILD“ und Schalter „Windows Auto HDR“ (AutomationId `DetailAutoHdr`) samt Erklärung und dem Hinweis „wirkt nur, wenn das Spiel über HDRpilot startet (Starten, Verknüpfung, Steam-Startoption)“; Hinweis, wenn „HDR automatisch“ aus ist; bei nativem HDR ausgegraut, aber bedienbar, mit Hinweis; Abzeichen „AUTO HDR“ in der Liste (nur Anzeige); alle Texte in resx; per Unit-Tests (mit Gegenproben) und UI-Automation an einer Testinstanz belegt
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~AutoHdr" -MinTests 25
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-13 auf phase5/release nach dem Zusammenführen (1.4.0): exit 0, TESTS_OK 217 (alle AutoHdr-Tests). UI-Automation an der Testinstanz: phase9-autohdr-ui.ps1 exit 0, PHASE9_AUTOHDR_UI_OK (Abschnitt BILD, Schalter, Hinweise, Abzeichen, Konfiguration). Drei unabhängige Prüfer, Korrektur, Gegenprüfung, zwei Nachkorrekturen; Gegenproben bei jeder Runde rot.

- [ ] G9.2: Pro-Spiel-Weg (`AutoHDREnable=2097` je EXE)
  EVIDENCE: Livetest 2026-10-12 (G9.6): wirkt unter 26H2 bei global ausgeschaltetem Auto HDR nicht.
  ABANDON: G9.2 Nach dem Livetest vom Nutzer gestrichen („Nur global vor dem Start“). Der Code dafür wird entfernt,
    HDRpilot schreibt keine Werte pro EXE. Falls ein späteres Windows-Update den Weg wieder beachtet: Entwurf und Code
    stehen in `feature/autohdr-core` (e9193f7).

- [x] G9.3: Globaler Schalter vor dem Start (Standard, keine Einstellung): Startet ein Spiel mit Auto HDR über einen Weg, den HDRpilot vorher kennt (Starten/„Mit HDR starten“, Verknüpfung, Steam-Startoption, Frühwarnung von Steam/Epic), setzt HDRpilot vor dem Spielprozess Windows-HDR und im Wert `DirectXUserGlobalSettings` das Token `AutoHDREnable=1` (andere Tokens, Reihenfolge und Abschluss bleiben) und stellt nach der Sitzung (Neustart-Puffer beachtet) den vorherigen Zustand her, nach einem Absturz von HDRpilot beim nächsten Start; hat der Nutzer den Schalter inzwischen selbst geändert, bleibt seine Wahl; nie, solange kein solches Spiel startet oder läuft; per Unit-Tests gegen eine Testwurzel (nie die echten Schlüssel) mit Gegenproben belegt
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~AutoHdrGlobal" -MinTests 10
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-13: exit 0, TESTS_OK 83. phase9-autohdr-ui.ps1: „Mit HDR starten“ startet ein harmloses Prüfprogramm, das beim eigenen Start AutoHDREnable=1 in der Testwurzel sah; danach exakt zurückgestellt, der echte Schlüssel blieb byteweise unverändert. Wiederholversuch nach Fehlern nur noch, solange der Start aussteht (Gegenprobe rot).

- [x] G9.4: Rückbau: Das Entfernen über den Setup stellt einen von HDRpilot eingeschalteten globalen Schalter zurück (Testwurzel, nie die echte Registry); keine Werte pro EXE; per Unit-Tests und Setup-Gate belegt
  CHECK: ps tools\gates\phase7-setup.ps1
  EXPECT: PHASE7_SETUP_OK
  EVIDENCE: 2026-10-13: exit 0, PHASE7_SETUP_OK und PHASE7_SETUP_UI_OK (Schritt „Auto HDR zurückstellen“). Ein gesperrter, schon erledigter Merker bleibt beim Löschen der Daten samt Kennzeichen liegen (Test mit Gegenprobe).

- [x] G9.5: Gesamtstand: build.ps1 ohne Warnungen, alle Tests und alle Gates ohne echte HDR-Umschaltung grün, Anti-Cheat-Architekturtest unverändert grün; README (Abschnitt Auto HDR, Abgrenzung zu „HDR automatisch“, Ergebnis des Livetests, Tipp: Einstellungsfenster der Game Bar neu öffnen) und E88 passen zu den resx-Texten; unabhängige Prüfung mit Gegenprüfung
  CHECK: ps tools\gates\phase5-readme.ps1
  EXPECT: PHASE5_README_OK
  EVIDENCE: 2026-10-13 auf phase5/release, Version 1.4.0, alle exit 0: BUILD_OK_0_WARNINGS, TESTS_OK 2410, PHASE9_AUTOHDR_UI_OK, PHASE2_SCANNER_TESTS_OK, PHASE2_SCAN_OK, PHASE3_IDLE_OK, PHASE4_RESOURCES_OK, PHASE5_README_OK, PHASE6_NAME_OK, PHASE4_UI_OK/PHASE4_PERSIST_OK, PHASE6_UI_OK, PHASE8_ICONS_OK, PHASE7_SETUP_OK, PHASE7_SETUP_UI_OK; AntiCheat-Tests im Gesamtlauf. phase5-publish.ps1: 24 Prüfungen ok, nur „hdr status“ exit 3 (Monitor aus, keine aktiven Bildschirme; umgebungsbedingt, bei 1.3.0 mit Monitor grün).

- [x] G9.6: Versuch am echten System mit dem Nutzer (Einzelspieler-Spiel ohne Anti-Cheat, DirectX 11/12, ohne eigenes HDR)
  EVIDENCE: 2026-10-12, gemeinsam mit dem Nutzer, Werte per WMI außerhalb des Containers gesetzt, Sicherung
    `%TEMP%\hdrp-ah\backup-directx.reg`, danach alles zurückgestellt (Export identisch mit der Sicherung, 0 Unterschiede).
    - Positivprobe TUNIC (Unity, DirectX 11): Auto HDR global an, HDR vor dem Start an → Regler „HDR-Intensität
      anpassen“ in der Game Bar bedienbar. Der Registry-Wert ist derselbe Schalter wie in den Windows-Einstellungen.
    - Global aus, nur `AutoHDREnable=2097` für `Tunic.exe` → Regler grau: Pro-Spiel-Weg wirkt unter 26H2 nicht.
    - Global erst eingeschaltet, während TUNIC lief → Windows zeigt „Eingeschaltet“, der Regler bleibt grau: Die
      Einstellung gilt erst beim nächsten Start.
    - HDR erst nach dem Spielstart eingeschaltet (global an) → Regler grau.
    - The Plucky Squire und Aliens: Dark Descent zeigten den Regler grau. Entweder sind sie nicht freigegeben, oder das
      Game-Bar-Fenster zeigte einen alten Stand: Der Nutzer stellte fest, dass man das Einstellungsfenster ganz
      schließen und neu öffnen muss.
    - Sekiro hat eigenes HDR (PCGamingWiki-Status „Native“ stimmte).

- [x] G9.7: Steam-Startoption anbieten: Schaltet der Nutzer Auto HDR für ein Steam-Spiel ein, ohne dass die HDRpilot-Startoption gesetzt ist, bietet HDRpilot an, sie zu setzen (vorhandene Funktion, gleiche Rückfragen und Bedingungen wie dort, z. B. Steam beendet); Ablehnen ändert nichts; per Unit-Tests belegt
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~AutoHdrSteam" -MinTests 4
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-13: exit 0, TESTS_OK 12. Umsetzung: Angebot „Steam-Startoption kopieren“ mit der vorhandenen Funktion (Zwischenablage und Anleitung). HDRpilot schreibt nie in Steams Dateien; eine Funktion zum Setzen gibt es nicht (Abweichung von der ursprünglichen Beschreibung „setzen“, im Abschlussbericht genannt).

- [ ] G9.8: Abschlusstest am echten System mit dem Nutzer (nach Installation mit Zustimmung): Start über HDRpilot und über die Steam-Startoption → Auto HDR aktiv (Regler bedienbar); nach dem Spiel ist der globale Schalter wieder aus; der Tray-Eintrag „Auto HDR“ schaltet den Windows-Schalter sichtbar um (Windows-Einstellungen → HDR)
  EVIDENCE: pending

- [x] G9.9: Tray-Eintrag „Auto HDR“ (Wunsch des Nutzers 2026-10-12): im Tray-Menü unter „HDR jetzt …“ ein Eintrag mit Häkchen, der den globalen Auto-HDR-Schalter von Windows anzeigt und umschaltet (nur das Token `AutoHDREnable`, andere Tokens bleiben); gilt für die nächsten Spielstarts, auch ohne HDRpilot; eine Änderung dort ist die Wahl des Nutzers und wird nach einer Auto-HDR-Sitzung nicht zurückgestellt; Pfeiltasten und die übrigen Einträge funktionieren weiter; Text und Hinweis in resx; per Unit-Tests (mit Gegenproben, Testwurzel statt echter Registry) belegt, `phase6-traymenu.ps1` kennt den Eintrag (läuft nur am echten Desktop mit Zustimmung)
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~AutoHdrTray" -MinTests 5
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-13: exit 0, TESTS_OK 11. phase6-traymenu.ps1 kennt den Eintrag (nur bearbeitet; der Lauf mit echter Maus folgt mit dem Nutzer, G9.8).

## Phase 10: Neugestaltung „Showcase Hero“ (Wunsch des Nutzers 2026-10-14/15, E89)

Spezifikation: `docs/design/showcase-hero/README.md` (Entscheidungen, Bildquellen, Effekte mit WPF-Primitiven), lauffähiger
Prototyp v4 unter `docs/design/showcase-hero/prototyp/`. Die vier Anmerkungen des Nutzers zum Prototyp (Raster im Vollbild,
Reihenfolge Liste/Raster/Regal, Nit-Skala bis zur echten HDR-Spitze, Anordnung der Seite „Darstellung“) gelten als Teil der
Kriterien. Installation beim Nutzer nur mit ausdrücklicher Zustimmung.

- [x] G10.1: Farben und Themen: Duoton und Tritonus mit 8 Vorgaben und eigenen Farben, Hell/Dunkel/Automatisch (Automatisch folgt Windows), gespeichert in der Konfiguration (alte Konfigurationen laden unverändert); alle Farb-Ressourcen der Oberfläche werden aus der Palette abgeleitet (Akzent, Atmosphäre, Glanz; Mischfarben vorab berechnet), Text auf Akzentflächen hält den Kontrast automatisch (mindestens 4,5:1 für Text, 3:1 für Bedienelemente); per Unit-Tests mit Gegenproben belegt
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~Palette|FullyQualifiedName~Theme" -MinTests 20
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-21 auf phase5/release nach der Zusammenführung (8f3a6a7, Stand gleich feature/library-toolbar 0adc290): exit 0, TESTS_OK 121.

- [x] G10.2: Tray-Symbol und Fenstersymbol in den Palettenfarben, Zustände HDR an, HDR aus und Automatik pausiert, in den Größen 16 bis 256 px, neu erzeugt nur bei Farb- oder Taskleisten-Themawechsel (nie im Leerlauf); per Unit-Tests mit Gegenproben belegt
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~TrayIcon|FullyQualifiedName~WindowIcon" -MinTests 8
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, TESTS_OK 30.

- [x] G10.3: Spielbilder aus lokalen Quellen, nur lesend:
  - Quellen: Steam `librarycache` (Titelbild, Logo mit deutscher Variante bevorzugt, Cover, unscharfes Titelbild), GOG-Galaxy-Cache, Ubisoft-Cache, Xbox-Paketbilder, Symbol aus der EXE (Epic, EA).
  - Erzeugte Ersatzbilder: Titelbild, Cover und Monogramm, gespeichert unter `%LocalAppData%\HDRpilot\art`.
  - Verschmierte Steam-Cover werden erkannt und durch einen Ausschnitt aus dem Titelbild ersetzt; Logos werden auf ihren sichtbaren Teil zugeschnitten.
  - Je Bild einmal berechnet und zwischengespeichert: Umgebungsfarben und eine helle „HDR“-Fassung.
  - Neue Bilder erscheinen von selbst (Beobachtung der Cache-Ordner); eigene Bilder per Rechtsklick „Bild wählen…“ bleiben dauerhaft.
  - Belegt per Unit-Tests mit Gegenproben und einer Messung an einer Kopie der echten Bibliothek (Abdeckung je Launcher wie im Prototyp: Steam Titelbild ≥ 631 von 636).
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~Artwork" -MinTests 30
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, TESTS_OK 123. Messung an einer Kopie der Bibliothek (2026-10-16, `--artwork-report`, Ergebnis in `docs/design/showcase-hero/umsetzung-core.md`): Steam Titelbild 631 von 636 Spielen.

- [x] G10.4: SteamGridDB optional: standardmäßig aus, nur mit eigenem API-Schlüssel (verschlüsselt gespeichert wie das PCGamingWiki-Bot-Passwort), füllt automatisch nur fehlende Bilder, klare Anzeige des Netzwerkzugriffs; ist das automatische Füllen aus, gibt es nachweislich keinen automatischen Netzwerkzugriff dafür (seit E93 sucht „Bild wählen…“ mit gespeichertem Schlüssel nur auf Klick, siehe G14.2), ohne Schlüssel gar keinen; per Unit-Tests mit Attrappe (nie das echte Netz in Tests) belegt
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~SteamGridDb" -MinTests 8
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, TESTS_OK 40.

- [x] G10.5: Bibliothek im neuen Design:
  - Ansichten in der Reihenfolge **Liste, Raster, Regal**; Liste ist der Standard, die Wahl wird gemerkt.
  - Liste: virtualisiert, sortierbar, „HDR automatisch“ je Zeile, Titelbereich des gewählten Spiels oben.
  - Höhe des Titelbereichs (Wunsch des Nutzers 2026-10-16):
    - per Ziehgriff am unteren Rand einstellbar, zwischen Liste und Titelbereich;
    - gemerkt in der Konfiguration, Doppelklick setzt auf den Standard zurück;
    - Standard etwa ein Drittel der Fensterhöhe;
    - kein automatisches Schrumpfen beim Scrollen mehr;
    - mit Tastatur bedienbar;
    - der Griff liegt genau auf der Grenze zwischen Titelbild und Liste (Wunsch 2026-10-16).
  - Werkzeugleiste über der Liste (Wünsche des Nutzers 2026-10-16):
    - über der Leiste (bis zur Unterkante des Titelbilds) so viel Luft wie darunter (Liste 10 px, Raster 22 px bis zur ersten Kachelreihe);
    - Filter „Automatik“, „Neu“ und „HDR-fähig“ so klein wie „Neu einlesen“ (40×40, eindeutige Symbole), Zähler-Plakette innerhalb des Knopfs, aktiver Zustand nicht nur über die Farbe, Erklärung nach 1 s Mouse-Over;
    - Ansicht-Umschaltung und „Spiel hinzufügen“ nur als Symbol mit Tooltip;
    - einzeilig hat Vorrang: Suche, Quelle und Sortierung geben stufenlos nach, dann zeigt die Sortierung nur ihr Symbol; erst darunter zwei Zeilen, getrennt an den Gruppengrenzen, mit sauberem Übergang; die Anpassung an schmale Fenster ist fließend und optisch sauber (kein Nachhinken, gleiche Abstände, bündige Kanten);
    - Aus- und Einblenden nach der abgenommenen Vorschau „Fließleiste mit Lichtnaht“ (`docs/design/showcase-hero/prototyp/leiste.html`, Wunsch des Nutzers 2026-10-17): Lasche rechts, ihre Unterkante 8 px über der Leiste (wie der Abstand der Knöpfe); eingeklappt eine Kapsel auf der Bildkante mit aktiven Filtern, Suchbegriff, Anzahl und Zurücksetzen; gefiltert die Lichtnaht; Strg+F schaltet um (eingeklappt: dauerhaft ausklappen, Cursor in die Suche; seit 2026-10-22, vorher Strg+Umschalt+F zum Umschalten), Strg+F1 ebenso, Alt+A/N/H schalten die Filter; Animation nach Effektstufe (Voll, Reduziert, Aus); der Zustand wird gemerkt;
    - der Inhalt des Titelbereichs sitzt 22 px über der Bildkante (Platz für Lasche und Kapsel).
  - Auswahl beim Filtern (Entscheidung des Nutzers 2026-10-19, ersetzt die Regel aus Phase 4): fällt das gewählte Spiel durch Suche oder Filter aus der Liste, springen Titelbereich und Details leise auf den ersten Treffer (nicht als „gesehen“ markiert, nichts gespeichert); ohne Treffer kein Spiel und der Hinweis „Keine Spiele für diese Auswahl“; nach dem Aufheben des Filters kehrt die vorherige Auswahl zurück, wenn sie wieder sichtbar ist.
  - Raster: skaliert bei jeder Fenstergröße sauber, auch im Vollbild bei 3440×1440: Spaltenzahl und Kachelgröße folgen der Breite, nichts gestreckt oder abgeschnitten.
  - Regal: Titelbild und Kachelreihe.
  - Detailbereich mit allen bisherigen Funktionen (STARTEN, AUTOMATIK, BILD, EXE-DATEIEN, Bilder); alle bisherigen Funktionen und Tastenkürzel bleiben.
  - Belegt per UI-Automation an einer Testinstanz und Bildern bei 1280×800, 1920×1080 und 3440×1440 in hell und dunkel.
  CHECK: ps tools\gates\phase10-ui.ps1
  EXPECT: PHASE10_UI_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, PHASE10_UI_OK (306 Prüfungen an einer Testinstanz mit eigener Ablage, 54 Bilder: je 18 bei 1280×800, 1920×1080 und 3440×1440, hell und dunkel; Auswahl beim Filtern und Detailbereich ohne Treffer eingeschlossen).

- [x] G10.6: Effekte wie im Prototyp: langsamer Zoom und Tiefenwirkung des Titelbilds, Überblendung beim Spielwechsel, Umgebungsfarbe, HDR-Moment (Lichtstreifen, helle Fassung, Partikel höchstens 2,5 s, leuchtender Rand). Stufen Voll, Reduziert und Aus; die Windows-Option „Animationseffekte“ wird beachtet. Dauer-Effekte laufen mit höchstens 30 fps und pausieren bei minimiertem oder inaktivem Fenster und während eines Spiels; bei geschlossenem Fenster läuft nichts. Ohne BlurEffect, DropShadowEffect und Mischmodi; per Unit-Tests mit Gegenproben belegt (Steuerung der Effekte)
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~Effect|FullyQualifiedName~Motion" -MinTests 15
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, TESTS_OK 108.

- [x] G10.7: Echte Bildschirmwerte über DXGI `IDXGIOutput6::GetDesc1` (Spitze, Vollbild, Minimum, Farbtiefe, HDR aktiv), gelesen nur bei HDR- oder Bildschirmwechsel; das „Maximum“ jeder Nit-Skala ist die höchste Nit-Zahl des Bildschirms bei aktivem HDR (nie ein fester Wert wie 1000); per Unit-Tests mit Attrappe belegt
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~DisplayLuminance" -MinTests 6
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, TESTS_OK 15.

- [x] G10.8: Einstellungen im neuen Design mit Seite „Darstellung“:
  - Inhalt: Farben Duoton/Tritonus mit Vorgaben und eigenem Farbwähler, Thema, Effektstufe, Bilder (lokal, SteamGridDB optional), Vorschau von Tray-Symbol und Tray-Menü.
  - Anordnung: Raster mit Höchstbreite je Bereich und mehreren Spalten bei breiten Fenstern, damit im Vollbild nichts in die Länge gezogen wird (Anmerkung des Nutzers).
  - Belegt per UI-Automation und Bildern bei 1280×800, 1920×1080 und 3440×1440.
  CHECK: ps tools\gates\phase10-settings-ui.ps1
  EXPECT: PHASE10_SETTINGS_UI_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, PHASE10_SETTINGS_UI_OK (28 Prüfungen: alle 7 Bereiche bei 1280×800, 1920×1080 und maximiert gefunden, innerhalb des Fensters und in der Höchstbreite, ab 1920 px Farbschema und Live-Vorschau nebeneinander; 24 Bilder in genauer Pixelgröße).

- [ ] G10.9: Leistung, gemessen am echten Desktop mit eingeschaltetem Bildschirm:
  - Bei geschlossenem Fenster bleibt der Leerlauf wie bisher (`phase3-idle.ps1`).
  - Offenes Fenster in Ruhe: Grafikkartenlast der Testinstanz im Mittel unter 5 % und nie über 20 %.
  - HDR-Moment: unter 30 % Spitze.
  - Minimiert oder inaktiv: keine Bildaufrufe.
  - Ohne aktiven Bildschirm meldet das Gate UMGEBUNG und gilt nicht als bestanden.
  CHECK: ps tools\gates\phase10-gpu.ps1
  EXPECT: PHASE10_GPU_OK
  EVIDENCE: pending. Auf dem Stand 1.5.0 nicht gemessen: `phase10-gpu.ps1` braucht den echten Desktop mit eingeschaltetem Bildschirm und läuft nur zusammen mit dem Nutzer. Früherer Lauf 2026-10-16 auf feature/showcase (vor Werkzeugleiste, Phase 11 und Quiver): PHASE10_GPU_OK, Liste in Ruhe 1,2 % GPU, HDR-Moment Spitze rund 24 % GPU, minimiert 0,2 % CPU.

- [x] G10.10: Gesamtstand: build.ps1 ohne Warnungen, alle Tests und alle Gates ohne echte HDR-Umschaltung grün (auch die bisherigen UI-Gates nach Anpassung), Anti-Cheat-Architekturtest grün, README und E89 passen zu den resx-Texten, unabhängige Prüfung mit Gegenprüfung
  CHECK: ps tools\gates\phase5-readme.ps1
  EXPECT: PHASE5_README_OK
  EVIDENCE: 2026-10-21 auf phase5/release nach der Zusammenführung (8f3a6a7), nacheinander, alle exit 0: BUILD_OK_0_WARNINGS, TESTS_OK 3409, Anti-Cheat-Architekturtest mit dem Filter von G5.4 TESTS_OK 47, die bisherigen UI-Gates nach Anpassung (PHASE4_UI_OK/PHASE4_PERSIST_OK, PHASE6_UI_OK, PHASE8_ICONS_OK, PHASE9_AUTOHDR_UI_OK) sowie PHASE10_UI_OK, PHASE10_SETTINGS_UI_OK, PHASE11_UI_OK, PHASE12_QUIVER_UI_OK, PHASE3_IDLE_OK, PHASE4_RESOURCES_OK, PHASE6_NAME_OK, PHASE5_README_OK (README stimmt mit dem Quelltext überein, 0 Probleme; Gegenproben 15 von 15 und 3 von 3). Unabhängige Prüfungen mit Gegenprüfung: Showcase Hero (`docs/stand.md`), Abschlussprüfung von feature/library-toolbar mit Korrekturen c5d0a1f, 8f47e30, 948102a und Prüfung von 1.5.0 (0adc290).

- [ ] G10.11: Abnahme durch den Nutzer am echten System (Aussehen, Effekte, Bedienung, Vollbild bei 3440×1440)
  EVIDENCE: pending. Abnahme durch den Nutzer am echten System steht aus (Vorführung, siehe `docs/stand.md`).

## Phase 11: Nicht installierte Spiele (Wunsch des Nutzers 2026-10-18, E90)

Scope: Bei nicht installierten Launcher-Spielen lassen sich die HDRpilot-Daten des Spiels entfernen (Kontextmenü und ganz unten im Detailbereich), und eine Einstellung blendet nicht installierte Spiele aus. Entscheidungen des Nutzers 2026-10-19: Entfernt werden Einstellungen, eigene Bilder und Symbole; HDRpilot-Verknüpfungen nur per Häkchen in der Rückfrage. Ausgeblendete Spiele zählen nicht mit (Kopf, Quellen, Kapsel), „HDR automatisch“ wirkt weiter; die Karte „Bibliothek“ in den Einstellungen zeigt die Anzahl und bietet „Einstellungen aller nicht installierten Spiele entfernen…“ mit Namensliste und Rückfrage. Spiel-, Launcher- und Steam-Dateien werden nie angefasst. Plan: `docs/uebergabe/wuensche-2026-10-18-plan.md`.

- [x] G11.1: Kern und Bibliothek, per Unit-Tests mit Gegenproben belegt:
  - Entfernt wird nur bei nicht installiert, nicht manuell und ohne laufende oder vorbereitete Sitzung; geprüft wird erst beim Schreiben (findet ein Einlesen das Spiel dazwischen wieder, bleibt alles).
  - Gelöscht werden nur der Eintrag, das eigene Symbol (PNG), der Eintrag im Symbol-Cache, die eigenen Bilder und, nur mit Häkchen, die exakt passenden HDRpilot-Verknüpfungen (Ziel HDRpilot.exe, genau `--run "<Schlüssel>"`) samt deren .ico; ein Attrappen-Dateisystem belegt, dass sonst nichts gelöscht wird.
  - Nach einer Neuinstallation erscheint das Spiel als neu, „HDR automatisch“ ist aus.
  - `hideUninstalledGames` fehlt: aus; Rundreise der Konfiguration stimmt, alte Konfigurationen laden unverändert, keine andere Einstellung geht verloren.
  - Ausblenden: Zähler, Quellen, Kapsel und Leerzustand stimmen, „HDR automatisch“ ordnet ausgeblendete Spiele weiter zu, nach dem Entfernen wird der Nachbar gewählt.
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~Uninstalled|FullyQualifiedName~RemoveSettings|FullyQualifiedName~LibraryCleanup" -MinTests 25
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-21 auf phase5/release nach der Zusammenführung (8f3a6a7): exit 0, TESTS_OK 109.

- [x] G11.2: Oberfläche an einer Testinstanz (Testablage mit 2 nicht installierten Launcher-Spielen und 1 manuellem):
  - Menüeintrag und Knopf erscheinen nur bei nicht installierten Launcher-Spielen; die Rückfrage nennt, was verloren geht (etwa „HDR automatisch ist an“), warnt bei fehlendem Laufwerk und hat den Fokus auf „Abbrechen“.
  - „Abbrechen“ lässt die Testablage byteweise gleich, „Entfernen“ nimmt genau ein Spiel heraus.
  - Ausblenden und Speichern senkt die Zählung um 2; das Sammel-Entfernen nennt beide Namen und entfernt nur sie.
  - Bilder bei 1280×800, 1920×1080 und 3440×1440, hell und dunkel.
  CHECK: ps tools\gates\phase11-uninstalled-ui.ps1
  EXPECT: PHASE11_UI_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, PHASE11_UI_OK (99 Prüfungen an einer Testinstanz; Testablage mit einem Steam-Spiel auf einem nicht verbundenen Laufwerk, einem Epic-Spiel, dessen Ordner nur noch Reste ohne EXE enthält, und einem eigenen Spiel; „Abbrechen“ lässt die Testablage byteweise gleich; 24 Bilder bei 1280×800, 1920×1080 und 3440×1440, hell und dunkel).

- [x] G11.3: Verträglichkeit an einer Kopie der echten Ablage (nur lesend kopiert): lädt ohne Warnung, zählt die nicht installierten Spiele; nach Umschalten und Speichern unterscheidet sich die Datei nur in `hideUninstalledGames`; die echte Ablage ist vorher und nachher gleich (Hash).
  CHECK: ps tools\gates\phase11-migration.ps1
  EXPECT: PHASE11_MIGRATION_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, PHASE11_MIGRATION_OK (Kopie der echten Ablage, nur lesend kopiert: lädt ohne Ladefehler, 5 nicht installierte Spiele gezählt, nichts als „defekt“ gesichert; nach Umschalten und Speichern unterscheidet sich die Datei nur in `settings.hideUninstalledGames`, keine andere Einstellung verloren; echte Ablage vorher und nachher gleich, SHA-256).

- [x] G11.4: Gesamtstand: build.ps1 ohne Warnungen, alle Tests und UI-Gates grün, README und E90 passen zu den Texten.
  CHECK: ps tools\gates\phase5-readme.ps1
  EXPECT: PHASE5_README_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, PHASE5_README_OK (0 Probleme); dazu BUILD_OK_0_WARNINGS, TESTS_OK 3409 und alle UI-Gates grün (siehe G10.10).

- [ ] G11.5: Abnahme durch den Nutzer am echten System
  EVIDENCE: pending. Abnahme durch den Nutzer am echten System steht aus (Vorführung, siehe `docs/stand.md`).

## Phase 12: Quiver Launcher (Wunsch des Nutzers 2026-10-18, E91)

Scope: Spiele des Quiver Launchers (portabel, MIT, bei dem Nutzer v3.5.1 mit „Black“) werden als eigene Quelle „Quiver“ erkannt, mit Cover aus Quivers lokalem Cache, und mit „HDR automatisch“ versehen. Entscheidungen des Nutzers 2026-10-19: eigene Quelle, Quiver-Ordner selbst finden bzw. merken, sobald Quiver läuft; „Mit HDR starten“ startet die Spiel-EXE direkt wie Quiver selbst; kein PCGamingWiki-Abgleich für Quiver-Spiele. Nur lesend an Quiver-Dateien, kein Netzwerk, keine GPL-Abhängigkeiten. Unbekannte Quellen in der Konfiguration werden künftig tolerant gelesen. Plan: `docs/uebergabe/wuensche-2026-10-18-plan.md`.

- [x] G12.1: Scanner mit Beispieldateien (erfundene Pfade): Ordner gefunden über Velopack-Protokoll, Suchordner, Downloads und gelernten Ordner; ein fremdes Velopack-Programm ist kein Treffer; `AppsPath` und `installPath` beachtet; `selected_executable.txt` gewinnt, zeigt sie aus dem Spielordner heraus, wird sie ignoriert; mehrere EXE ohne Wahl ergeben keinen Start-Befehl; `folderName` mit `..` wird abgelehnt; Cover-Cache-Name stimmt; nichts geschrieben, kein Netzwerk.
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~Quiver" -MinTests 30
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-21 auf phase5/release nach der Zusammenführung (8f3a6a7): exit 0, TESTS_OK 171.

- [x] G12.2: Pflichttests je Scanner (fehlender Launcher ergibt leer ohne Fehler, defekte und unvollständige Daten).
  CHECK: ps tools\gates\phase2-scanner-tests.ps1
  EXPECT: PHASE2_SCANNER_TESTS_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, TESTS_OK 574, PHASE2_SCANNER_TESTS_OK.

- [x] G12.3: Verträglichkeit: eine Konfiguration mit unbekannter Quelle lädt, alle übrigen Spiele und Einstellungen bleiben, nichts wird als „defekt“ gesichert; Gegenprobe mit dem bisherigen Konverter schlägt fehl.
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~UnknownSource" -MinTests 3
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, TESTS_OK 13.

- [x] G12.4: Echtes System, nur lesend: `hdrpilot scan --source Quiver` liefert „Black“ mit der Spiel-EXE ohne Fehler; die Dateiliste des Quiver-Ordners (Größen, Zeitstempel) ist vorher und nachher gleich.
  CHECK: ps tools\gates\phase12-quiver-scan.ps1
  EXPECT: PHASE12_QUIVER_SCAN_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, PHASE12_QUIVER_SCAN_OK (in der Sitzung und außerhalb des Containers je 1 Quiver-Spiel „Black“ mit Start-EXE, Arbeitsordner und Cover aus Quivers Zwischenspeicher, ohne Fehler, eigene Ablage leer; Dateiliste des Quiver-Ordners vorher und nachher gleich, 304 Einträge, 0 Unterschiede).

- [x] G12.5: Testinstanz: Quelle „Quiver“ in Filter und Kapsel, Cover aus dem Cache der Beispieldateien, „Mit HDR starten“ startet ein harmloses Prüfprogramm direkt mit seinem Ordner als Arbeitsordner und die Sitzung erkennt es; Bilder bei drei Größen, hell und dunkel.
  CHECK: ps tools\gates\phase12-quiver-ui.ps1
  EXPECT: PHASE12_QUIVER_UI_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, PHASE12_QUIVER_UI_OK (66 Prüfungen an einer Testinstanz mit erfundenem Quiver-Ordner und harmlosem Prüfprogramm, ohne echten Bildschirm; 36 Bilder bei 1280×800, 1920×1080 und 3440×1440, hell und dunkel).

- [x] G12.6: Gesamtstand: build.ps1 ohne Warnungen, alle Tests und UI-Gates grün, README, E91 und `docs/scanner/quiver.md` passen zu den Texten.
  CHECK: ps tools\gates\phase5-readme.ps1
  EXPECT: PHASE5_README_OK
  EVIDENCE: 2026-10-21 (phase5/release, 8f3a6a7): exit 0, PHASE5_README_OK (0 Probleme); dazu BUILD_OK_0_WARNINGS, TESTS_OK 3409 und alle UI-Gates grün (siehe G10.10).

- [ ] G12.7: Livetest mit „Black“ nur mit dem Nutzer: aus Quiver gestartet schaltet HDR ein, nach dem Ende wird zurückgestellt; danach Abnahme durch den Nutzer.
  EVIDENCE: pending. Livetest mit „Black“ und Abnahme nur zusammen mit dem Nutzer; steht aus.

## Phase 13: Veröffentlichung auf GitHub (Vorgaben des Nutzers 2026-10-20, E92)

Scope: HDRpilot 1.5.0 im öffentlichen Repository `DanteGE/HDRpilot`. Lizenz PolyForm Strict 1.0.0, Name überall „Dante_GE“ mit der Noreply-Adresse von GitHub, keine lokalen Pfade, keine Spuren von Claude-Sitzungen, keine echten Installationsordner und Spielelisten des Prüfrechners, auch nicht in alten Versionen. Die Commits des Nutzers auf GitHub (README, `funding.yml`) bleiben erhalten.

- [x] G13.1: Version 1.5.0 (App, Kommandozeile, Setup-Datei `HDRpilot-Setup-1.5.0.exe`), in den Programmdaten `Authors` Dante_GE und Copyright „© Dante_GE“; Firma, Produkt und Herausgeber bleiben „HDRpilot“.
  CHECK: ps tools\gates\phase5-readme.ps1
  EXPECT: PHASE5_README_OK
  EVIDENCE: 2026-10-20 auf feature/library-toolbar: phase5-readme.ps1 exit 0, PHASE5_README_OK (Setup-Datei HDRpilot-Setup-1.5.0.exe aus Directory.Build.props). Gebaute HDRpilot.dll und hdrpilot.dll (FileVersionInfo): Produkt HDRpilot 1.5.0, Datei 1.5.0.0, Firma HDRpilot, Copyright „© Dante_GE“.

- [x] G13.2: `LICENSE` (PolyForm Strict 1.0.0 mit „Required Notice: Copyright Dante_GE“) unverändert; README oben mit der englischen Einleitung aus dem GitHub-Repository, Abschnitt „Lizenz“ (Fremdbestandteile, PCGamingWiki-Testdaten unter CC BY-NC-SA 3.0) und Hinweis auf GitHub Releases; `.gitignore` schließt `.claude/`, `.vscode/`, `.idea/` und `*.binlog` aus; `LICENSE` liegt dem Ordner ohne Installation und dem Paket der Setup-Datei bei.
  CHECK: ps tools\gates\phase5-readme.ps1
  EXPECT: PHASE5_README_OK
  EVIDENCE: 2026-10-20: LICENSE byte-gleich mit der vorbereiteten Fassung (SHA-256 7bf1fa5b…); die ersten zwei README-Zeilen byte-gleich mit README.md aus origin/main; phase5-readme.ps1 exit 0, PHASE5_README_OK (0 Probleme, Gegenproben 15 von 15 und 3 von 3); git check-ignore trifft .claude/, .vscode/, .idea/ und *.binlog. Nach der Prüfung (Fund 3, 2026-10-21): `tools\publish.ps1` ausgeführt (nur Bau, kein Start): Ordner enthält genau cli\hdrpilot.exe, HDRpilot.exe, LICENSE, README.md, THIRD-PARTY-NOTICES.txt, LICENSE byte-gleich (SHA-256 7bf1fa5b…); HDRpilot-Setup-1.5.0.exe eigens gelesen: Abschluss stimmig, EXE-Teil gleich der portablen EXE, Paket README.md, LICENSE, THIRD-PARTY-NOTICES.txt, cli/hdrpilot.exe, alle byte-gleich mit dem Ordner. Unit-Tests: Paket nennt LICENSE, Installation legt sie ab, Aktualisierung von einer Setup-Datei ohne LICENSE (vor 1.5.0) legt sie dazu, Entfernen löscht sie; 3 Mutanten (LICENSE fehlt im Paket, fehlt in den installierten Dateien, Link in der Kennung beim Abruf von SteamGridDB) erkannt. phase5-publish.ps1 und Phase7Common.ps1 angepasst, aber nicht ausgeführt (phase5-publish ruft die Kommandozeile ohne eigene Ablage auf, phase7-* installieren in eine Testablage). Antwort des Nutzers 2026-10-21 (LICENSE in Setup und portablem Paket): phase7-setup.ps1 prüft jetzt die LICENSE (im Repository mit dem Hinweis, byte-gleich in der portablen Auslieferung und im Paket der Setup-Datei, nach (a) und (e) im Programmordner, (e) legt sie zu einer Installation ohne LICENSE dazu, nach (d) und (f) entfernt). 2026-10-22 auf `release/publish-paths` (16a4910, Code gleich dem Merge-Commit f3da056 in phase5/release), niedrige Priorität: phase7-setup.ps1 exit 0, PHASE7_SETUP_OK (113 ok, 0 FEHLER; nur Testablage und Test-Zweig, beide danach entfernt; echter Stand und UserGpuPreferences unverändert).

- [x] G13.3: Keine lokalen Pfade (auch URL-kodiert), Claude-Ordner, privaten Links, frühere Mail-Adresse, echten Installationsordner (mit Laufwerk; unverwechselbare Ordnernamen auch ohne), Kennungen oder Stichproben aus den entfernten Spielelisten in den verfolgten Dateien; Positivkontrolle bei jedem Lauf. Seit Runde 2 der Prüfung vor der Veröffentlichung stehen die gesuchten Werte nur in einer privaten Liste außerhalb des Repositorys (`-PrivateList`, Standard: `hdrpilot-publish` im gemeinsamen Git-Ordner); das Gate findet auch deren Hashwerte und schlägt ohne die Liste fehl. Seit Runde 3 auch einen echten Spielordner am Anfang eines relativen Pfads (`<Spiel>/bin/<exe>.exe`), Zahlen mit Tausendertrennzeichen und Zeitpunkte auf die Minute.
  CHECK: ps tools\gates\publish-check.ps1
  EXPECT: PUBLISH_CHECK_OK
  EVIDENCE: 2026-10-20: publish-check.ps1 exit 0, PUBLISH_CHECK_OK (721 verfolgte Dateien, 0 Treffer; Positivkontrolle 14 von 14, 14 erlaubte Schreibweisen ohne Treffer, UTF-16LE erkannt). Gegenproben außerhalb des Repositorys: eine Datei mit den echten Werten in allen 21 Zeilen erkannt, ebenso die alten Fassungen von product-with-games.db (beide Pfade), velopack_QuiverLauncher.log (10 Zeilen) und umgebung.md (3 Kennungen); -Rev 8f47e30 (vor der Bereinigung) 1648 Treffer, -Rev HEAD 0. Die erste Gesamtrunde fand einen Treffer im Skript selbst (Muster des Scratchpad-Pfads), behoben und erneut grün. Nach der Prüfung (Fund 1, 2026-10-21): neue Gruppen „echter Ordnername“ (ohne Laufwerk, auch UTF-16) und „Titel aus entfernter Spieleliste“, Versionsnummern als Kennung, Text zusätzlich URL-dekodiert; publish-check.ps1 exit 0, PUBLISH_CHECK_OK (721 verfolgte Dateien, 0 Treffer; Positivkontrolle 18 von 18 bei 17 eingeschleusten Werten, 18 erlaubte Schreibweisen ohne Treffer, UTF-16LE mit Profilpfad und Ordnername). Gegenprobe mit den Fundstellen der Prüfung (außerhalb des Repositorys): die alte GamingRoot_D.bin, die alten umgebung.md, xbox.md und XboxFormatTests.cs sowie URL-kodierte Profilpfade (Kontrollname und echter Name) jetzt erkannt, 23 Treffer; vorher 5. Nach der Zusammenführung mit `release/publish-paths` (2026-10-22, Merge-Commit f3da056: erfundene Spielordner an Standardorten samt Kennungen, erfundene Werte in den Steam-Beispieldateien der Werkzeuge, E92) und den Nachträgen in `docs/stand.md` und `GATES.md`: publish-check.ps1 exit 0, PUBLISH_CHECK_OK (721 verfolgte Dateien, 0 Treffer; Positivkontrolle 18 von 18 bei 17 eingeschleusten Werten, 18 erlaubte Schreibweisen ohne Treffer, UTF-16LE erkannt), für den Arbeitsstand und mit `-Rev HEAD`.

- [x] G13.4: Gesamtstand: build.ps1 ohne Warnungen, alle Tests und alle Gates ohne echte HDR-Umschaltung grün.
  CHECK: ps tools\gates\test.ps1
  EXPECT: TESTS_OK
  EVIDENCE: 2026-10-20 auf feature/library-toolbar nach dem Zusammenführen von phase5/release, nacheinander, alle exit 0: BUILD_OK_0_WARNINGS, TESTS_OK 3405 (erster Lauf nur mit dem bekannten zeitabhängigen Fall in EffectReviewTests rot, Wiederholung grün), PHASE2_SCANNER_TESTS_OK (574), PHASE12_QUIVER_SCAN_OK, PHASE12_QUIVER_UI_OK, PHASE11_UI_OK, PHASE11_MIGRATION_OK, PHASE10_UI_OK, PHASE10_SETTINGS_UI_OK, PHASE4_UI_OK/PHASE4_PERSIST_OK, PHASE6_UI_OK, PHASE8_ICONS_OK, PHASE9_AUTOHDR_UI_OK, PHASE3_IDLE_OK, PHASE4_RESOURCES_OK, PHASE5_README_OK, PHASE6_NAME_OK, PUBLISH_CHECK_OK. Nicht ausgeführt, weil sie echtes HDR schalten oder Eingaben senden: phase1-*, phase3-standin, phase4-run, phase4-crash, phase6-wrap, phase6-traymenu, phase10-gpu; ebenso nicht phase2-scan, phase5-publish und phase7-*. Nach den Funden der Prüfung (2026-10-21, Stand des Commits „Release 1.5.0: Funde der Prüfung“), nacheinander, alle exit 0 im ersten Lauf, ohne Wiederholung: BUILD_OK_0_WARNINGS, TESTS_OK 3409, PHASE2_SCANNER_TESTS_OK (574), PHASE12_QUIVER_SCAN_OK, PHASE12_QUIVER_UI_OK, PHASE11_UI_OK, PHASE11_MIGRATION_OK, PHASE10_UI_OK, PHASE10_SETTINGS_UI_OK, PHASE4_UI_OK/PHASE4_PERSIST_OK, PHASE6_UI_OK, PHASE8_ICONS_OK, PHASE9_AUTOHDR_UI_OK, PHASE3_IDLE_OK, PHASE4_RESOURCES_OK, PHASE5_README_OK, PHASE6_NAME_OK (samt ausgelieferter Datei), PUBLISH_CHECK_OK; nach den letzten Änderungen am Skript publish-check.ps1 und an GATES.md publish-check.ps1 und phase5-readme.ps1 erneut grün. Nicht ausgeführt wie oben. Nach der Zusammenführung in phase5/release (2026-10-21, Merge-Commit 8f3a6a7, Baum gleich 0adc290) auf dem Stand mit den Nachweisen G10 bis G12, nacheinander, alle exit 0: BUILD_OK_0_WARNINGS, TESTS_OK 3409, die Testfilter der Gates G5.4, G10.1–G10.4, G10.6, G10.7, G11.1, G12.1 und G12.3, PHASE2_SCANNER_TESTS_OK (574), PHASE12_QUIVER_SCAN_OK, PHASE12_QUIVER_UI_OK, PHASE11_UI_OK, PHASE11_MIGRATION_OK, PHASE10_UI_OK, PHASE10_SETTINGS_UI_OK, PHASE4_UI_OK/PHASE4_PERSIST_OK, PHASE6_UI_OK, PHASE8_ICONS_OK, PHASE9_AUTOHDR_UI_OK, PHASE3_IDLE_OK, PHASE4_RESOURCES_OK, PHASE5_README_OK, PHASE6_NAME_OK, PUBLISH_CHECK_OK. Nicht ausgeführt wie oben. Nach der Zusammenführung mit `release/publish-paths` in phase5/release (2026-10-22, Merge-Commit f3da056, ohne Konflikte), niedrige Priorität, nacheinander, alle exit 0 im ersten Lauf: BUILD_OK_0_WARNINGS, TESTS_OK 3409, PUBLISH_CHECK_OK. Vorher auf `release/publish-paths` (16a4910, Code gleich): BUILD_OK_0_WARNINGS, TESTS_OK 3409, PHASE2_SCANNER_TESTS_OK (574), PHASE7_SETUP_OK, PUBLISH_CHECK_OK. In dieser Runde nicht ausgeführt: die UI-Gates, phase5-readme und phase6-name (rufen die Kommandozeile ohne eigene Ablage auf) und die oben genannten.

- [ ] G13.5: Geschichte aller veröffentlichten Zweige mit denselben Regeln umgeschrieben (Autor und Committer Dante_GE mit Noreply-Adresse; alte Fassungen der bereinigten Dateien ausdrücklich behandelt, `messung-vorher/` aus allen Versionen entfernt, siehe `docs/stand.md`), Restesuche über alle Commits, Nachrichten, Pfadnamen und Blob-Versionen ohne Treffer (im umgeschriebenen Repository nach `git gc --prune=now`, für jeden Zweig, der hochgeladen wird), mit `origin/main` zusammengeführt (ohne Überschreiben) und hochgeladen.
  CHECK: ps tools\gates\publish-check.ps1 -History * -PrivateList <private Liste> (im umgeschriebenen Repository: alle Refs, also die hochzuladenden Zweige und origin/main; die private Liste liegt nur beim unbereinigten Repository)
  EXPECT: PUBLISH_CHECK_OK
  EVIDENCE: pending. Vorlauf 2026-10-20 (Werkzeug der Prüfung getestet, Umschreiben steht aus): Positivkontrolle der Geschichte 6 von 6 (Autor, Committer, Profilpfad und Titel nur in alten Versionen, Ordnername im Pfadnamen, Claude-Ordner in einer Nachricht), 7 von 7 Mutanten der Prüfung von den Positivkontrollen erkannt; unbereinigtes Repository (main, phase5/release, feature/library-toolbar: 216 Commits, 2 204 Blob-Versionen, 740 Pfadnamen) 3 427 Treffer, davon 404 bei Autor oder Committer (202 Commits); Probelauf des Umschreibwerkzeugs (dry2, `-History *`: 204 Commits, 2 070 Blob-Versionen) 2 261 Treffer und keine bei Autor oder Committer: 2 120 echte Installationsordner, 68 Titel aus entfernten Listen, 40 Ordnernamen ohne Laufwerk, 17 Kennungen, 16 gekürzte Verweise auf den Notizordner. Im unbereinigten Repository trifft außerdem die Nachricht des Commits „Bereinigung: Profilpfade, Claude-Ordner, Name und Kennungen des Rechners“ (gekürzter Verweis auf den Notizordner); beim Umschreiben anpassen. Vorbereitet 2026-10-22: Zuordnung der echten Spielordner an Standardorten zu erfundenen für alle Versionen (nicht im Repository, siehe `docs/stand.md`), mit Reihenfolge der Regeln und Umbenennungen alter Beispieldateien; im Werkzeug noch nachzurüsten, Umschreiben steht aus.

## Phase 14: SteamGridDB-Suche in „Bild wählen…“ (Wunsch des Nutzers 2026-10-22, E93)

Scope: Im Dialog „Bild wählen…“ je Reiter (Titelbild, Logo, Cover) ein Abschnitt „STEAMGRIDDB“ unter den lokalen Bildern: Spielzuordnung „Spiel: <Name> ▾“ mit Spielwahl, **Bei SteamGridDB suchen** nur auf Klick, 24 Vorschauen je Seite mit Autor, **Mehr laden**, nur unbewegte Bilder ohne NSFW-/Humor-Markierung; das volle Bild lädt erst **Übernehmen** und speichert es wie eine eigene Datei. Ohne Schlüssel Hinweis und **Zu den Einstellungen**; mit Schlüssel auch bei ausgeschaltetem automatischem Füllen. Dazu: eine Kachelform je Bildart für lokale und Online-Bilder, Fenster nie höher als die Arbeitsfläche und ohne Schrumpfen, hoher Kontrast, Schalter der Einstellungen in eigener Zeile, ein gemeinsamer Abstand aller Anfragen und Zeitgrenzen für jede Übertragung. Kein Netz in Tests und Prüfläufen (Attrappen, erzeugte Testdaten), keine Spielbilder in Testdaten und Bildschirmfotos. Spezifikation: siehe E93. Als eigenes Test-Update nach den UI-Wünschen vom 2026-10-22.

- [ ] G14.1: Schnittstelle im Kern: Seiten mit Vorschau, Autor, Größe und Filtern (static, nsfw/humor/epilepsy false, Cover 600×900/342×482/660×930, page ohne limit), Ende erst mit leerer Seite (404 ab Seite 1 = Ende), Trefferliste und games/id mit Jahr, bereinigte Suchbegriffe und Namen, erlaubte Rechner auch für Vorschauen und nach Weiterleitungen, Vorschau nur aus thumb, HTTP 401/403 eines Bildes kein abgelehnter Schlüssel, getrennte Budgets, Vorschauen ohne 1-s-Abstand, gemeinsamer exklusiver Abstand der Schnittstelle über Threads, Zeitgrenzen und sofortiger Abbruch hängender Übertragungen, Größengrenzen 4 MB/32 MB/15 MB, Fehlerarten, Schlüssel nie in Ausnahmen; mit Gegenproben
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~SteamGridDbSearchClient" -MinTests 30
  EXPECT: TESTS_OK
  EVIDENCE: pending.

- [ ] G14.2: Dienst, Sitzung und Bilddaten: ohne Schlüssel keine Sitzung und kein Handler, mit Schlüssel kein Handler vor dem ersten Klick; Suche auch bei ausgeschaltetem Schalter, automatischer Lauf weiter nur eingeschaltet; Zuordnung (App-ID, gemerkt mit Name und Jahr, gleicher Name, mehrdeutig, Trefferliste), geteilte Auflösung ohne gemerkte Fehlschläge, ein Abstand für Lauf, Prüfen und Sitzungen, höchstens 3 Vorschauen gleichzeitig, Warteschlange abbrechbar, Grenzen je Dialog, 60 s Pause nach 429, Schlüssel entfernen und Beenden brechen sofort ab, Zuordnung nur ohne Steam-App-ID gemerkt und danach neu gefüllt, kein Bild der alten Zuordnung gespeichert, keine Autoren/Begriffe/Namen/Adressen/Schlüssel im Protokoll; Bilder aus Daten speichern wie Dateien; Testdaten-Lieferant nur mit --config-dir, ohne echten Handler; mit Gegenproben
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~SteamGridDbSearchSession|FullyQualifiedName~SteamGridDbFixture|FullyQualifiedName~ArtworkCustomDataTests" -MinTests 37
  EXPECT: TESTS_OK
  EVIDENCE: pending.

- [ ] G14.3: ViewModel des Dialogs: Zustände und Texte aus resx (ohne Schlüssel, nicht lesbar, bereit, lädt, langsam, Ergebnisse, Ende, Grenze je Reiter, kein Treffer, keine Bilder, Schlüssel abgelehnt, zu viele Anfragen, offline, Zeitüberschreitung, Grenze erreicht), je Reiter eigener Klick, Mehr laden bis zur leeren Seite, Speicher verborgener Reiter freigegeben, Spielwahl, eine Wahl je Reiter über lokale und Online-Liste, Spielwechsel mit Hinweis, Herunterladen beim Übernehmen noch im Dialog mit Meldung im betroffenen Reiter, Abbrechen ohne Absturz, Speichern als eigenes Bild, Zurücksetzen, Abbrechen schreibt nichts, Schließen gibt frei und lässt die lokale Wahl bedienbar, Link zu den Einstellungen, Ansage nur bei Zustandswechseln; mit Gegenproben
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~ArtworkPickerSearchTests" -MinTests 40
  EXPECT: TESTS_OK
  EVIDENCE: pending.

- [ ] G14.4: Fenster: nie höher als die Arbeitsfläche des Bildschirms der Bibliothek (1280×800 bei 100 und 125 %, 1920×1080, 3440×1440), wächst nur und zur Mitte hin, nur die Mitte scrollt (auch per Mausrad über Kacheln), die Bildlaufleiste kostet keine Breite, Kacheln füllen die 830 DIP genau (lokal und online), Logos ganz auf Schachbrett, Tab-Reihenfolge, Fokus nach Suche und Mehr laden nur aus dem Abschnitt heraus, Esc und Enter in der Spielwahl, Kennungen, Muster und Namen für Bildschirmleser, Live-Ansage, hoher Kontrast mit lesbarem Text, Effektstufen Aus/Reduziert/Voll, helles und dunkles Thema, Übernehmen nach Schließen ohne Fehler
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~ArtworkPickerSearchWindowTests" -MinTests 17
  EXPECT: TESTS_OK
  EVIDENCE: pending.

- [ ] G14.5: Einstellungen: Schlüsselfeld und „Prüfen“ auch bei ausgeschaltetem Schalter, „Prüfen“ speichert dann ohne Lauf, Schalter in eigener Zeile mit sichtbarer Beschriftung, Statuszeile „Automatisches Laden aus · Suche in „Bild wählen…“ nur auf Klick“ bzw. letzter Zugriff (Lauf oder Suche), Prüfmeldung bleibt nicht hängen, abgelehnter Schlüssel aus einer Suche rot, Sprung zum Schlüsselfeld
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~SettingsAppearancePageTests|FullyQualifiedName~SettingsSteamGridDbWindowTests" -MinTests 20
  EXPECT: TESTS_OK
  EVIDENCE: pending.

- [ ] G14.6: Bisherige Zusagen von G10.4 bleiben (aus: kein automatischer Netzwerkzugriff, ohne Schlüssel gar keiner), zusammen mit den neuen SteamGridDB-Tests
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~SteamGridDb" -MinTests 90
  EXPECT: TESTS_OK
  EVIDENCE: pending.

- [ ] G14.7: Testinstanz mit erzeugten SteamGridDB-Antworten (kein Netz, nur UI-Automation): Schlüssel bei ausgeschaltetem Schalter prüfen und speichern ohne Lauf; keine Anfrage vor dem Klick; 24/48/70 Ergebnisse mit Autor, Ende mit leerer Seite; nur www./cdn2.steamgriddb.com, Schlüssel nur an die Schnittstelle; Übernehmen speichert ein 600×900-Cover als eigenes Bild, Zurücksetzen entfernt es, Abbrechen schreibt nichts; Spielwahl, gemerkte Zuordnung nur ohne Steam-App-ID; Fehlerzustände samt Zeitüberschreitung; Link zum Schlüsselfeld; keine Vorschau-Threads nach dem Schließen; Bildschirmfotos aller Zustände hell und dunkel, auch klein und in hohem Kontrast, dazu die Einstellungskarte
  CHECK: ps tools\gates\phase14-sgdb-ui.ps1
  EXPECT: PHASE14_SGDB_UI_OK
  EVIDENCE: pending.

- [ ] G14.8: Einstellungen per UI-Automation weiter grün, mit der angepassten Prüfung „Prüfen auch bei ausgeschaltetem Schalter bedienbar“
  CHECK: ps tools\gates\phase10-settings-ui.ps1
  EXPECT: PHASE10_SETTINGS_UI_OK
  EVIDENCE: pending.

- [ ] G14.9: Bibliothek und Dialog ohne Schlüssel weiter grün (Abbrechen schreibt nichts, kein Netz)
  CHECK: ps tools\gates\phase10-ui.ps1
  EXPECT: PHASE10_UI_OK
  EVIDENCE: pending.

- [ ] G14.10: Bilder geprüft: Bildschirmfotos aus G14.7 bei 1280×800, 1920×1080 und 3440×1440, hell und dunkel, zugeschnitten und vermessen (Kachelgrößen, Abstände, rechte Kante bei 830 DIP, Luft um die Trennlinie); Vergleich an den Nutzer geschickt, bevor das Test-Update installiert wird
  EVIDENCE: pending.

- [ ] G14.11: Gesamtstand: build.ps1 ohne Warnungen, alle Tests und alle Gates ohne echte HDR-Umschaltung grün (nacheinander, gedrosselt)
  CHECK: ps tools\gates\test.ps1
  EXPECT: TESTS_OK
  EVIDENCE: pending.

- [ ] G14.12: README, E93 und GATES passen zu den resx-Texten (fett gesetzte Bezeichnungen, Einstellungstabelle)
  CHECK: ps tools\gates\phase5-readme.ps1
  EXPECT: PHASE5_README_OK
  EVIDENCE: pending.

- [ ] G14.13: Keine lokalen Pfade, Namen oder echten Spieldaten in neuen Dateien (Testdaten-Lieferant, Tests, Doku)
  CHECK: ps tools\gates\publish-check.ps1
  EXPECT: PUBLISH_CHECK_OK
  EVIDENCE: pending.

- [ ] G14.14: Livetest nur mit dem Nutzer und seinem eigenen Schlüssel (der Nutzer klickt; Protokollstufe vorher auf „Ausführlich (Debug)“, danach zurück): Rechner der Vorschauen (das Debug-Protokoll nennt nur Rechnernamen), Seite 1 unterscheidet sich von Seite 0, ob eine Seite mit weniger als 50 Einträgen immer die letzte ist, was eine Seite hinter dem Ende liefert, ob `limit` in der Antwort steht, was der CDN für eine fehlende Vorschau antwortet (403 oder 404), Logo-Vorschauen behalten ihre Durchsichtigkeit, Namen mit „+“, „/“, Apostroph und Umlauten finden das Spiel, 342×482-Cover sehen zugeschnitten gut aus, WebP wird gezeigt, nie HTTP 429 bei normaler Nutzung
  EVIDENCE: pending. Nur zusammen mit dem Nutzer.

- [ ] G14.15: Abnahme durch den Nutzer am echten System (Aussehen bei 1280×800, 1920×1080 und 3440×1440, hell und dunkel, Bedienung mit Tastatur)
  EVIDENCE: pending.
