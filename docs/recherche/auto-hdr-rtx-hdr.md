# Auto HDR und RTX HDR pro Spiel (Recherche, zurückgestellt)

> **Status: zurückgestellt.** Am 2026-10-10 hat der Nutzer entschieden, das Thema vorerst nicht zu verfolgen, aber die
> Erkenntnisse aufzuheben. Es wurde nichts umgesetzt und auf dem PC keine Einstellung geändert. Wer das Thema wieder
> aufnimmt, beginnt mit den Versuchen in Abschnitt 7. Vorher prüfen, ob die Angaben noch stimmen: Windows-Build,
> NVIDIA-Treiber, NVIDIA App und die Quellen können sich seitdem geändert haben.
>
> Ausgangsfrage des Nutzers: Kann HDRpilot für Spiele ohne natives HDR die Auto-HDR-Funktion von Windows oder NVIDIA
> einschalten, je nachdem, was die besseren Ergebnisse liefert?
>
> Grenzen, die bei einer Wiederaufnahme gelten:
> - Versuche nur außerhalb des Claude-Containers (E84).
> - Nur Einzelspieler-Spiele ohne Anti-Cheat, die der Nutzer selbst startet. Nie mit echten Konten testen.
> - Kein Code aus RHI (GPL-3.0) oder anderen GPL-Projekten. NVAPI-Interop eigenständig aus `nvapi.h` (MIT).
> - HDRpilot bleibt ohne Adminrechte (`asInvoker`).

**Stand:** 2026-10-10. Das ist reine Recherche. Auf diesem PC wurde keine Einstellung geändert.

**Kennzeichnung im Text:**
- **[belegt]**: Primärquelle oder lokale Lesemessung.
- **[unsicher]**: nur Community-Quelle oder Rückschluss.
- **[widersprochen]**: Es gibt Belege dagegen.
- **[ungetestet]**: Das kann nur ein Versuch auf diesem PC klären.

