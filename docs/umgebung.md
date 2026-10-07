# Umgebung

Kurzbeschreibung der Testumgebung, in der HDRpilot entwickelt und am echten System geprüft wurde (erfasst ab
2026-10-07 durch Abfragen am System, nicht aus Annahmen). Geräte-Kennungen, Adapter-Adressen, Treiberstände und
persönliche Angaben zum Rechner sind vor der Veröffentlichung entfernt.

## Betriebssystem

| Merkmal | Wert |
|---|---|
| Produkt | Windows 11 Home, 64 Bit (die Registry meldet historisch „Windows 10 Home“) |
| Version | 25H2 (Build 26200) zu Beginn, später 26H2 (Build 26300) |
| Folge für HDR | Build ≥ 26100: der neuere API-Weg (Typ 15/16) ist Standard (`HdrControllerOptions.ModernApiMinBuild`) |

## Bildschirm und Grafik

- Ein HDR-fähiger OLED-Bildschirm (3440 × 1440, 144 Hz, 10 Bit) über DisplayPort an einer NVIDIA-Grafikkarte der
  RTX-50-Serie; ein Bildschirm aktiv, er ist der Hauptbildschirm.
- Ohne HDR läuft der Bildschirm im Modus WCG (automatische Farbverwaltung an), mit 10 Bit.
- Daneben installiert, aber inaktiv: virtuelle Anzeigetreiber (VR).

Gemessene Rohwerte der HDR-Abfragen (Grundlage der Unit-Tests in `BitfieldTests`):

| Zustand | Typ 15 `value` | `activeColorMode` | bpc | Typ 9 `value` |
|---|---|---|---|---|
| HDR aus, WCG an | `0xD7` | 1 (WCG) | 10 | `0x7` (supported, enabled, wideColorEnforced) |
| HDR an | `0xF3` | 2 (HDR) | 10 | (enabled ohne wideColorEnforced) |
| Bildschirm im Bild-in-Bild-Modus (zweite Quelle angeschlossen) | `0x45` | 0 (SDR) | 8 | `0x5` |

Beobachtung: Im Bild-in-Bild-Modus meldet der Bildschirm 59,973 Hz, 8 bit und **kein HDR** (normal sind 144 Hz).
Ein HDR-Wunsch in diesem Zustand wird als „nicht HDR-fähig“ gemeldet und nicht geschaltet.

Unabhängige Gegenprobe zum gemeldeten Zustand: Die Windows-Einstellungen legen den HDR-Schalter je Bildschirm unter
`HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\MonitorDataStore\<Bildschirm>` als `HDREnabled` ab (daneben
`AutoColorManagementEnabled`). Das Gate-Skript für Phase 1 vergleicht dagegen.

## Launcher

Am Prüfrechner waren alle Launcher installiert, die HDRpilot liest: Steam, Epic Games, Xbox-App (mit Game Bar),
GOG Galaxy, Ubisoft Connect, EA app und Battle.net, später auch der Quiver Launcher. Wo HDRpilot sie findet und wie die
Dateien aufgebaut sind, beschreiben die Berichte unter `docs/scanner/`.

Format von `.GamingRoot` (gemessen): 4 Byte Kennung `RGBX`, UInt32 = 1, danach ein nullterminierter
UTF-16-Ordnername relativ zur Laufwerkswurzel (`XboxGames` auf C:, auf einem zweiten Laufwerk ein selbst gewählter
Name).

## Werkzeuge

- .NET SDK 10 (Ziel `net10.0-windows`, x64), Tests mit xUnit v3.
- Git für Windows.
- Header des Windows SDK (`wingdi.h` u. a.) für die Strukturtests aus dem NuGet-Paket `Microsoft.Windows.SDK.CPP`,
  geladen von `tools\fetch-sdk-headers.ps1` nach `.sdk\` (nicht im Repository).
- Gate-Skripte in Windows PowerShell 5.1 (kein Node.js, kein Python).
