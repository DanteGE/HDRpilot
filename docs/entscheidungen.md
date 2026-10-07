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