Die Quellen stehen als [Q#] am Ende. „L“ heißt lokale Lesemessung auf diesem PC vom 2026-10-10: `reg query` außerhalb des Claude-Containers, `icacls` und eine DRS-Leseprobe ohne Set- oder Save-Aufrufe.

**Korrekturen zur Umgebung (L):**
- Der PC läuft mit **Build 26300.9550 (26H2)**. `docs/umgebung.md` ist seitdem nachgetragen.
- Der Bildschirm ist der MAG 341C OLED mit 3440×1440 bei **144 Hz**. Das genaue Modell ist nicht bestätigt.

---

## 1. Kurzantwort: teilweise ja

**Das geht:**
- Beide Funktionen lassen sich technisch pro Spiel schalten.
- Es braucht dafür voraussichtlich keine Adminrechte.
- Der Spielprozess wird nicht angefasst.

**So funktioniert es:**
- **Windows Auto HDR** über einen undokumentierten Wert pro EXE in HKCU [Q1][Q6].
- **NVIDIA RTX HDR** über das Treiberprofil (DRS). Die dafür nötigen NVAPI-Funktionen sind öffentlich (MIT-Lizenz) [Q14]. Die Einstellungs-IDs für RTX HDR sind dagegen undokumentiert [Q15].

**Was noch offen ist:**
1. **Auto HDR:** „Global aus, pro Spiel an“ steht nur im Microsoft-Blog von 2023 [Q1]. Die heutigen Einstellungen und die Support-Seite bieten pro Spiel nur ein **Abwählen** an („Don't use Auto HDR“) [Q2]. Lokal gibt es dazu `AutoHDROptOutApplicable=1` (L). Dieser Punkt ist **[widersprochen/ungetestet]**.
2. **RTX HDR:** Niemand hat gezeigt, dass die NVIDIA App Profilwerte übernimmt, die ein fremdes Programm geschrieben hat **[ungetestet]**.
3. **„Was besser aussieht“:** Das kann HDRpilot nicht messen, ohne sich in das Spiel einzuklinken. Es geht nur über Regeln (API, PCGamingWiki, Hardware, Anti-Cheat, Vorliebe). Der Nutzer muss das Ergebnis mit eigenen Augen bestätigen.

**Empfehlung:**
- Erst die Versuche aus Abschnitt 7 machen (E1/E2 für Auto HDR, E7–E9 für RTX HDR). Danach in zwei Stufen bauen.
- Für die Bildqualität ist meistens RTX HDR besser. Auto HDR kostet dafür fast keine Leistung.

---

## 2. Windows Auto HDR

**Wo der Wert steht:**
- Schlüssel `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`. Pro EXE gibt es einen REG_SZ, der Wertname ist der volle Pfad. Inhalt: eine Folge `Key=Value;`.
- `AutoHDREnable=2097;` heißt an, `2096;` heißt aus. Das wurde unter **26200** beobachtet [Q6]. Laut [Q6] trägt nur Bit 0 den Schalter. Die höheren Bits sind unbekannt.
- Unter 26300 ist das ungeprüft. Der lokale Wert für Alan Wake 2 (`2097`) kann noch vor dem Upgrade entstanden sein.
- „Zurücksetzen“ löscht laut [Q6] den Wert. So macht es auch die Einstellungen-App.
- Der globale Schalter ist `DirectXUserGlobalSettings` mit `AutoHDREnable=0/1`. Lokal steht dort `AutoHDREnable=0;SwapEffectUpgradeEnable=1;` (L).

**Was die Ziel-EXE erfüllen muss:**
- Der Wert muss an der EXE hängen, die **tatsächlich rendert**. Ein Wert am Launcher bewirkt nichts [Q6, Notiz vom 2026-09-08].
- Windows-HDR muss an sein [Q33].
- Unterstützte APIs:
  - DX11/DX12 [Q3][Q33].
  - DX10–12 laut Blog 2023 [Q1].
  - DX9, OpenGL und Vulkan werden nativ nicht unterstützt.
- Spiele im Fenster oder randlosen Fenster mit DX10/11 brauchen „Optimierungen für Spiele im Fenstermodus“ (`SwapEffectUpgradeEnable=1`) [Q5][Q2]. Das ist lokal an (L).
- Auto HDR wirkt nur bei Spielen, die Microsoft als „eligible“ führt [Q1][Q3]. Ob ein Spiel dazugehört, lässt sich vorab nicht abfragen.

**Wann eine Änderung wirkt:** erst beim nächsten Spielstart („Restart your game or app“) [Q2][Q4]. Der Wert muss also **vor** dem Prozessstart stehen.

**Erzwingen bei nicht freigegebenen Spielen:**
- Weg: Unterschlüssel `HKCU\Software\Microsoft\Direct3D\ApplicationN` mit `Name` und `D3DBehaviors=BufferUpgradeOverride=1` [Q7][Q9].
- Das ist undokumentiert und die Ergebnisse sind gemischt.
- Ob der Name nur der Dateiname oder der volle Pfad sein muss: **[ungetestet]**.
- Möglicherweise muss dafür auch der globale Schalter an sein [Q8] **[unsicher]**.
- In HDRpilot höchstens als experimentelle Option, die man selbst einschalten muss.

**Rückbau und Rechte:**
- Rein in HKCU, pro EXE, ohne Admin. Original sichern und wiederherstellen ist einfach.
- Der Parser muss mit Ausreißern klarkommen:
  - `GraphicsFeaturesNotificationConfig` ist ein DWORD (L).
  - `ScopeX.exe` hat als Wert nur `"0"` (L).
  - `LosslessScaling.exe` soll einen Wert mit eingebettetem NUL haben. Das hat ein Recherche-Agent gemeldet, es ist nicht gegengeprüft.

**Risiken:**
- Das Format ist undokumentiert und kann sich mit einem Windows-Update ändern.
- Ein 24H2-Fehler (Hänger, falsche Farben) wurde im Februar 2025 behoben [Q10].
- Anti-Cheat: Es gibt keine Herstelleraussage. Bekannt ist nur ein Einzelbericht: Fortnite unter EAC ohne Auto HDR [Q11, 2022].

**Erfolg prüfen:** In der Game Bar (Win+G) muss der Intensitätsregler aktiv sein und die Helligkeit ändern. Bei erzwungenen Spielen kommt keine Auto-HDR-Meldung [Q8].

**Wo die Intensität gespeichert wird:** angeblich als Token `AutoHDRStrength=` [Q9] **[ungetestet]**.

**Falls „global aus, pro Spiel an“ nicht geht (Plan B):**
- HDRpilot setzt nur für die Dauer einer Auto-HDR-Sitzung im globalen Wert das Token `AutoHDREnable=1`.
- Danach stellt HDRpilot den Wert zurück.
- Das klappt nur, wenn das Spiel über HDRpilot gestartet wird, weil der Wert vor dem Start stehen muss.

---

## 3. NVIDIA RTX HDR

**Offizielle Bedienung:** über die NVIDIA App, global oder pro Spiel. Eine Programmierschnittstelle oder ein SDK für Fremdprogramme gibt es nicht [Q12].

**Was die NVAPI liefert:**
- Öffentliche DRS-Funktionen, im Header geprüft [Q14]:
  - `CreateSession` (0x0694D52E), `LoadSettings` (0x375DBD6B)
  - `FindApplicationByName` (0xEEE566B2): Mit vollem Pfad liefert es das Profil, das der Treiber tatsächlich anwendet.
  - `CreateProfile`/`CreateApplication`
  - `SetSetting` (0x577DD202), `SaveSettings` (0xFCBC7E14)
  - Zurücknehmen mit `DeleteProfileSetting` (0xE4A26362) oder `RestoreProfileDefaultSetting` (0x53F0381E)
- Strukturgrößen: `NVDRS_SETTING_V1` = 12.320 Byte, `NVDRS_APPLICATION_V4` = 20.492 Byte.

**Die Einstellungs-IDs (nur durch Profile Inspector benannt [Q15], nicht von NVIDIA dokumentiert):**

| ID | Bedeutung | Status |
|---|---|---|
| `0x00DD48FB` | Ein (0/1) | ID belegt |
| `0x00DD48FC`–`FF` | Peak, Middle Grey, Contrast, Saturation | IDs belegt, Kodierung nur laut RHI [Q9], **[unsicher]** |
| `0x1077A11A` | „RTX HDR Allow“ | ID belegt |
| `0x00432F84` | Driver Flags / Debanding | ID belegt |
| `0x00980896` | Game Filters Enable (versteckt) | ID belegt |

- Die Bedeutung der Flags hat sich zwischen älteren Treibern schon einmal geändert. Ein Treiber-Update kann die Funktion also brechen.

**Zwei Betriebswege:**
- **App/Overlay-Weg:** Enable = 1. Die Regler wirken. Der Weg braucht NVIDIA App und Overlay. Ohne sie greifen die Profilwerte laut Forum nicht [Q17, 2026-01-13] **[unsicher]**.
- **Treiber-Weg:** Driver Flags ungleich 0.
  - Läuft ohne App, aber mit festen Werten: etwa 1000 nits und Papierweiß 150 [Q16, emoose 2024-02-04]. Die Regler werden ignoriert [Q15].
  - Debanding braucht Admin [Q9, RHI 2.2.6].
  - HDRpilot läuft bewusst als `asInvoker` (ohne Adminrechte). Für HDRpilot kommt dieser Weg also nicht in Frage.

**Adminrechte:**
- Der NVAPI-Header nennt für DRS keine Admin-Pflicht [Q14].
- Die DRS-Dateien geben „Everyone“ und „ALL APPLICATION PACKAGES“ Vollzugriff (L).
- RHI setzt Enable und die Regler ohne Admin [Q9].
- Schreiben ohne Admin ist trotzdem **[ungetestet]**.

**Offene Fragen:**
- Übernimmt die App fremde Werte? Überschreibt sie sie? **[ungetestet]**
  - NVIDIA-Komponenten schreiben die Datenbank auch von selbst. Die Datei wurde am 2026-10-10 um 13:30 geändert, vor der Leseprobe (L).
- Muss „Allow“ auf 1 stehen?
  - Laut emoose mussten `DD48FB`, `00980896` **und** `1077A11A` alle auf 1 stehen [Q16, 2024-02-03].
  - Die Regel „Allow nie auf 1 setzen“ aus dem Bericht ist deshalb **[widersprochen]**.
  - Lokal steht Allow in 224 Profilen voreingestellt auf 0, fast nur bei Nicht-Spielen und EAC-Launchern (L).
  - Sicher bleibt nur: **Bei einer Voreinstellung 0 schaltet HDRpilot nichts ein.**
- Braucht RTX HDR den Schalter „Game Filters and Photo Mode“? **[unsicher]**
  - Mit Treiber 591.44 und neuer brauchen Game Filters eine Mindestversion der App [Q18]. Die Seite selbst ist bei der Versionsnummer widersprüchlich und erwähnt RTX HDR nicht.
- Ob die Änderung erst beim nächsten Start wirkt, sagt nur ein Forenbeitrag **[unsicher]**.

**Voraussetzungen laut NVIDIA FAQ, Stand 2026-02-24 [Q12]:**
- Windows 11 22621 oder neuer.
- RTX 20 oder neuer, eine einzelne GPU.
- Bildschirm mit HDR400 oder besser.
- Treiber 565.90 oder neuer.
- APIs: DX9–12 und Vulkan, **kein OpenGL**. DX10 ist unklar, weil NVIDIA-News nur DX9, DX11, DX12 und Vulkan nennen [Q13].
- Auto HDR, das HDR im Spiel und Fast Sync müssen aus sein.
- Vollbild oder IFLIP. Ein randloses Fenster braucht die Fenstermodus-Optimierung.
- Kein Surround, kein Klonen. Das Spiel muss auf dem HDR-Bildschirm starten.
- MPO wird automatisch abgeschaltet.
- Peak kommt aus der EDID, höchstens 2000 nits.
- DSR, DLDSR und NIS nennt die aktuelle FAQ nicht mehr **[unsicher]**.

**Profile finden (Leseprobe, L):**
- Namen sind klein geschrieben. Es gibt auch Pfad-Endungen wie `deadisland2/deadisland/binaries/win64/deadisland-win64-shipping.exe`.
- Liegt ein Spiel in einem umbenannten Ordner, findet der Treiber das vordefinierte Profil nicht (Fehler −166).
- `game.exe` und `launcher.exe` landen in **gemeinsamen** Profilen. Dort darf HDRpilot nie schreiben.
- `CreateApplication` kennt einen Fehler −167, den Profile Inspector umgeht [Q15].
- Die Datenbank liegt unter `C:\ProgramData` und gilt **für alle Windows-Benutzer** auf diesem PC.

**Anti-Cheat:**
- Es gibt keine Aussage von NVIDIA oder einem Anti-Cheat-Hersteller.
- HDRpilot selbst bleibt sauber. Aber der **Treiber** lädt NVIDIA-signierte DLLs (`nvppex.dll`, `nvAIHDRx.dll`) in das Spiel [Q16].
- Beispiel Arc Raiders: NVIDIA hat dort die Filter gesperrt. Wer sie per Profile Inspector erzwingt, bekommt angeblich „Blocked Application“ [Q19, 2026-01-03].
- Ein CoD-Bann wurde einem DLSS-Override per Profile Inspector zugeschrieben. Ein Zusammenhang ist nicht belegt [Q20, 2025-03].
- Folge für HDRpilot: Bei Spielen mit Anti-Cheat standardmäßig **nichts** einschalten. Nie mit echten Konten testen.

**Lizenz:** RHI steht unter GPL-3.0. HDRpilot darf dort keinen Code übernehmen. Die Umsetzung muss eigenständig aus `nvapi.h` (MIT) entstehen.

---

## 4. Qualität und Entscheidungsregel

| | Auto HDR | RTX HDR |
|---|---|---|
| Bild | Windows zeigt SDR mit der sRGB-Kurve, dadurch werden Schwarzwerte angehoben [Q21][Q25] **[unsicher, Community]** | Etwas bessere Trennung der Lichter [Q21, nur 1 Spiel, Kena]. „Fast in jeder Hinsicht besser“ laut Digital Foundry, aber nur aus zweiter Hand [Q26]. Debanding kann feine Details schlucken [Q27] **[unsicher]** |
| Gamma | sRGB-Kurve | Laut Community entspricht Kontrast +25 etwa Gamma 2,2 [Q29]. RHI hat seine Voreinstellung entsprechend auf Gamma 2,2 umgestellt [Q9] **[unsicher]** |
| Leistung | „Kostet etwas GPU-Leistung“ [Q3], nach Community-Eindruck kaum messbar | Etwa 7–10 % auf einer RTX 4090 [Q22, 2026-07-21]. Für die RTX 50 gibt es keine belastbaren Werte |
| APIs | DX11/12 (DX10) | DX9–12, Vulkan |
| Abhängigkeit | nur „eligible“ Spiele | NVIDIA App mit Overlay (vermutlich), RTX-GPU |

- Eine gemessene Gegenüberstellung von Rtings, HDTVTest oder ComputerBase gibt es nicht.
- „RTX HDR sieht besser aus“ ist Community-Konsens, keine Messung.

**Regel für „Automatisch“** (pro EXE, nicht pro Spiel; ausgewertet beim Speichern und beim Start):

1. Hat der Nutzer etwas festgelegt, gilt das.
2. PCGamingWiki-HDR ist `true`, `always on` oder `limited` → **Nativ**: keine Nachrüstung. Eigene Nachrüstungen für diese EXE nimmt HDRpilot zurück.
3. HDR-Status `unknown` oder kein Wiki-Eintrag → **Aus**, mit Hinweis „bitte wählen“. Sonst könnte sich RTX HDR über natives HDR legen.
4. Anti-Cheat im Installationsordner erkannt (EasyAntiCheat, BattlEye, `start_protected_game.exe` usw.) → **Aus**, manuelle Wahl mit Warnung.
5. Je nach API-Klasse:
   - **D3D9 oder Vulkan** → RTX HDR, wenn verfügbar, sonst Aus.
   - **DXGI (D3D10–12)** → RTX HDR, wenn verfügbar und die Vorliebe „Bildqualität“ ist. Sonst Auto HDR, aber nur wenn Versuch E2 bestanden ist. Ohne Gewähr, weil nur „eligible“ Spiele mitmachen.
   - **OpenGL oder Legacy** → Aus.
   - **Unklar oder gemischt** → RTX HDR, wenn verfügbar, sonst Aus.
6. „RTX HDR verfügbar“ heißt, alles davon gilt:
   - NVIDIA RTX und Treiber ≥ 565.90.
   - NVIDIA App vorhanden (und Overlay, falls E9 das verlangt).
   - E8 hat auf diesem PC bestanden. Das ist ein Funktionsschalter, der beim Build- und Treiberstand festgehalten wird.
   - Keine Allow-Voreinstellung 0.
   - Kein generischer EXE-Name.
   - Kein Surround oder Klonen, Zielbildschirm HDR-fähig.
7. Globale Vorliebe „Leistung“, gedacht für kompetitive Spiele oder hohe fps → Auto HDR vor RTX HDR.

**Nebenbefund:** Für Spiele ohne natives HDR und ohne Nachrüstung bringt Windows-HDR nichts. Wegen der sRGB-Kurve sieht das Bild eher schlechter aus [Q25] **[unsicher]**. HDRpilot könnte dort empfehlen, HDR gar nicht einzuschalten.

---

## 5. API und Ziel-EXE erkennen, ohne das Spiel anzufassen

**Ziel-EXE, in dieser Reihenfolge:**
1. Die gelernte Laufzeit-EXE. Das kann HDRpilot schon (`SystemProcessIdInformation`, E65).
2. Vor dem ersten Start: Store-Metadaten mit Regeln für Stubs.
   - Unreal: `<Name>\Binaries\Win64|WinGDK\*-Shipping.exe` statt der Stub-EXE im Hauptordner.
   - Launcher und Anti-Cheat-Bootstrapper überspringen.
   - Xbox: `MicrosoftGame.config` ist lesbar, die EXE selbst nicht.
   - GOG: `playTasks`.
3. Der erste Start kann trotzdem danebengehen.

**API-Klasse, sortiert nach Verlässlichkeit:**
1. Startargumente: Wrapper-Kommandozeile, Steam `LaunchOptions` in `localconfig.vdf`, Optionen aus `appinfo.vdf` v41 [Q31]. Beispiele: `-dx11/-dx12/-vulkan/-force-*`.
2. Bekannte Konfigurationsdateien: Unreal-INI, RDR2 `system.xml`, Dying Light `video.scr`.
3. EXE-Namen mit `_dx11` oder `_dx12`.
4. PCGamingWiki. Die Parameter im Wikitext `{{API` lädt HDRpilot ohnehin schon, anonym. Mit Konto zusätzlich `cargoquery`: Join auf die Tabelle `API` mit `Direct3D_versions`, `Vulkan_versions` und `OpenGL_versions` [Q28]. Anonymes `cargoquery` wird abgelehnt.
   - Das Wiki liefert nur die **Menge** der APIs, nicht die Voreinstellung.
   - `HDRCompatibleAPI` wird auch durch OpenGL gesetzt.
   - Der Wert `9.0` wird als `9` gespeichert.
5. Statische PE-Hinweise (Datei nur lesend öffnen, mit FileShare.ReadWrite|Delete):
   - `D3D12SDKVersion`-Export [Q30], `d3d11`-Import, `vulkan-1`-String.
   - Lokal scheiterte das bei 9 von 15 EXEs oder führte in die Irre (Messung eines Recherche-Agenten, nicht gegengeprüft). Deshalb nur als schwacher Hinweis.
6. Ein Laufzeit-Trace per ETW (PresentMon) [Q32] braucht die Gruppe „Performance Log Users“. Das kommt nicht in Frage.

Gespeichert wird die Klasse zusammen mit Quelle, Vertrauen und einem Nutzer-Override.

---

## 6. Vorschlag für HDRpilot

**Neue Option pro Spiel „HDR-Art“:**
- **Automatisch:** wendet Regel 4 an und zeigt Wahl und Grund, z. B. „→ RTX HDR (Vulkan, laut Startargument)“.
- **Nativ**
- **RTX HDR**
- **Windows Auto HDR**
- **Aus:** HDRpilot rüstet nichts nach und lässt vorhandene Werte stehen.

Nicht verfügbare Einträge sind ausgegraut und erklären per Tooltip, warum.

**Wann geschrieben und zurückgestellt wird:**
- **Dauerhaft pro EXE, solange die Option gewählt ist**, also nicht pro Sitzung. Beide Funktionen wirken nur, wenn Windows-HDR an ist. So muss HDRpilot nicht kurz vor dem Prozessstart schreiben.
- Schreiben, sobald die Wahl gespeichert wird oder eine neue EXE gelernt wurde.
- Zurückstellen bei Wechsel der Option, beim Entfernen des Spiels, über die Schaltfläche „Alle Nachrüstungen zurücknehmen“ und bei der Deinstallation. Dafür muss das Setup erweitert werden.
- Zustandsdatei pro Schreibvorgang:
  - Original: Typ und Rohbytes, oder „fehlte“.
  - Was geschrieben wurde.
  - OS-Build und Treiberversion.
- Zurückgestellt wird nur, wenn der aktuelle Wert noch dem geschriebenen entspricht. Sonst gilt „extern geändert“, der Wert bleibt und es wird protokolliert.
- Beim Programmstart gleicht HDRpilot alles einmal ab.
- **Auto HDR:** Nur das Token `AutoHDREnable` ersetzen. Reihenfolge, fremde Tokens und das abschließende `;` bleiben erhalten. Werte, die kein reiner REG_SZ sind oder nicht sauber zerfallen, werden nicht angefasst.
- **RTX HDR:**
  - Spielspezifisches Profil per vollem Pfad suchen.
  - Ist es generisch oder fehlt es, ein eigenes Profil „HDRpilot – <Spiel>“ anlegen.
  - Allow-Voreinstellung 0 respektieren.
  - Allow nur dann setzen, wenn die App es laut E7 selbst so macht.
  - Rücknahme per `DeleteProfileSetting`. Eigene Profile werden gelöscht.
- **Gegenseitiger Ausschluss pro EXE:** Bei RTX HDR wird Auto HDR für die EXE ausdrücklich abgeschaltet (`2096`). Ob das genügt, klärt E12.

**Zeitpunkt beim Spielstart:**
- Die automatische Erkennung schaltet HDR erst **nach** dem Spielstart ein.
- Ob Auto HDR und RTX HDR dann noch greifen, ist **[ungetestet]** (E4/E10).
- Bis das geklärt ist, gelten nur „Mit HDR starten“, die Verknüpfung, die Steam-Startoption und die Frühwarnung als sicher.

**Oberfläche:**
- Statusanzeige pro EXE: „gesetzt“, „extern geändert“, „unbestätigt (noch nicht gestartet)“.
- Nach dem ersten Start ein Prüfhinweis: Win+G-Regler aktiv? NVIDIA-Overlay zeigt RTX HDR?
- Globale Einstellungen:
  - „Bildqualität / Leistung“
  - „Spiele mit Anti-Cheat nicht automatisch nachrüsten“ (an)
  - „Auto HDR erzwingen (experimentell)“ (aus)
- Warnung bei RTX HDR: Die Einstellung gilt für alle Windows-Benutzer und der Treiber lädt DLLs in das Spiel.

**Sicherheit:**
- Nur HKCU und NVAPI DRS im eigenen Prozess.
- Kein HKLM, kein Admin, `asInvoker` bleibt.
- Der Architekturtest (`AntiCheatSafetyTests`) bleibt unverändert gültig.
- Für die NVAPI-Strukturen einen Header-Abgleich gegen `nvapi.h` bauen, analog zu dem für wingdi.h.
- Funktionsschalter nach Build und Treiberstand: Hat sich etwas geändert, erscheint der Hinweis „ungeprüft“.

**Aufwand (grob geschätzt, ca. 9–14 Arbeitstage):**

| Teil | Tage |
|---|---|
| Auto-HDR-Modul (Parser, Zusammenführen, Zustand, Rückbau, Tests) | 2–3 |
| RTX-HDR-Modul (NVAPI-Interop, Profilauflösung, Umgehung von −167, Header-Abgleich, Tests) | 4–5 |
| API-Klassifikation, Mindestumfang (Wiki, Startargumente, Name, Konfigurationsdateien) und Anti-Cheat-Erkennung | 1,5–2 |
| Oberfläche und Setup-Aufräumen | 1–2 |
| Prüfrunde | 1–2 |

Gebaut wird in zwei Stufen: **Stufe 1 Auto HDR** nur, wenn E1/E2 bestehen. **Stufe 2 RTX HDR** nur, wenn E7–E9 bestehen. Scheitert E8, bleibt bei RTX HDR nur der Hinweis „in der NVIDIA App einschalten“.

**Risiken:**
- Undokumentierte Formate und IDs können mit jedem Update brechen.
- Die NVIDIA App ignoriert oder überschreibt fremde Werte.
- HDRpilot kann von außen nicht bestätigen, dass die Funktion wirkt.
- Der erste Start einer neuen EXE kann danebengehen.
- RTX HDR kostet Leistung.
- Beim Anti-Cheat gibt es keine Herstelleraussage.

---

## 7. Versuche vor der Umsetzung

**Grundregeln:**
- Alle Versuche außerhalb des Claude-Containers durchführen, also durch den Nutzer selbst oder per WMI. Im Container landet ein HKCU-Schreibvorgang in einer privaten Kopie.
- Nur Einzelspieler-Spiele **ohne** Anti-Cheat verwenden.

**Vorbereitung:**
- **E0 Sicherung:**
  - `reg export` von `…\DirectX\UserGpuPreferences`, `…\Microsoft\Direct3D` und `…\DirectX\GraphicsSettings` nach `%TEMP%`.
  - `DrsProbe.cs` um `EnumSettings` erweitern und die Profile der Testspiele vollständig auslesen. Weiterhin ohne Set oder Save.
  - Stand des Schalters „Game Filters and Photo Mode“ notieren.

**Auto HDR:**

| Nr. | Ablauf | Rückbau |
|---|---|---|
| **E1** | Mit Auto HDR global aus: In Einstellungen → Grafik → Spiel → Optionen nachsehen, welches Steuerelement es gibt. Dann global an, „Don't use Auto HDR“ setzen und wieder entfernen, nach jedem Schritt `reg query` | Global wieder aus, `reg import` |
| **E2** | Global aus und `AutoHDREnable=2097;` an der Render-EXE, dann starten. Prüfen, ob der Win+G-Regler aktiv ist und die Helligkeit ändert. Gegenproben: global an ohne Wert; global an mit `2096` | Original zurück. Optional, mit Admin durch den Nutzer: `AutoHDR.ScreenSplit` [Q3], danach wieder löschen |
| **E3** | Spiel, bei dem der Regler ausgegraut ist: erst nur `2097`, dann `D3DBehaviors` mit Dateiname bzw. vollem Pfad, jeweils global an und aus | Nur den eigenen `ApplicationN`-Schlüssel löschen |
| **E4** | Den Wert und HDR **während** das Spiel läuft ändern. Greift es ohne Neustart? Besonders wichtig: HDRpilot schaltet HDR erst nach dem Spielstart ein | – |
| **E5** | Game-Bar-Regler bewegen, Wert vorher und nachher vergleichen (`AutoHDRStrength`?) | Wert zurück |
| **E6** | Nur lesend: `AppStatus` (0/4/4096) mit „Regler aktiv oder ausgegraut“ vergleichen, 4–6 Spiele | – |

**RTX HDR:**

| Nr. | Ablauf | Rückbau |
|---|---|---|
| **E7** | RTX HDR in der App pro Spiel ein und wieder aus. Jeweils die DRS-Profile vorher und nachher vergleichen: Welche IDs schreibt die App, legt sie ein eigenes Profil an, speichert sie Zustand außerhalb von DRS? | Per App ausschalten, mit der Ausgangslage vergleichen |
| **E8** | Ohne Admin genau die Unterschiede aus E7 an ein **anderes** Testspiel schreiben (öffentliches `SetSetting`, dann `SaveSettings`). Fehler pro ID protokollieren. Greift es? Zeigt die App es an? Überschreibt sie es? | `DeleteProfileSetting` und `SaveSettings`, dann Vergleich mit der Ausgangslage |
| **E9** | E8 mit „Game Filters and Photo Mode“ aus und an wiederholen | Schalter zurück |
| **E10** | Wie E4 für RTX HDR | – |
| **E11** | Szene, die die GPU auslastet, ohne Bildratenbegrenzung, 3440×1440: SDR, Auto HDR und RTX HDR vergleichen (PresentMon oder Overlay). Dazu Sichtprüfung der dunklen Bildteile | – |
| **E12** | RTX HDR an, Auto HDR global an, pro Spiel `2096`: Wird doppelt verarbeitet (Win+G-Regler aktiv)? | Ausgangslage zurück |

---

**Quellen**
- [Q1] Microsoft DirectX Blog, 2023-01-19: https://devblogs.microsoft.com/directx/navigating-the-redesigned-graphics-settings-page/
- [Q2] Microsoft Support „Optimizations for windowed games“ (undatiert, © 2026): https://support.microsoft.com/en-us/windows/optimizations-for-windowed-games-in-windows-11-3f006843-2c7e-4ed0-9a5e-f9389e535952
- [Q3] Microsoft DirectX Blog, 2021-03-17: https://devblogs.microsoft.com/directx/auto-hdr-preview-for-pc-available-today/
- [Q4] Microsoft DirectX Blog, 2022-04-25: https://devblogs.microsoft.com/directx/exciting-additions-to-your-auto-hdr-experience-on-pc/
- [Q5] Microsoft DirectX Blog, 2022-02-16: https://devblogs.microsoft.com/directx/updates-in-graphics-and-gaming/
- [Q6] Phoenix-/ForceAutoHDR.Net (2026-09/10), `GpuPreferencesStore.cs` und `notes/`: https://github.com/Phoenix-/ForceAutoHDR.Net
- [Q7] ledoge/autohdr_force: https://github.com/ledoge/autohdr_force/blob/master/main.c
- [Q8] 7gxycn08/ForceAutoHDR: https://github.com/7gxycn08/ForceAutoHDR
- [Q9] RankFTW/RHI (GPL-3.0, 2026): https://github.com/RankFTW/RHI ; Release 2.2.6: https://newreleases.io/project/github/RankFTW/RHI/release/RHI-2.2.6
- [Q10] Windows Latest, 2025-02-12: https://windowslatest.com/2025/02/12/microsoft-unlocks-windows-11-24h2-for-more-gaming-pcs-with-feb-11-2025-update
- [Q11] Microsoft Q&A, 2022: https://learn.microsoft.com/en-us/answers/a/6502239
- [Q12] NVIDIA App FAQ a_id 5521, 2026-02-24: https://nvidia.custhelp.com/app/answers/detail/a_id/5521
- [Q13] NVIDIA News, 2024-10-01: https://www.nvidia.com/en-us/geforce/news/nvidia-app-beta-update-g-sync-controls-rtx-hdr-multi-monitor/
- [Q14] NVIDIA/nvapi (MIT, R615, 2026-09-18): https://github.com/NVIDIA/nvapi
- [Q15] nvidiaProfileInspector, `CustomSettingNames.xml` und `Reference.xml` (2026-08): https://github.com/Orbmu2k/nvidiaProfileInspector
- [Q16] Guru3D NvTrueHDR, emoose 2024-02-03/04: https://forums.guru3d.com/threads/nvtruehdr-tool-to-enable-rtx-hdr-in-games.451108/
- [Q17] Guru3D, 2026-01-13: https://forums.guru3d.com/threads/is-there-any-way-to-get-the-rtx-hdr-settings-to-take-effect-without-installing-the-nvidia-app.459032/
- [Q18] NVIDIA a_id 5752, 2025-12-04: https://nvidia.custhelp.com/app/answers/detail/a_id/5752
- [Q19] allthings.how, 2026-01-03: https://allthings.how/arc-raiders-has-blocked-nvidia-game-filters-what-that-means-and-what-you-can-do/
- [Q20] Guru3D, 2025-03: https://forums.guru3d.com/threads/i-get-permaban-in-cod-after-dlss-override.455671/page-2
- [Q21] HDRgameAnalysis, 2026-08: https://hdrgameanalysis.com/auto-hdr-windows-pc
- [Q22] XDA, 2026-07-21: https://www.xda-developers.com/i-dismissed-nvidia-app-until-i-tried-rtx-hdr/
- [Q23] PCGH, 2026-07-02: https://www.pcgameshardware.de/Nvidia-Geforce-Grafikkarte-255598/News/App-Neuer-Test-kaum-Fps-Verlust-Filter-kosten-bis-zu-10-Prozent-1546900/
- [Q24] Tom's Hardware, 2024-12: https://www.tomshardware.com/pc-components/gpu-drivers/nvidia-app-performance-issue-gets-fixed-with-release-11-0-1-but-all-it-has-done-is-turn-off-game-filters-by-default
- [Q25] dylanraga, win11hdr-srgb-to-gamma2.2-icm: https://github.com/dylanraga/win11hdr-srgb-to-gamma2.2-icm
- [Q26] StealthOptional (zitiert Digital Foundry), 2024-02-18: https://stealthoptional.com/article/nvidia-rtx-hdr-crushes-windows-autohdr
- [Q27] r/nvidia, 2024-02-29: https://www.reddit.com/r/nvidia/comments/1b3fds4/rtx_hdr_can_destroy_fine_picture_detail/
- [Q28] PCGamingWiki API (cargofields, Template:API, Template:Video), geprüft 2026-10-10: https://www.pcgamingwiki.com/w/api.php?action=cargofields&table=API
- [Q29] PCGamingWiki Glossar HDR: https://www.pcgamingwiki.com/wiki/Glossary:High_dynamic_range_(HDR)
- [Q30] Microsoft Agility SDK, 2021-04-20: https://devblogs.microsoft.com/directx/gettingstarted-dx12agility/
- [Q31] SteamDatabase/SteamAppInfo: https://github.com/SteamDatabase/SteamAppInfo
- [Q32] PresentMon: https://github.com/GameTechDev/PresentMon
- [Q33] Microsoft Support „Use Auto HDR“ (undatiert): https://support.microsoft.com/en-us/Windows/Hardware/Display-Graphics/use-auto-hdr-for-better-gaming-in-windows

**Weitere Unterlagen in diesem Ordner:**
- `auto-hdr-rtx-hdr/berichte/`: die fünf Einzelberichte der Recherche (englisch):
  - `autohdr.md`: Windows Auto HDR.
  - `rtxhdr.md`: RTX HDR über NVAPI und DRS.
  - `quality.md`: Bildqualität und Entscheidungsregel.
  - `apidetect.md`: Grafik-API und Ziel-EXE erkennen.
  - `critique.md`: Gegenprüfung aller Aussagen.

  Wo die Berichte und dieses Dokument voneinander abweichen, gilt dieses Dokument. Es berücksichtigt die Gegenprüfung.
- `auto-hdr-rtx-hdr/werkzeuge/`: lokale Lesewerkzeuge, die nur lesen (keine Set-, Save-, Create- oder Delete-Aufrufe). Sie sind nicht Teil des Builds:
  - `drsread.ps1`: liest RTX-HDR-Einstellungen aus allen DRS-Profilen und dem Profil einer EXE. Die EXE ist fest eingetragen und vor dem Lauf anzupassen.
  - `DrsProbe.cs`: findet zu Namen und Pfaden das Treiberprofil, das der Treiber tatsächlich anwendet.
  - `PeScan.cs`: liest Hinweise auf die Grafik-API (Importe, Exporte, Zeichenketten) aus einer EXE.
  - `AppInfo.cs`: liest Startoptionen aus Steams `appinfo.vdf` (v41).

  Für Versuch E0 muss `DrsProbe.cs` noch um `EnumSettings` erweitert werden.
- Laufen müssen sie außerhalb des Claude-Containers. Im Container sehen Registry-Lesungen unter HKCU die private Kopie.

**Projektbezug:**
- `src/HdrPilot.Core/Config/AppConfig.cs`: `GameConfig` bekäme das neue Feld „HDR-Art“.
- `tests/HdrPilot.Tests/Architecture/AntiCheatSafetyTests.cs`: NVAPI im eigenen Prozess und HKCU sind damit vereinbar.
- `src/HdrPilot.App/Setup/`: Beim Deinstallieren müssten Nachrüstungen zurückgenommen werden.
