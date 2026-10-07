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

- [ ] G3.1: Sitzungslogik vollständig mit Attrappen (IHdrController, IProcessWatcher) und steuerbarer Uhr getestet: Zustand merken/wiederherstellen, mehrere Spiele, Nachlaufzeit, Wiederanlauf nach Absturz, nicht HDR-fähiger Bildschirm
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~Session" -MinTests 25
  EXPECT: TESTS_OK
  EVIDENCE: pending

- [ ] G3.2: Stellvertreter-EXE in einem als Spiel eingetragenen Ordner: Start schaltet HDR innerhalb von 2 s ein, Ende nach der Nachlaufzeit wieder aus
  CHECK: ps tools\gates\phase3-standin.ps1 -Scenario single
  EXPECT: PHASE3_SINGLE_OK
  EVIDENCE: pending

- [ ] G3.3: Zwei gleichzeitige Stellvertreter: erst nach dem Ende des zweiten wird zurückgestellt
  CHECK: ps tools\gates\phase3-standin.ps1 -Scenario two
  EXPECT: PHASE3_TWO_OK
  EVIDENCE: pending

- [ ] G3.4: War HDR vorher an, bleibt es an
  CHECK: ps tools\gates\phase3-standin.ps1 -Scenario alreadyon
  EXPECT: PHASE3_ALREADYON_OK
  EVIDENCE: pending

- [ ] G3.5: App bei laufendem Stellvertreter hart beenden, Stellvertreter schließen, App neu starten: HDR wird zurückgestellt
  CHECK: ps tools\gates\phase3-standin.ps1 -Scenario crash
  EXPECT: PHASE3_CRASH_OK
  EVIDENCE: pending

- [ ] G3.6: Überwachung im Leerlauf sparsam: CPU-Zeit und Handle-Zahl über 60 s gemessen
  CHECK: ps tools\gates\phase3-idle.ps1
  EXPECT: PHASE3_IDLE_OK
  EVIDENCE: pending

## Phase 4: Oberfläche und Tray

- [ ] G4.1: Alle Punkte aus „Oberfläche“ bedienbar (UI-Automation-Rauchtest über Tray-Menü, Hauptfenster, Spiel-Details, Einstellungen)
  CHECK: ps tools\gates\phase4-ui.ps1
  EXPECT: PHASE4_UI_OK
  EVIDENCE: pending

- [ ] G4.2: Einstellungen überleben einen Neustart
  CHECK: ps tools\gates\phase4-persist.ps1
  EXPECT: PHASE4_PERSIST_OK
  EVIDENCE: pending

- [ ] G4.3: Nur eine Instanz; zweiter Aufruf mit --run reicht an die laufende Instanz weiter
  CHECK: ps tools\gates\phase4-single-instance.ps1
  EXPECT: PHASE4_SINGLE_INSTANCE_OK
  EVIDENCE: pending

- [ ] G4.4: Alle UI-Texte in Ressourcendateien (keine festen Texte in XAML/ViewModels)
  CHECK: ps tools\gates\phase4-resources.ps1
  EXPECT: PHASE4_RESOURCES_OK
  EVIDENCE: pending

- [ ] G4.5: Start über die App und über die erzeugte Verknüpfung funktioniert für je ein Spiel pro installiertem Launcher; Protokoll belegt mit Zeitstempeln, dass HDR vor dem Prozessstart aktiv war (gemeinsamer Test mit dem Nutzer)
  EVIDENCE: pending

## Phase 5: Auslieferung

- [ ] G5.1: Einzelne, eigenständige EXE veröffentlicht und lauffähig (ohne installierte .NET-Runtime-Abhängigkeit)
  CHECK: ps tools\gates\phase5-publish.ps1
  EXPECT: PHASE5_PUBLISH_OK
  EVIDENCE: pending

- [ ] G5.2: README mit Einrichtung, Funktionsweise, bekannten Grenzen und Fehlersuche
  CHECK: ps tools\gates\phase5-readme.ps1
  EXPECT: PHASE5_README_OK
  EVIDENCE: pending

- [ ] G5.3: Rückfrage beim Nutzer zu WMI-Überwachung, Neustart bei verspätetem HDR, Steam-Hülle, englischer Oberfläche
  EVIDENCE: pending
