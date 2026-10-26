# HDRpilot
A sleek, 100% vibe-coded Windows app that automatically enables HDR for selected games. Features optional Auto HDR for SDR games, shortcut and Steam launch option integration, an intuitive GUI, and support for Steam, Epic Games, Ubisoft, Battle.net, GOG, and Quiver Launcher.

HDRpilot schaltet Windows-HDR automatisch ein, wenn du ein ausgewähltes Spiel startest, und stellt danach den
vorherigen Zustand wieder her. Es findet deine installierten Spiele selbst (Steam, Epic, Xbox/Game Pass, GOG,
Ubisoft Connect, EA app, Battle.net, Quiver Launcher). Spiele ohne Launcher trägst du selbst ein, oder HDRpilot schlägt
sie aus deinen Suchordnern vor. Welche Spiele HDR unterstützen, zeigt es dir laut PCGamingWiki an. Im Alltag läuft es
unauffällig im Infobereich.

- Ein Setup ohne Administratorrechte, alternativ eine einzelne EXE ganz ohne Installation; kein Windows-Dienst.
- Keine Telemetrie und keine Eingriffe in Spielprozesse. Ins Netz geht HDRpilot nur für den Abgleich der HDR-Liste mit
  PCGamingWiki; das lässt sich abschalten.
- Im Hintergrund sparsam: eine Prozessabfrage pro Sekunde, gemessen rund 0,1–0,4 % eines CPU-Kerns und etwa 40–50 MB
  Arbeitsspeicher.

## Voraussetzungen

- Windows 11 oder Windows 10 ab 21H2, 64 Bit.
- Ein HDR-fähiger Bildschirm, bei dem sich unter *Einstellungen → System → Anzeige* „HDR verwenden“ einschalten lässt.
- Keine .NET-Installation nötig, die Laufzeit steckt in der EXE.

## Einrichtung

Empfohlen ist der Setup `HDRpilot-Setup-1.5.0.exe`. Er installiert nur für deinen Windows-Benutzer und braucht keine
Administratorrechte. Wer lieber ohne Installation arbeitet, nimmt den Ordner `HDRpilot` (siehe „Ohne Installation“).
Fertige Versionen gibt es unter [GitHub Releases](https://github.com/DanteGE/HDRpilot/releases).

1. `HDRpilot-Setup-1.5.0.exe` starten. Die Datei ist nicht signiert, deshalb fragt Windows SmartScreen beim ersten
   Start nach („Weitere Informationen“ → „Trotzdem ausführen“).
2. Der Setup zeigt den Programmordner `%LOCALAPPDATA%\Programs\HDRpilot` und drei Optionen: **Mit Windows starten**
   (vorbelegt), **Verknüpfung auf dem Desktop** (nicht vorbelegt) und **HDRpilot danach starten** (vorbelegt).
   **Installieren** kopiert HDRpilot dorthin, legt eine Verknüpfung im Startmenü an und trägt HDRpilot unter
   *Einstellungen → Apps → Installierte Apps* ein. Die Kommandozeile liegt danach in `<Programmordner>\cli`, daneben
   README, Lizenztext (`LICENSE`) und Lizenzhinweise.
3. Mit der dritten Option öffnet sich nach der Installation die Spielebibliothek (nicht, wenn der Setup mit
   Administratorrechten läuft; dann startest du HDRpilot über das Startmenü). Im Alltag läuft HDRpilot als Symbol
   im Infobereich (unten rechts neben der Uhr) und liest bei jedem Start deine Spiele ein. Siehst du das Symbol nicht,
   hat Windows es hinter dem Pfeil (˄) ausgeblendet; von dort kannst du es in die Taskleiste ziehen. Ein Doppelklick
   auf das Symbol (oder ein Start über das Startmenü) öffnet die Spielebibliothek.
4. Bei den Spielen, die HDR bekommen sollen, den Schalter **HDR automatisch** einschalten. Eingelesene Spiele sind
   anfangs immer aus; Spiele, die du selbst hinzufügst, haben den Schalter gleich an. Der Filter **HDR-fähig** zeigt
   dir, welche deiner Spiele laut PCGamingWiki HDR unterstützen.

Das Fenster zu schließen beendet HDRpilot nicht. Beenden über das Infobereich-Menü → **HDRpilot beenden**.

Einstellungen, Spieleliste, Protokolle und HDR-Liste liegen nicht im Programmordner, sondern in `%APPDATA%\HDRpilot`
und `%LOCALAPPDATA%\HDRpilot`. Der Setup fasst sie nie an: Hattest du HDRpilot schon, gelten sie nach der Installation
unverändert weiter. Lag deine bisherige `HDRpilot.exe` schon in `%LOCALAPPDATA%\Programs\HDRpilot`, ersetzt der Setup
sie dort, und Autostart, Verknüpfungen und Steam-Startoptionen zeigen weiter auf dieselbe EXE. Lag sie woanders,
beendet der Setup eine laufende alte Version und startet danach die neue; die alte EXE kannst du dann löschen, ihre
Verknüpfungen und Steam-Startoptionen legst du neu an.

### Aktualisieren

Den Setup der neuen Version starten. Er zeigt die installierte und die neue Version; Autostart und Desktop-Verknüpfung
sind wie bisher vorbelegt. **Aktualisieren** beendet ein laufendes HDRpilot kurz, ersetzt die Programmdateien und
startet HDRpilot wieder, wenn es vorher lief (wieder nur im Infobereich); lief es nicht, entscheidest du mit
**HDRpilot danach starten**. Schlägt dabei etwas fehl, bleibt die bisherige Version unverändert und lauffähig.
Einstellungen, Spieleliste, Protokolle und HDR-Liste bleiben erhalten, weil sie in `%APPDATA%\HDRpilot` und
`%LOCALAPPDATA%\HDRpilot` liegen. Denselben Setup noch einmal auszuführen repariert die Installation. Eine ältere
Version installiert er nur, wenn du **Trotzdem die ältere Version installieren** ankreuzt. Vorsicht nach Spielen aus dem
Quiver Launcher: HDRpilot 1.4.0 und älter kennen diese Quelle nicht, halten deine Spieleliste dann für defekt und
beginnen leer (siehe „Bekannte Grenzen“). Welche Version läuft, zeigen das Infobereich-Menü (graue Zeile unter dem
Menükopf) und der Kopf der Einstellungen.

### Ohne Installation

1. Den Ordner `HDRpilot` (mit `HDRpilot.exe`, `cli\hdrpilot.exe`, README, Lizenztext `LICENSE` und Lizenzhinweisen)
   an einen dauerhaften Ort legen, etwa nach `%LOCALAPPDATA%\Programs\HDRpilot\`. Autostart, Verknüpfungen und
   Steam-Startoptionen zeigen auf diesen Ort. Verschiebst du die EXE später, korrigiert HDRpilot den
   Autostart-Eintrag beim nächsten Start selbst; Verknüpfungen und Steam-Startoptionen musst du neu anlegen.
2. `HDRpilot.exe` starten (SmartScreen wie oben). Es öffnet sich kein Fenster: HDRpilot meldet sich kurz im
   Infobereich und liest deine Spiele ein. Ein Doppelklick auf das Symbol oder ein zweiter Start der EXE öffnet die
   Spielebibliothek; weiter wie oben in Schritt 4.
3. Optional in den Einstellungen **Mit Windows starten** einschalten.

Auf eine Installation per Setup kannst du jederzeit wechseln; Einstellungen und Spieleliste bleiben dabei erhalten.

## Bedienung

### Spielebibliothek

- Links im Kopf steht groß die gewählte Quelle („Alle Spiele“, „Steam-Spiele“, „GOG-Spiele“ … „Eigene Spiele“) unter
  der Kennzeile „SPIELEBIBLIOTHEK“, darunter ihre Zahlen: Spiele, davon mit **HDR automatisch**, davon neu (etwa „641
  Spiele · 12 mit HDR automatisch · 3 neu“). Mit Filtern oder Suche zählen nur die gezeigten Spiele dieser Quelle („36
  von 641 Spielen · 4 mit HDR automatisch · 1 neu“); ausgeblendete nicht installierte Spiele zählen nie. Ein Klick auf
  den Titel (oder Eingabe-, Leertaste bzw. Alt+Pfeil nach unten) öffnet die Quellen mit ihrer Zahl; die Wahl gilt wie
  im Feld „Quelle“ der Werkzeugleiste. Rechts stehen die Bildschirm-Kapsel und die Knöpfe **HDR einschalten** bzw. **HDR
  ausschalten** (schaltet sofort, unabhängig von einem Spiel), **Automatik pausieren** und das Zahnrad für die
  Einstellungen. Die
  Kapsel zeigt den Zielbildschirm mit Farbtiefe, ob HDR an ist, und seine Werte laut Windows: Spitze, Vollbild und
  Minimum in nits. Die Skala reicht von 0 bis zur Spitze bei eingeschaltetem HDR, auch wenn HDR gerade aus ist (die
  Fähigkeit des Bildschirms, keine Messung); solange HDRpilot die Werte nicht kennt, steht dort „—“.
- Darunter der Titelbereich des gewählten Spiels: Titelbild, Logo oder Name, wann es zuletzt gespielt wurde, die Quelle
  und ein Kennzeichen zur HDR-Unterstützung. Beim Wechsel des Spiels blendet das Bild über; Umgebungslicht und Leuchten
  des Fensters nehmen die Farben des Titelbilds an (abschaltbar unter „Darstellung“, siehe „Einstellungen“).
- Drei Ansichten, umschaltbar in der Werkzeugleiste oder mit Strg+1, Strg+2 und Strg+3; die Wahl bleibt gespeichert:
  - **Liste** (Standard): je Spiel eine Zeile mit Vorschau, Name, Quelle, „Zuletzt gespielt“, HDR-Kennzeichen und dem
    Schalter **HDR automatisch**. Ein Klick auf eine Spaltenüberschrift sortiert. Links über der Liste wählst du die
    Vorschau: **16:9-Vorschaubild** oder **Symbol**. In schmalen Fenstern fallen erst die Quelle, dann die HDR-Spalte
    und „Zuletzt gespielt“ weg. Wie hoch der Titelbereich über der Liste ist, stellst du mit dem Griff an seiner
    Unterkante ein: ziehen oder, wenn der Griff den Fokus hat, Pfeil hoch/runter (Bild auf/ab in größeren Schritten,
    Pos1/Ende kleinste bzw. größte Höhe); Esc bricht das Ziehen ab, Doppelklick oder Eingabetaste stellen den Standard
    wieder her (ein Drittel der Fensterhöhe). HDRpilot merkt sich die Höhe; darunter bleiben immer mindestens fünf ganze
    Zeilen der Liste zu sehen. Beim Scrollen bleibt die Höhe, wie sie ist.
  - **Raster**: Cover im Format 2:3. Spaltenzahl und Kachelgröße richten sich nach der Fensterbreite, im Vollbild
    genauso wie im kleinen Fenster; nichts wird gestreckt oder abgeschnitten. Der Schalter „HDR automatisch“ erscheint
    an der gewählten Kachel und beim Zeigen mit der Maus. Den Titelbereich stellst du mit demselben Griff ein wie in der
    Liste; eine ganze Kachelreihe bleibt immer zu sehen.
  - **Regal**: großes Titelbild mit **Starten**, **Desktop** und **Startmenü**, darunter eine Reihe Cover zum Blättern
    und rechts die Karte „AUTOMATIK“ mit den wichtigsten Schaltern (nur, wenn ein Spiel gewählt ist). **Details** (oder
    die Taste I) öffnet alle Details als Schublade, Esc schließt sie. Mit dem Griff unter dem Titelbild tauschst du
    Titelbild gegen Covergröße: Ein höheres Titelbild macht die Cover kleiner, bis zur kleinsten Größe.
  - Jede Ansicht merkt sich ihre eigene Höhe des Titelbereichs; Doppelklick oder Eingabetaste am Griff setzen nur die
    Höhe der gerade gezeigten Ansicht zurück.
- In Liste und Raster stehen rechts die Details zum gewählten Spiel. Im Spalt dazwischen sitzt die Bildlaufleiste der
  Liste bzw. des Rasters: Die Pille zeigt, wo du bist und wie viel davon zu sehen ist. Ziehen scrollt, ein Klick über
  oder unter die Pille blättert eine Seite, das Mausrad scrollt auch im Spalt; passt alles ins Fenster, ist sie weg. Die
  Breite der Details änderst du an der feinen Linie, die beim Überfahren des Spalts erscheint: waagerecht ziehen (Esc
  bricht ab), mit Fokus auf dem Trenner die Pfeiltasten, Doppelklick setzt zurück; die Breite merkt sich HDRpilot,
  höchstens 560 px.
- Tastatur: Strg+F blendet die Werkzeugleiste ein (der Cursor steht dann in der Suche) und wieder aus, die Pfeiltasten
  wählen, Enter startet das gewählte Spiel, die Leertaste schaltet „HDR automatisch“, Esc leert die Suche, Entf fragt
  bei einem nicht installierten Spiel aus einem Launcher, ob seine Einstellungen weg sollen, bei einem selbst
  hinzugefügten, ob es aus der Bibliothek soll (siehe unten). Rechtsklick, Umschalt+F10 oder die Menütaste öffnen das
  Spielmenü mit **Starten**, **HDR automatisch**, **Nach dem Spiel zurückstellen**, **Windows Auto HDR**, **Bild
  wählen…**, **Symbol ändern…** bzw. **Symbol zurücksetzen**, **Desktop-Verknüpfung erstellen**, **Zum Startmenü
  hinzufügen**, **Installationsordner öffnen** und, nur bei nicht installierten Spielen aus Launchern, **Einstellungen
  entfernen…**. Knöpfe, die ein Menü öffnen (der Titel mit den Quellen, „···“ und das große Symbol in den Details),
  schließen es beim zweiten Klick wieder, ebenso Eingabe- oder Leertaste, solange darin nichts gewählt ist.
- Titelbild, Logo und Cover kommen aus den Bild-Zwischenspeichern der Launcher auf dem PC (Steam, GOG Galaxy, Ubisoft
  Connect, Xbox-Paket, bei Quiver nur das Cover), nur lesend, nie aus dem Netz. Fehlt eines, erzeugt HDRpilot ein
  Ersatzbild aus Symbol und Name; neue Bilder eines Launchers erscheinen von selbst, bei Quiver nach dem nächsten
  Einlesen (siehe „So funktioniert es“). Ein anderes Bild wählst du per Rechtsklick › **Bild wählen…** oder im
  Abschnitt „BILDER“ der Details: Je Reiter **Titelbild**, **Logo** und **Cover** stehen **Automatisch**, weitere lokale
  Bilder (etwa der Startbildschirm eines Xbox-Pakets), das erzeugte Bild und **Eigene Datei…** (PNG, JPG, WebP, BMP) zur
  Wahl; **Übernehmen** speichert, **Zurücksetzen** wählt im aktuellen Reiter wieder **Automatisch**. Eigene Bilder legt
  HDRpilot verkleinert in seinem Datenordner ab, die gewählte Datei bleibt unverändert. Optional füllt SteamGridDB
  fehlende Bilder aus dem Netz (siehe „Einstellungen“).
- Bewegung: Das Titelbild zoomt langsam und folgt leicht der Maus, Lichtstrahlen und Lichtpunkte ziehen darüber. Geht
  HDR an, während die Bibliothek offen ist, läuft ein kurzer HDR-Moment (Lichtstreifen, hellere Fassung des Titelbilds,
  Partikel für höchstens 2,5 s, leuchtender Rand). Wie viel sich bewegt, stellst du unter „Darstellung“ › „Effekte“
  ein; minimiert, im Hintergrund und während ein Spiel läuft steht alles still. Rechts in der Statuszeile steht, was
  gerade gilt.
- Was PCGamingWiki über die HDR-Unterstützung weiß, zeigt die Liste in der Spalte „HDR“: „NATIV“, „TEILWEISE“
  (eingeschränkt) oder „MOD“ (nur per Mod). Kacheln von Spielen mit HDR tragen ein Kennzeichen „HDR“, der Titelbereich
  nennt „NATIVES HDR“, „HDR TEILWEISE“, „HDR PER MOD“ oder „HDR UNBEKANNT“.
- Das violette Kennzeichen „AUTO HDR“ direkt hinter dem Namen zeigt Spiele, bei denen **Windows Auto HDR** an ist. Es ist
  nur eine Anzeige; geschaltet wird in den Details, wirken tut es beim Start über HDRpilot (siehe „Windows Auto HDR“).
  Sein Funkel-Symbol hat sonst nur der Eintrag **Auto HDR** im Infobereich.
- Die Symbole stammen aus Dateien, die schon auf dem PC liegen, nie aus dem Netz (bei Steam das offizielle
  Steam-Symbol, siehe „So funktioniert es“). Ein eigenes Bild setzt du per Rechtsklick auf das Spiel oder per Klick auf
  das große Symbol in den Details: **Symbol ändern…** nimmt ein Bild (PNG, JPG, WebP, BMP, GIF, ICO) oder eine EXE,
  **Symbol zurücksetzen** zeigt wieder das automatische Symbol. Das eigene Symbol hat Vorrang vor allen anderen
  Quellen; die gewählte Datei bleibt unverändert, HDRpilot legt eine verkleinerte Kopie (96 px) in seinem Datenordner ab.
- Die Werkzeugleiste über der Liste: Suche, Filter nach Quelle, Sortierung, die Filter **HDR automatisch**,
  **Neu** und **HDR-fähig** als Symbol-Schalter mit ihrer Zahl (auch Alt+A, Alt+N, Alt+H), rechts Ansicht, **Neu
  einlesen**, **Alle als gesehen markieren** und **Spiel hinzufügen**. Zeigst du eine Sekunde auf ein Element, erklärt
  ein Tooltip es samt Kürzel. In schmaleren Fenstern werden Suche, Quelle und Sortierung schmaler, dann zeigt die
  Sortierung nur ihr Symbol; erst wenn auch das nicht passt, verteilt sich die Leiste auf zwei Zeilen. Ein Filter-Schalter
  ist nur hervorgehoben, solange sein Filter an ist; den Fokusrahmen zeigt er nur, wenn du mit der Tastatur bedienst,
  nicht nach einem Mausklick.
- Die Werkzeugleiste lässt sich ausblenden: über die kleine Lasche rechts über der Leiste, mit Strg+F (auch aus der
  Suche heraus; Strg+F1 geht auch) oder per Rechtsklick auf eine freie Stelle der Leiste (auch Umschalt+F10) › **Leiste
  ausblenden**. Dann steht an der Unterkante des Titelbilds nur eine Kapsel, die die Suche bzw. die aktiven Filter samt
  Anzahl zeigt; Suche und Filter bleiben dabei aktiv, gefiltert leuchtet die Kante, und das Kreuz in der Kapsel setzt
  alle Filter zurück. Ein Klick auf die Kapsel oder Strg+F blendet die Leiste wieder ein, mit Strg+F steht der Cursor
  gleich in der Suche. HDRpilot merkt sich, ob sie aus- oder eingeblendet ist.
- Fällt das gewählte Spiel durch Suche oder Filter aus der Liste, zeigen Titelbild und Details den ersten Treffer (er
  bleibt „NEU“, bis du ihn selbst anklickst); passt gar nichts, steht in der Liste „Keine Spiele für diese Auswahl“
  und in den Details „Kein Spiel gewählt“. Sobald dein Spiel wieder in der Liste steht, etwa nach dem Zurücksetzen
  der Filter, ist es wieder gewählt.
  Verschwindet das gewählte Spiel ganz aus der Liste (entfernt oder ausgeblendet), ist das nächste gewählt, am Ende der
  Liste das vorige; fällt damit auch die gewählte Quelle weg, zeigt die Liste wieder alle Spiele, und gewählt ist das,
  das dort auf das verschwundene folgt.
- Sortierung nach **Name**, **Zuletzt gespielt** oder **HDR-Unterstützung**; die Wahl bleibt gespeichert. „Zuletzt
  gespielt“ kennt Steam-Spiele aus Steams eigenen Daten und alle Spiele, die HDRpilot laufen sieht.
- Neu gefundene Spiele tragen die Markierung „NEU“. Sie verschwindet, sobald du das Spiel anklickst oder seinen
  Schalter änderst; das Augen-Symbol neben „Neu einlesen“ (**Alle als gesehen markieren**) entfernt sie bei allen.
- **Neu einlesen** liest alle Launcher erneut ein. Das passiert auch bei jedem Start von HDRpilot im Hintergrund.
  Deinstallierte Spiele bleiben mit dem Hinweis „Nicht installiert“ samt Einstellungen in der Liste; nach einer
  Neuinstallation gelten die Einstellungen wieder. Brauchst du sie nicht mehr, entfernst du sie mit **Einstellungen
  entfernen…** (siehe unten); ausblenden lassen sich nicht installierte Spiele unter „Einstellungen“ › **Bibliothek**.
- **Spiel hinzufügen**: Spiele ohne Launcher selbst eintragen. Wähle am besten die Start-EXE des Spiels. Wählst du einen
  Ordner, kann HDRpilot das Spiel nur dann selbst starten, wenn direkt darin genau eine EXE liegt; die Erkennung beim
  Spielen funktioniert in beiden Fällen.
- **Vorschläge aus deinen Suchordnern**: Unterordner deiner Suchordner mit EXE-Dateien erscheinen oben in der
  Bibliothek. Übernommen wird nur, was du dort hinzufügst.
- Details zum gewählten Spiel, von oben nach unten:
  - **Starten** (für Bildschirmleser **Mit HDR starten**): schaltet HDR ein, wartet die Bestätigung ab und startet dann
    das Spiel. Ist **Nach dem Start in den Infobereich** an (siehe „Einstellungen“), schließt sich die Bibliothek danach.
    Bei einem Spiel aus dem Quiver Launcher ist es gesperrt, solange HDRpilot keine eindeutige Start-EXE kennt; der
    Hinweis darunter sagt, was zu tun ist (siehe „Quiver Launcher“).
  - **Desktop** / **Startmenü**: legt eine Verknüpfung „<Spiel> (HDR)“ an, die das Spiel genauso über HDRpilot startet.
    Im Startmenü liegt sie im Ordner „HDRpilot“. Sie zeigt das Symbol der Liste, auch dein eigenes (dafür legt HDRpilot
    neben dessen PNG eine ICO-Datei ab, die beim Zurücksetzen bleibt, damit die Verknüpfung ihr Symbol behält). Ist das
    Symbol der Liste ein Bild wie ein Xbox-Logo, zeigt sie das der Spiel-EXE.
  - **Steam-Startoption kopieren** (nur bei Steam-Spielen): Notlösung für Spiele, die du lieber direkt aus Steam
    startest. Kopiert `"…\HDRpilot.exe" --wrap -- %command%`; in Steam per Rechtsklick auf das Spiel → Eigenschaften →
    Allgemein → Startoptionen einfügen. Dann ist HDR auch beim Start aus Steam an, bevor das Spiel startet
    (HDRpilot muss dafür laufen).
  - **HDR automatisch**, **Bildschirm** (welcher Bildschirm geschaltet wird, Standard: die globale Einstellung) und
    **Nach dem Spiel zurückstellen** (danach wieder den HDR-Zustand von vorher herstellen, Standard: an).
  - Abschnitt „BILD“ mit **Windows Auto HDR**: Windows rechnet das SDR-Bild eines Spiels ohne eigenes HDR in HDR um
    (DirectX 11/12). Das wirkt nur, wenn das Spiel über HDRpilot startet (**Starten**, „(HDR)“-Verknüpfung,
    Steam-Startoption): HDRpilot schaltet Auto HDR in Windows direkt davor ein und nach dem Spiel wieder zurück; kurz
    steht das unter dem Titel („Wirkt beim Start über HDRpilot“), ausführlich hinter **Mehr erfahren** (wieder zu mit
    **Weniger**; aufgeklappt bleibt es für alle Spiele, bis du das Fenster schließt). Das Schalten selbst ändert in
    Windows noch nichts. Unabhängig von **HDR automatisch**; ist das aus, weist ein Hinweis darauf hin, dass Windows-HDR
    schon vor dem Spielstart an sein muss. Ist Auto HDR an und **HDR automatisch** aus, steht dieser Hinweis auch
    zugeklappt, sonst nur bei den Erklärungen. Hat das Spiel laut PCGamingWiki eigenes HDR, sind Titel und Schalter
    blasser, und bei den Erklärungen rät ein Hinweis, das HDR des Spiels zu nehmen; schalten lässt es sich trotzdem
    (siehe „Windows Auto HDR“). Schaltest du es bei einem Steam-Spiel ein, dessen
    Steam-Startoption noch fehlt, fragt HDRpilot, ob es sie kopieren soll (**Startoption kopieren**, wie
    **Steam-Startoption kopieren**); eintragen musst du sie in Steam selbst. Ablehnen ändert nichts, Auto HDR bleibt an.
  - **EXE-Dateien**: Grundsätzlich zählt jedes Programm im Installationsordner als dieses Spiel, außer Hilfsprozessen.
    Die Liste zeigt die bekannten EXEs mit Herkunft („vom Launcher“, „beim Spielen erkannt“, „von dir hinzugefügt“).
    Du kannst EXEs hinzufügen (auch außerhalb des Spielordners), entfernen (dann zählen sie für dieses Spiel nie) und
    mit **Wieder aufnehmen** zurückholen. **Neu lernen** vergisst die beim Spielen erkannten EXEs.
  - Abschnitt „BILDER“: Titelbild, Logo, Cover und Symbol des Spiels; ein Klick öffnet **Bild wählen…**, beim Symbol
    das Menü mit **Symbol ändern…**. Darunter steht, woher die Bilder kommen, etwa „Lokal aus dem Steam-Cache · nichts
    heruntergeladen“ oder „Eigene Datei · nur auf diesem PC“.
  - **Aus der Bibliothek entfernen**: nur für selbst hinzugefügte Spiele.
  - **Einstellungen entfernen…** (ganz unten, rot): nur für nicht installierte Spiele aus Launchern, auch per
    Rechtsklick oder Entf. Nach einer Rückfrage vergisst HDRpilot, was es zu dem Spiel gespeichert hat: den Eintrag
    samt Schaltern, gemerkten EXE-Dateien und Bildwahl, das eigene Symbol und die eigenen Bilder. Die Rückfrage nennt,
    was verloren geht (etwa dass „HDR automatisch“ an ist oder „2 gemerkte EXE-Dateien“), warnt, wenn das Spiel
    vielleicht noch installiert ist (sein Laufwerk ist nicht verbunden, eine EXE des Spiels ist noch da, Steam führt es
    noch als installiert, etwa mitten in einem Update, oder Laufwerk bzw. Ordner antworten nicht oder sind nicht lesbar;
    ein Restordner nur mit Spielständen, Einstellungen und Protokollen zählt nicht), und hat den Fokus auf **Abbrechen**. Gibt es
    „(HDR)“-Verknüpfungen des Spiels auf dem Desktop oder im Startmenü, löscht ein Häkchen sie mit, ihre ICO-Datei nur,
    wenn sicher keine andere Verknüpfung sie mehr zeigt; ohne Häkchen bleiben sie liegen und starten das Spiel erst
    wieder, wenn es installiert und neu eingelesen ist. Am Spiel, am Launcher und an anderen Verknüpfungen ändert sich
    nichts. Danach ist das nächste Spiel der Liste gewählt. Installierst du das Spiel wieder, erscheint es beim nächsten
    Einlesen als neu, mit „HDR automatisch“ aus. Solange das Spiel läuft, geht es nicht.

### Infobereich

Das Symbol, eine Sonne über dem Horizont in den Farben deines Farbschemas, zeigt den Zustand: bei ausgeschaltetem HDR
als Linie, bei eingeschaltetem als gefüllte Kachel, bei pausierter Automatik mit gelbem Pause-Abzeichen und, solange ein
Spiel mit HDR läuft, mit grünem Abzeichen. Das Fenstersymbol in Titelleiste und Taskleiste zeigt dasselbe. Der Tooltip
nennt die Spiele, für die HDRpilot gerade HDR geschaltet hat, ob die Automatik aktiv ist und gegebenenfalls, wie lange
der Neustart-Puffer HDR noch anlässt. Das Menü (Rechtsklick) bietet:

- **HDR jetzt einschalten** / **HDR jetzt ausschalten**
- **Auto HDR**: zeigt, ob der Schalter „Auto HDR“ von Windows gerade an ist (gelesen beim Öffnen des Menüs), und
  schaltet ihn um. Das gilt ab dem nächsten Spielstart, für jedes Spiel und auch ohne HDRpilot. Eine Änderung hier ist
  deine Wahl: HDRpilot stellt sie nach einem Spiel nicht zurück (siehe „Windows Auto HDR“). Hat HDRpilot den Schalter
  gerade nur für ein Spiel eingeschaltet, sagt das der Tooltip: Nach dem Spiel ist er wieder aus. Ist der Wert nicht
  lesbar oder in einem fremden Format, ist der Eintrag gesperrt.
- **Automatik pausieren**: keine neuen automatischen Schaltungen; laufende Spiele werden noch normal zurückgestellt.
  „Starten“, „(HDR)“-Verknüpfungen und die Steam-Startoption schalten weiterhin. Die Pause gilt, bis du sie
  aufhebst oder HDRpilot neu startet; sie wird nicht gespeichert.
- **Spieleliste öffnen**, **Einstellungen**, **Protokollordner öffnen**
- **HDRpilot beenden**

Direkt unter dem Kopf des Menüs (Symbol, „HDRpilot“ und Status) steht grau und nicht anklickbar die Version („Version“
mit Versionsnummer), dieselbe Nummer wie im Setup und unter „Installierte Apps“.

Ein Klick auf einen Hinweis von HDRpilot öffnet ebenfalls die Spielebibliothek.

### Einstellungen

| Einstellung | Bedeutung | Standard |
|---|---|---|
| Bildschirme | Auf welchen Bildschirmen HDR geschaltet wird | Hauptbildschirm |
| Abfrageintervall | Wie oft nach Spielstarts gesehen wird | 1000 ms |
| Nachlaufzeit | Wartezeit nach dem Spielende, bevor zurückgestellt wird | 10 s |
| Neustart-Puffer | Endet ein Spiel weniger als 5 Minuten nach dem Start, bleibt HDR so lange an (Minuten und Sekunden, höchstens 30:00, 0:00 = aus; siehe „So funktioniert es“) | 1:00 |
| Startfrist | Bei „Starten“: so lange wird auf den Spielprozess gewartet | 120 s |
| Mit Windows starten | Autostart über `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | aus |
| Hinweise anzeigen | Kurze Meldungen im Infobereich beim Schalten und bei Problemen; nie während Vollbild-Spielen, Präsentationen oder bei gesperrtem Bildschirm (dann nur im Protokoll). Aus: alle Meldungen nur im Protokoll | an |
| Suchordner | Ordner mit Spielen ohne Launcher; Unterordner mit EXE werden vorgeschlagen | keine |
| Nicht installierte Spiele ausblenden | Nicht installierte Spiele stehen nicht in der Bibliothek und zählen dort nirgends mit (Zahl der Spiele, Quellen, Filter); wie viele es sind, steht daneben und im Tooltip zum Zurücksetzen der Filter. „HDR automatisch“ wirkt für sie weiter | aus |
| Hilfsprozesse ausschließen | Programme, die nie als Spiel zählen (Absturzmelder, Installer und Laufzeitpakete, Anti-Cheat-Installer und -Dienste, reine Launcher-Fenster …) | Standardliste |
| Farbschema | Duoton oder Tritonus, eine der acht Vorlagen oder eigene Farben; färbt Oberfläche, Tray- und Fenstersymbol | Abendrot, Duoton |
| Textfarbe auf Akzentflächen automatisch | Dunkle Schrift oder Weiß auf Akzentflächen, je nachdem, was besser lesbar ist | an |
| Hintergrund folgt dem Spiel | Umgebungslicht und Leuchten der Bibliothek nehmen die Farben des gewählten Titelbilds an | an |
| Thema | Automatisch (wie der App-Modus von Windows), Hell oder Dunkel | Automatisch |
| Effekte | Voll, Reduziert (nur kurze Übergänge bis 0,3 s) oder Aus | Voll |
| Beim Spielen anhalten | Solange ein Spiel läuft, steht jede Animation still | an |
| Im Hintergrund anhalten | Minimiert, verdeckt oder nicht aktiv steht jede Animation still | an |
| Windows-Animationseffekte beachten | Ist „Animationseffekte“ in Windows aus, gilt mindestens „Reduziert“ | an |
| Nach dem Start in den Infobereich | Nach einem Start aus der Bibliothek schließt sich das Fenster, HDRpilot läuft im Infobereich weiter | aus |
| SteamGridDB (online) | Fehlende Titelbilder, Logos und Cover von steamgriddb.com laden, nur mit eigenem API-Schlüssel | aus |
| Bei jedem Start abgleichen | HDR-Liste von PCGamingWiki 30 s nach dem Start abgleichen, höchstens einmal pro Stunde (siehe „HDR-Liste“) | an |
| Protokollstufe (Erweitert) | Wie ausführlich protokolliert wird | Normal |
| Tastenkürzel als Notlösung (Erweitert) | Sendet Win+Alt+B, nur wenn die Windows-Schnittstelle scheitert und gerade kein Spiel läuft; braucht die Xbox Game Bar und schaltet alle HDR-fähigen Bildschirme um | aus |

Im Bereich „HDR-Liste (PCGamingWiki)“ gibt es außerdem **Jetzt abgleichen**, den Stand der Liste und das optionale
PCGamingWiki-Konto (siehe unten). Für „Windows Auto HDR“ gibt es keine Einstellung: Es wird je Spiel in der Bibliothek
geschaltet und wirkt immer beim Start über HDRpilot (siehe „Windows Auto HDR“).

Nur in `config.json` einstellbar: `launcherSignals` (Frühwarnung aus den Protokollen von Epic und Steam, Standard
`true`; sie setzt für Spiele mit „Windows Auto HDR“ auch den Auto-HDR-Schalter vor dem Spiel). Den Neustart-Puffer
speichert HDRpilot dort als `restartBufferSeconds` (in Sekunden); fehlt der Wert, gilt der Standard von 1 Minute.
Ordner des Quiver Launchers, die HDRpilot sich gemerkt hat, stehen in `quiverFolders` (fehlt, solange es keinen gibt);
einen Ordner trägst du dort ein oder, einfacher, startest Quiver einmal, während HDRpilot läuft.

Im Bereich **Bibliothek** entfernt **Einstellungen aller nicht installierten Spiele entfernen…** auf einmal, was
**Einstellungen entfernen…** bei jedem einzelnen nicht installierten Spiel aus einem Launcher entfernen würde. Die
Rückfrage nennt alle betroffenen Spiele, unter jedem, was verloren geht, und warnt, wenn Spiele vielleicht noch
installiert sind; warum, steht unter dem Namen des Spiels: Laufwerk nicht verbunden, EXE-Datei noch da, bei Steam noch
eingetragen oder nicht prüfbar (Laufwerk bzw. Ordner antworten nicht oder sind nicht lesbar). Ein Restordner nur mit
Spielständen, Einstellungen und Protokollen zählt nicht. Das gilt sofort und unabhängig von **Speichern**; selbst
hinzugefügte und gerade laufende Spiele bleiben (laufen alle, sagt die Karte das).

Links führt eine Navigation durch die Bereiche (**Allgemein**, **Bildschirme**, **Automatik**, **Suchordner**,
**Bibliothek**, **Hilfsprozesse**, **Darstellung** mit **Farben**, **Bilder** und **Symbole und Tray**, **Erweitert**,
**Protokolle**); alle stehen untereinander auf einer Seite, ein Klick springt hin. Oben in der Navigation steht die
Version von HDRpilot, dieselbe Nummer wie im Infobereich-Menü, unten die Zahl der Spiele (ohne ausgeblendete) und
**Spielebibliothek öffnen**. In
breiten Fenstern stehen die Bereiche in bis zu drei Spalten nebeneinander, jeder höchstens 760 px breit; im Vollbild
bleibt der Rest frei, statt die Bereiche in die Länge zu ziehen.

**Speichern** (Strg+S) und **Abbrechen** (Esc) gelten für alle Bereiche außer der Darstellung. Was du unter
„Darstellung“ änderst, gilt und speichert sofort (**Wird sofort angewendet**); **Abbrechen** nimmt es nicht zurück,
**Standard** setzt die ganze Darstellung zurück. Dort gibt es:

- **Farbschema**: **Duoton** (Akzent und Atmosphäre, der Glanz entsteht aus der Atmosphäre) oder **Tritonus** (Akzent,
  Atmosphäre und Glanz), acht Vorlagen (Abendrot, Cyber, Neongrün, Royal, Gletscher, Synthwave, Glut, Polarlicht) und
  eigene Farben: Ein Klick auf ein Farbfeld öffnet den Farbwähler (Feld, Farbton-Leiste, Hex-Wert). Der Akzent färbt
  Schaltflächen und Auswahl, die Atmosphäre Hintergrund und Leuchten, der Glanz HDR-Moment und Lichtstreifen. Alle
  Farben der Oberfläche, das Tray-Symbol und das Fenstersymbol folgen dem Schema; Text auf Akzentflächen bleibt
  mindestens 4,5:1 lesbar, Schalter und Fokusrahmen heben sich mindestens 3:1 ab.
- **Live-Vorschau**: deine zuletzt gespielten Spiele im gewählten Schema. Der Schalter **HDR** zeigt den HDR-Moment nur
  in der Vorschau und schaltet kein HDR.
- **Thema**: **Automatisch** (wie der App-Modus von Windows), **Hell** oder **Dunkel**.
- **Effekte**: **Voll**, **Reduziert** (nur kurze Übergänge bis 0,3 s, keine Daueranimation) oder **Aus**, dazu
  **Beim Spielen anhalten**, **Im Hintergrund anhalten**, **Windows-Animationseffekte beachten** und **Nach dem Start in
  den Infobereich**. Die Stufe gilt überall: Bibliothek, Einstellungen, Dialoge und Tray-Menü. Daueranimationen laufen
  mit höchstens 30 Bildern pro Sekunde und nur, solange etwas davon zu sehen ist; bei geschlossenem Fenster läuft gar
  nichts.
- **Bilder**: wie viele Bilder je Launcher lokal gefunden und genutzt werden, je Ersatzfall ein Beispiel aus deiner
  Bibliothek und **SteamGridDB (online)**.
- **Symbole und Tray**: Tray- und Fenstersymbol in allen Zuständen und in echter Größe, das Tray-Menü und eine
  Taskleiste zur Vorschau (**Hell** oder **Dunkel**, nur für die Vorschau).

**SteamGridDB (online)** ist die einzige Bildquelle aus dem Netz und standardmäßig aus. Eingeschaltet und mit eigenem
API-Schlüssel von steamgriddb.com (eintragen, dann **Prüfen**) lädt HDRpilot nur Titelbilder, Logos und Cover, die lokal
fehlen und die du nicht selbst gewählt hast: nach dem Einschalten, nach dem Prüfen eines Schlüssels, mit
**Fehlende Bilder jetzt laden** und höchstens einmal am Tag eine Minute nach dem Start, nie während eines Spiels. Der
Schlüssel wird verschlüsselt (DPAPI) für dein Windows-Konto gespeichert und nie ins Protokoll geschrieben; die Zeile
darunter zeigt den letzten Netzwerkzugriff, **Schlüssel entfernen** löscht ihn. Aus oder ohne Schlüssel gibt es keinen
Netzwerkzugriff dafür: Ausschalten, Schlüssel entfernen oder ein Spielstart halten auch einen laufenden Abruf sofort an.
Ausgeschaltet zeigt HDRpilot wieder die lokalen bzw. erzeugten Bilder; die geladenen bleiben für ein erneutes
Einschalten liegen.

## So funktioniert es

**Erkennen (Weg A):** HDRpilot fragt einmal pro Sekunde die Prozessliste ab. Startet ein Programm aus dem
Installationsordner eines Spiels mit „HDR automatisch“, schaltet HDRpilot HDR ein. Reine Launcher-Fenster,
Absturzmelder, Installer und Laufzeitpakete zählen nicht. Start-Helfer, die das Spiel selbst starten (etwa
`gamelaunchhelper.exe` oder `PlayRDR2.exe`), zählen bewusst schon als Spiel, damit HDR früh an ist. Die beim ersten
Lauf erkannten EXEs merkt sich HDRpilot und zeigt sie in den Details.

**Frühwarnung bei Epic und Steam:** Startest du ein Spiel mit „HDR automatisch“ aus dem Epic Games Launcher oder aus
Steam, liest HDRpilot das in deren eigenen Protokolldateien mit (nur lesend) und schaltet HDR schon 1–2 s vor dem
Spielprozess ein. Kommt das Spiel doch nicht, stellt die Startfrist HDR wieder zurück. Abschaltbar über
`launcherSignals` in `config.json`.

**Starten über HDRpilot (Weg B):** „Starten“ oder eine „(HDR)“-Verknüpfung (`HDRpilot.exe --run <Quelle:ID>`)
schaltet HDR zuerst ein, wartet die Bestätigung ab und startet erst dann das Spiel: über den Launcher-Link der Quelle
(etwa `steam://`), Spiele aus GOG und dem Quiver Launcher sowie selbst eingetragene direkt über ihre EXE. Das ist der
sichere Weg für Spiele, die HDR nur beim Start prüfen. Erscheint innerhalb der Startfrist (Standard 120 s) kein
erkannter Spielprozess, etwa weil der Launcher noch aktualisiert, stellt HDRpilot HDR wieder zurück. Solange ein
Programm aus dem Spielordner läuft (etwa ein Launcher-Fenster, das auf die Anmeldung wartet), wartet HDRpilot weiter,
höchstens 30 Minuten. Scheitert das Einschalten, startet das Spiel trotzdem. Doppelte Startanforderungen (doppelter
Doppelklick) übergeht HDRpilot.

**Steam-Startoption:** Mit `"…\HDRpilot.exe" --wrap -- %command%` in den Startoptionen eines Steam-Spiels startet Steam
zuerst HDRpilot. Läuft HDRpilot, schaltet es HDR ein, wartet die Bestätigung und noch 1,5 s ab, startet dann das Spiel
und beendet sich sofort (es hält nichts am Spiel fest). Wie bei „Starten“ gilt das auch bei pausierter
Automatik und für Spiele ohne „HDR automatisch“. Läuft HDRpilot nicht oder ist das Spiel unbekannt, startet das Spiel
sofort ohne HDR.

**Quiver Launcher:** Quiver ist portabel und liegt dort, wo du es entpackt hast. HDRpilot erkennt seinen Ordner an
`current\sq.version` mit der Kennung QuiverLauncher (bis Version 2.4.x an `Quiver.exe`) und sucht ihn nur lesend in
dieser Reihenfolge: Ordner, die es sich gemerkt hat; das Protokoll des Updaters von Quiver
(`%LOCALAPPDATA%\velopack\velopack_QuiverLauncher.log`); deine Suchordner und ihre Unterordner;
`%LOCALAPPDATA%\QuiverLauncher` und ein Eintrag unter
`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\QuiverLauncher` (falls Quiver je mit einem Setup kommt).
Findet das nichts, sucht es in Downloads, Desktop, Dokumenten, im Benutzerordner und auf den festen Laufwerken bis
zwei Ebenen tief (höchstens 1500 Ordner, ohne Windows- und Programmordner, nur auf festen Laufwerken und ohne
verknüpften Ordnern zu folgen, also nie auf eine Netzfreigabe). Liegt Quiver woanders, merkt sich HDRpilot den Ordner,
sobald Quiver startet, während HDRpilot läuft, und liest wenige Sekunden später neu ein. Gelesen werden `apps.json`
und aus `settings.json` nur ein eigener Apps-Ordner (auch eine Laufwerkswurzel wie `G:\`); Quivers Dateien ändert
HDRpilot nie. „Starten“ startet die Spiel-EXE direkt mit ihrem Ordner als Arbeitsordner, wie Quiver selbst: die in
Quiver gewählte EXE, sonst die einzige oben im Spielordner, sonst die einzige bis drei Ebenen tief. Ein Startskript
(BAT oder CMD) zählt dabei wie in Quiver mit, gestartet wird es aber nie: Startet Quiver ein Skript, liegen mehrere
Kandidaten im Spielordner oder liegt die EXE noch tiefer, kennt HDRpilot keine Start-EXE. Dann startest du das Spiel
einmal in Quiver und wählst dort die EXE, wenn Quiver danach fragt; nach „Neu einlesen“ geht es auch über HDRpilot.
Als installiert gilt ein Spiel wie in Quiver, sobald irgendwo im Spielordner eine EXE, BAT oder CMD liegt (gesucht bis
16 Ebenen tief, höchstens 3000 Ordner; endet die Suche an dieser Grenze, gilt es als installiert). Spiele, die Quiver
über den Windows Installer installiert hat, starten über die EXE, die Quiver dafür verknüpft hat; ihr Ordner gilt als
Installationsordner. Erkannt wird ein laufendes Spiel immer über seinen Ordner. Steht dasselbe Spiel in zwei
Quiver-Ordnern (etwa während des Umzugs von Quiver 2.4 auf 3.x), gilt der Ordner von 3.x, sonst der zuletzt gespielte.
Hast du ein Quiver-Spiel früher selbst eingetragen, steht es zweimal in der Bibliothek; die Details beider Einträge
weisen darauf hin, entfernt wird nichts von selbst. Das Cover liest HDRpilot aus dem Ordner `Cache` von Quiver,
„Zuletzt gespielt“ aus Quivers `LastPlayed.txt` (ein Datum in einem anderen Kalender, etwa dem thailändischen mit dem
Jahr 2569, zählt nicht).

**Zurückstellen:** Vor dem Schalten merkt sich HDRpilot den HDR-Zustand jedes betroffenen Bildschirms und speichert
ihn sofort. Wenn das letzte HDR-Spiel beendet ist und die Nachlaufzeit (10 s) verstrichen ist, stellt HDRpilot diesen
Zustand wieder her. Laufen mehrere Spiele, wird erst nach dem letzten zurückgestellt, und nur, wenn alle beteiligten
Spiele „Nach dem Spiel zurückstellen“ an haben. Diese Einstellung gilt ab dem nächsten Spielstart. War HDR schon vor
dem Spiel an, schaltet HDRpilot am Ende nichts. Schaltest du HDR während des Spiels von Hand um, überschreibt
HDRpilot das nicht; am Ende schaltet es HDR nur aus, wenn es vor dem Spiel aus war, und nie wieder ein.

**Neustart-Puffer:** Endet ein Spiel weniger als 5 Minuten nach dem Start (typisch für Hänger oder Abstürze) und hat
HDRpilot HDR dafür eingeschaltet, bleibt HDR noch eine Weile an statt nur die Nachlaufzeit: Standard 1 Minute,
einstellbar unter „Einstellungen“ in Minuten und Sekunden (0:00 bis 30:00, 0:00 schaltet den Puffer ab; ist er nicht
länger als die Nachlaufzeit, wirkt er wie 0:00). Eine Änderung gilt ab dem nächsten Spielende, ohne Neustart von
HDRpilot. Startest du das Spiel in dieser Zeit neu, ist HDR schon an (hast du es zwischendurch von Hand ausgeschaltet,
schaltet HDRpilot es bei Spielen mit „HDR automatisch“ wieder ein). Läuft beim Ablauf des Puffers eine
Vollbild-Anwendung, wartet HDRpilot mit dem Zurückstellen, bis sie endet (höchstens 30 Minuten; nur nach dem Puffer,
nie nach der Nachlaufzeit). Spiele, die HDRpilot nach einem eigenen Neustart übernimmt, gelten nie als kurz.

**Hängender Spielprozess:** Läuft ein Spiel nach dem Spielen unsichtbar weiter (kein Fenster, keine Rechenzeit), lässt
HDRpilot HDR an, weil das Spiel ja noch läuft. Nach 2 Minuten ohne jede Aktivität erscheint einmal ein Hinweis, dass das
Spiel im Hintergrund zu hängen scheint; läuft gerade ein Vollbild-Programm oder ist der Bildschirm gesperrt, wartet
der Hinweis, bis er wieder angezeigt werden kann. HDRpilot beendet nichts selbst.

**Beenden, Absturz oder Neustart:** Wird HDRpilot während eines Spiels oder in der Nachlaufzeit beendet (auch über
„HDRpilot beenden“, durch einen Absturz oder beim Abmelden), bleibt HDR zunächst an. Beim nächsten Start stellt
HDRpilot den gemerkten Zustand wieder her, sobald kein Spiel aus diesem Zustand mehr läuft.

**Bildschirm ohne HDR oder nicht angeschlossen:** HDRpilot meldet das (während eines Vollbild-Spiels nur im
Protokoll) und lässt das Spiel normal laufen; es blockiert nie einen Spielstart. Bei Änderungen an den Bildschirmen
(anschließen, Auflösung, Aufwachen aus dem Ruhezustand) holt HDRpilot Ausstehendes nach: Ein Bildschirm, der beim
Spielstart fehlte oder kein HDR konnte, wird dann eingeschaltet, und ein gescheitertes Zurückstellen wird wiederholt.

**Spielsymbole:** Je Spiel gilt die erste brauchbare Quelle, nur aus Dateien auf dem PC (nur lesend): ein eigenes
Symbol; bei Steam das offizielle Steam-Symbol (`steam\games\<Name>.ico` laut Steams App-Daten, sonst das Symbol des
Eintrags „Steam App <ID>“ unter „Installierte Apps“, einer Steam-Verknüpfung oder das 32-px-Bild aus Steams
Bibliothekszwischenspeicher); bei GOG `goggame-<ID>.ico` im Spielordner oder das Symbol aus dem Zwischenspeicher von
GOG Galaxy; bei Xbox-Spielen ihre Logos; bei Ubisoft das Symbol des Deinstallationseintrags; bei Epic, EA, Battle.net
und dem Quiver Launcher die Spiel-EXE. Brauchbar heißt: kein bekanntes Standardsymbol (EasyAntiCheat, Unreal, Epic
Online Services, Godot, Unity, Windows-Programmsymbol) und in der Datei mindestens 48 px groß. Die Spiel-EXE kommt bei
Steam deshalb nur zum Zug, wenn das Steam-Symbol fehlt, ein Standardsymbol ist oder kleiner als 48 px ist, während die
EXE mindestens 48 px hat. Anti-Cheat-Starter (EasyAntiCheat, BattlEye, EA Anti-Cheat, GameGuard, PunkBuster, Epic
Online Services), Absturzmelder, Installer und Werkzeuge liefern nie das Symbol, auch wenn du die Liste der
Hilfsprozesse änderst; bei Unreal-Spielen zählt zuerst `<Projekt>\Binaries\Win64\*-Shipping.exe`.
Findet sich gar kein echtes Symbol, sucht HDRpilot bis drei Ordnerebenen tief (höchstens 300 Ordner und 1 s). Die
gewählte Quelle merkt es sich in `icon-cache.json`, damit die Liste nach einem Neustart keine Spielordner durchsucht.
Symbole lädt HDRpilot nur für sichtbare Zeilen und nur, solange die Bibliothek offen ist.

**Spielbilder:** Titelbild, Logo und Cover liest HDRpilot nur aus den Bild-Zwischenspeichern der Launcher: Steam
`appcache\librarycache` (deutsche Fassungen zuerst), GOG Galaxy `webcache`, Ubisoft Connect `cache\assets`, bei
Xbox-Spielen das Kachelbild im Paket und beim Quiver Launcher das Cover aus seinem Ordner `Cache` (dein eigenes Cover in
Quiver, sonst die Kopie, die Quiver geladen hat; neue Bilder von Quiver erscheinen nach dem nächsten Einlesen). Epic, EA
und Battle.net haben keinen solchen Speicher; dort, bei Quiver und bei selbst eingetragenen Spielen kommt das Symbol aus
der Spiel-EXE. Fehlt ein Bild, erzeugt HDRpilot ein Ersatzbild: ein Titelbild aus den Farben des Symbols mit dem Namen
als Muster, ein Cover aus Titelbild oder Symbol mit dem Namen, statt eines Logos den Namen in der Titelschrift und ohne
Symbol ein Monogramm. Verschmierte Steam-Kapseln (gestreckte Kapselbilder als einziges Cover) erkennt HDRpilot und
ersetzt sie durch einen Ausschnitt aus dem Titelbild. Quadratische Cover von Quiver (wie meist, 480 × 480) beschneidet
es nicht, sondern setzt sie ganz oben in ein Cover im Hochformat und rechts in das erzeugte Titelbild, wie das
Kachelbild eines Xbox-Spiels; wie dieses füllt ihre Mitte auch das Vorschaubild in der Liste, und die Farben kommen
aus diesem Bild. Logos schneidet es auf ihren sichtbaren Teil zu.
Umgebungsfarben berechnet es je Bild einmal, die hellere Fassung des Titelbilds nur bei aktivem Bibliotheksfenster und
nie, solange ein Spiel läuft oder startet: kurz nachdem die Auswahl zur Ruhe gekommen ist, mit niedriger Priorität,
damit sie bereitliegt, wenn HDR angeht. Beides legt es mit den Ersatzbildern unter `%LOCALAPPDATA%\HDRpilot\art\` ab.
Bilddateien der Launcher öffnet es nur so lange, bis ihr Inhalt im Speicher liegt. Solange ein Fenster offen ist,
beobachtet es die Ordner der Launcher (bei Ubisoft Connect auch den Produkt-Cache, den Ordner von Quiver nicht), sodass
neue Bilder von selbst erscheinen; im Speicher hält es höchstens 192 MB Bilder. Bei geschlossenem Fenster gibt es weder
Beobachtung noch Bilder im Speicher (auch den Grafikspeicher des Fensters gibt HDRpilot wenige Sekunden nach dem
Schließen zurück), und Bilder von Spielen, die nicht mehr in der Bibliothek stehen, räumt es dann auf; Spiele einer
Quelle, die diese Version nicht kennt, zählen dabei als vorhanden. Eigene Bilder („Bild wählen…“, höchstens 32 MB und
100 Megapixel, größere Fotos werden verkleinert) stehen je Bildart in `config.json` (`artwork`) und liegen in
`art\custom\`.

**SteamGridDB:** nur eingeschaltet und mit eigenem Schlüssel (siehe „Einstellungen“). Gefragt wird
`www.steamgriddb.com`, Steam-Spiele über ihre App-ID, andere über den Namen (nur ein Treffer mit gleichem Namen zählt),
und nur nach unbewegten Bildern ohne NSFW- oder Spaß-Markierung. Bilder lädt HDRpilot nur von https-Adressen unter
steamgriddb.com, ohne Weiterleitungen, höchstens 15 MB je Bild, mit mindestens 1 s Abstand und höchstens 200 Anfragen
je Lauf; was SteamGridDB nicht hat, fragt es 30 Tage lang nicht erneut. Geladene Bilder liegen in `art\sgdb\`.

**Bildschirmwerte:** Spitze, Vollbild, Minimum, Farbtiefe und ob HDR an ist, liest HDRpilot über die
Grafikschnittstelle von Windows (DXGI), nur solange ein Fenster sie zeigt und nur nach einem HDR- oder
Bildschirmwechsel. Die Spitze bei eingeschaltetem HDR merkt es sich in `display-luminance.json`; sie ist das Maximum
jeder Nit-Skala, auch wenn HDR gerade aus ist. Gibt es noch keine Messung mit HDR, gilt der aktuelle Wert. Meldet
Windows keine Werte (etwa bei ausgeschaltetem Bildschirm), steht dort „—“.

**Schalten:** über die Windows-Anzeige-Schnittstelle (DisplayConfig), danach wird der Zustand jedes Mal erneut gelesen
und so bestätigt. Ab Windows 11 24H2 (Build 26100) nutzt HDRpilot die neue HDR-Schnittstelle, sonst die ältere
„Advanced Color“-Schnittstelle. Meldet die neue Schnittstelle einen Fehler, weicht HDRpilot automatisch auf die ältere
aus.

## HDR-Liste (PCGamingWiki)

HDRpilot zeigt, welche deiner Spiele laut [PCGamingWiki](https://www.pcgamingwiki.com) HDR unterstützen. Abgeglichen
wird 30 s nach jedem Start im Hintergrund, höchstens einmal pro Stunde, und jederzeit über **Jetzt abgleichen**.

- **Ohne Konto (Standard):** HDRpilot fragt bei PCGamingWiki nach den Seiten deiner Spiele; dabei gehen die
  Spielnamen an PCGamingWiki, sonst nichts. Je Start höchstens 100 Spiele (2 Anfragen plus 2 Suchen, 2 s Abstand),
  neue und kürzlich gespielte zuerst, bekannte alle 14 Tage neu. Bei einer großen Bibliothek ist nach einigen Starts
  alles geprüft. Steam- und GOG-Spiele werden über ihre Laden-ID zugeordnet, andere über den Namen.
- **Mit eigenem PCGamingWiki-Konto:** Unter Einstellungen → „HDR-Liste (PCGamingWiki)“ führen **Konto anlegen** und
  **Bot-Passwörter** zu PCGamingWiki. Dort ein Bot-Passwort mit dem Namen HDRpilot anlegen und nur das Recht „Create,
  query and delete data through the Cargo extension“ wählen. Benutzername (`Name@HDRpilot`) und Bot-Passwort in
  HDRpilot eintragen und **Speichern und testen**. Dann lädt HDRpilot die vollständige Liste und gleicht sie nur auf
  deinem PC ab; Spielnamen werden nicht gesendet. Das Passwort liegt mit Windows (DPAPI) verschlüsselt in
  `pcgw-account.dat` und ist nur für deinen Windows-Benutzer lesbar; **Konto entfernen** löscht es.

Spiele aus dem Quiver Launcher gleicht HDRpilot in beiden Fällen nicht ab: Meist sind es Umsetzungen älterer Spiele,
deren Name die Seite des Originals träfe. Ihre Namen gehen deshalb nicht an PCGamingWiki, und sie stehen als „HDR
UNBEKANNT“.

Ohne Netz bleibt der zuletzt geladene Stand erhalten. PCGamingWiki ist ein kleines Gemeinschaftsprojekt; HDRpilot hält
sich an dessen Anfragegrenzen und speichert die Ergebnisse zwischen. Die HDR-Angaben stehen unter der Lizenz
CC BY-NC-SA 3.0.

## Windows Auto HDR

„Windows Auto HDR“ ist eine Funktion von Windows: Sie rechnet das SDR-Bild eines Spiels, das selbst kein HDR kann, in
HDR um. HDRpilot schaltet sie auf Wunsch je Spiel für dich ein, wenn du das Spiel über HDRpilot startest. Das ist etwas
anderes als „HDR automatisch“:

| | „HDR automatisch“ | „Windows Auto HDR“ |
|---|---|---|
| Was passiert | HDRpilot schaltet Windows-HDR beim Spielstart ein und danach zurück | Windows rechnet das SDR-Bild in HDR um; HDRpilot schaltet dafür den Auto-HDR-Schalter von Windows direkt vor dem Start ein und danach zurück |
| Wann | bei jedem erkannten Spielstart, auch ohne HDRpilot | nur bei einem Start über HDRpilot: „Starten“, „(HDR)“-Verknüpfung, Steam-Startoption (dazu die Frühwarnung von Steam und Epic) |
| Wo | Abschnitt „AUTOMATIK“ in den Details und Schalter in der Liste | Abschnitt „BILD“ in den Details, in der Liste nur das Kennzeichen „AUTO HDR“ |
| Braucht | einen HDR-fähigen Bildschirm | Windows-HDR schon vor dem Spielstart an und ein Spiel mit DirectX 11 oder 12, das Windows für Auto HDR zulässt |

Beide Schalter sind unabhängig voneinander. „Starten“, „(HDR)“-Verknüpfungen und die Steam-Startoption schalten
HDR ohnehin vor dem Spiel ein; ist „HDR automatisch“ für das Spiel aus, steht unter dem Schalter ein Hinweis, dass HDR
sonst schon vor dem Spielstart an sein muss (bei eingeschaltetem Auto HDR immer, sonst hinter **Mehr erfahren**). Hat
ein Spiel laut PCGamingWiki eigenes HDR (Kennzeichen „HDR“ oder „HDR*“), sind Titel und Schalter blasser, und hinter
**Mehr erfahren** rät ein Hinweis, das HDR des Spiels zu nehmen, das meist besser aussieht; einschalten lässt sich Auto
HDR trotzdem.

**So funktioniert es:** Windows wendet Auto HDR nur an, wenn beim Start des Spielprozesses Windows-HDR und der Schalter
„Auto HDR“ der Windows-Einstellungen (System → Anzeige → HDR) an sind. Dieser Schalter steht als Token
`AutoHDREnable=1` im Wert `DirectXUserGlobalSettings` unter `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`.
Startest du ein Spiel mit „Windows Auto HDR“ über HDRpilot oder greift die Frühwarnung, setzt HDRpilot direkt davor nur
dieses Token (alle anderen Einträge im Wert, ihre Reihenfolge und der Abschluss bleiben), schaltet HDR ein und startet
dann das Spiel. Den Wert von vorher merkt es sich in `autohdr-global.json`. Zurückgestellt wird nach dem Spiel wie der
HDR-Zustand: nach der Nachlaufzeit, bei einem Spiel, das kürzer als 5 Minuten lief, nach dem Neustart-Puffer (Standard
1 Minute); startet das Spiel in dieser Zeit neu, bleibt der Schalter an. Laufen mehrere solche Spiele, bleibt er an, bis
das letzte endet. Erscheint kein Spielprozess, stellt HDRpilot ihn mit dem Ende der Startfrist zurück, scheitert schon
der Start-Befehl, sofort. Beim Beenden stellt HDRpilot den Schalter sofort zurück, nach einem Absturz beim nächsten
Start, und das Entfernen über den Setup erledigt einen Rest. Während der Schalter an ist, gilt Auto HDR für jedes Spiel,
das in dieser Zeit startet.

Ohne Start über HDRpilot schaltet HDRpilot Auto HDR nie, auch nicht, wenn es ein Spiel mit der Option laufen sieht:
Windows hat den Schalter dann schon beim Spielstart gelesen. Aus demselben Grund setzt ein Start über HDRpilot nichts,
solange das Spiel schon läuft (etwa ein zweiter Klick auf **Starten** oder die „(HDR)“-Verknüpfung). HDRpilot
fasst nur Werte aus reinem Text mit `Schlüssel=Wert;`-Paaren an und schreibt keine Werte je Spiel-EXE, nur unter HKCU,
nie unter HKLM, und braucht keine Administratorrechte.

**Deine Wahl bleibt:** War Auto HDR in Windows schon an, ändert HDRpilot nichts und schaltet es auch nicht aus. Stellst
du den Schalter in Windows um, während HDRpilot ihn hält, bleibt deine Einstellung, und das Protokoll vermerkt „extern
geändert“ (schaltest du ihn dort aus und gleich wieder ein, sieht HDRpilot keinen Unterschied und stellt danach
zurück). Den Windows-Schalter selbst schaltest du auch im Menü des Infobereichs (**Auto HDR**): Das gilt ab dem nächsten
Spielstart, auch ohne HDRpilot, und ist immer deine Wahl. Hält HDRpilot den Schalter gerade für ein Spiel, gibt es ihn
ab, stellt danach nichts zurück und setzt ihn für die laufenden Spiele nicht neu. Jeder neue Start über HDRpilot liest
den Schalter aber neu: Ist er aus, setzt dieser Start ihn für sein Spiel und stellt danach deinen Stand wieder her.

**Steam-Spiele:** Startest du ein Steam-Spiel direkt in Steam, erfährt HDRpilot vom Start zuverlässig nur über die
Steam-Startoption; die Frühwarnung aus Steams Protokoll kommt nicht immer vor dem Spielprozess. Schaltest du „Windows
Auto HDR“ für ein Steam-Spiel ein, prüft HDRpilot deshalb (nur lesend in Steams `localconfig.vdf`), ob die Startoption
schon eingetragen ist. Fehlt sie, fragt es, ob es sie in die Zwischenablage kopieren soll, wie
**Steam-Startoption kopieren**. Eintragen musst du sie in Steam selbst (Rechtsklick auf das Spiel → Eigenschaften →
Allgemein → Startoptionen); Steams Dateien ändert HDRpilot nie. Ablehnen ändert nichts, Auto HDR bleibt an und wirkt
dann beim Start über HDRpilot oder die „(HDR)“-Verknüpfung, beim Start direkt aus Steam nur, wenn die Frühwarnung vor
dem Spielprozess kommt.

**Livetest (Windows 11 26H2, Build 26300, 2026-10-12):**

- TUNIC (Unity, DirectX 11): Auto HDR in Windows an und HDR vor dem Start an, dann ist der Regler „HDR-Intensität
  anpassen“ in der Game Bar bedienbar. Der Registry-Wert ist derselbe Schalter wie in den Windows-Einstellungen.
- Nur der Wert je Spiel-EXE (`AutoHDREnable=2097`) bei ausgeschaltetem Auto HDR wirkt nicht. Deshalb nimmt HDRpilot den
  globalen Schalter.
- Auto HDR erst einschalten, während das Spiel läuft, oder HDR erst nach dem Spielstart einschalten wirkt erst beim
  nächsten Start. Deshalb setzt HDRpilot beides vor dem Spielprozess.

**Prüfen:** Wirkt Auto HDR, ist in der Xbox Game Bar (Win+G) der Regler für die Auto-HDR-Intensität bedienbar. Das
Einstellungsfenster der Game Bar zeigt einen alten Stand, bis du es ganz schließt und neu öffnest.

RTX HDR von NVIDIA gehört nicht zu HDRpilot; das geht in der NVIDIA App.

## Anti-Cheat

HDRpilot greift nicht in Spiele ein und ist so gebaut, dass Anti-Cheat-Systeme keinen Anlass zum Eingreifen haben:

- Es liest nur die Prozessliste von Windows. Den EXE-Pfad eines Programms erfragt es bei Windows, ohne das Programm
  selbst zu öffnen; ein Spiel bekommt von HDRpilot also gar nichts mit. Für den Hänger-Hinweis fragt es Rechenzeit und
  sichtbare Fenster ebenfalls nur bei Windows ab.
- Es liest und schreibt keinen Spielspeicher, schleust keinen Code ein, setzt keine Hooks, legt kein Overlay über das
  Spiel und ändert weder Priorität noch Prozessorzuteilung von Spielen. Die Protokolle von Epic und Steam liest es nur.
- Es lädt keinen Treiber und braucht keine Administratorrechte. Netzwerk nutzt es nur für die HDR-Liste und, wenn du
  es einschaltest, für SteamGridDB.
- HDR schaltet es über die Anzeige-Einstellungen von Windows, wie der HDR-Schalter in den Windows-Einstellungen.
- „Windows Auto HDR“ schaltet es nur über den Auto-HDR-Schalter in den Grafikeinstellungen von Windows unter HKCU, wie
  der Schalter in den Windows-Einstellungen selbst, und zwar vor dem Spielstart; das Spiel und seine Dateien fasst es
  dafür nicht an. Eine Aussage der Anti-Cheat-Hersteller dazu gibt es nicht.
- Künstliche Tastendrücke gibt es nur in der Tastenkürzel-Notlösung. Sie ist ab Werk aus und sendet nie etwas,
  solange ein Spiel läuft oder eine Vollbild-Anwendung im Vordergrund ist, auch nicht beim Umschalten von Hand.
- Launcher-Spiele startet es über den Launcher-Link, sodass deren Anti-Cheat-Starter wie gewohnt läuft. Die
  Steam-Startoption startet genau den Befehl, den Steam vorgibt, und beendet sich sofort.

Ein automatischer Test stellt sicher, dass solche Techniken nicht in den Quelltext gelangen. Eine Garantie gibt es
trotzdem nicht, denn die Regeln der Anti-Cheat-Hersteller sind nicht öffentlich. Meldet ein Anti-Cheat HDRpilot (etwa
„Hack attempt detected“ bei nProtect GameGuard), HDRpilot vor diesem Spiel beenden und den Fall dem Spielhersteller
melden. Selbst eingetragene Spiele mit Anti-Cheat (etwa mit einem Ordner EasyAntiCheat oder BattlEye) über ihr
Start-Programm eintragen, zum Beispiel `start_protected_game.exe`, nicht über die eigentliche Spiel-EXE.

## Speicherorte

| Was | Wo |
|---|---|
| Programm (Setup): HDRpilot.exe, `cli\hdrpilot.exe`, README, `LICENSE`, Lizenzhinweise | `%LOCALAPPDATA%\Programs\HDRpilot\` |
| Eintrag unter „Installierte Apps“ (Setup) | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\HDRpilot` |
| Verknüpfung im Startmenü (Setup) | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\HDRpilot.lnk` |
| Protokoll von Setup und Entfernen | `%TEMP%\HDRpilot-Setup.log` |
| Einstellungen und Spieleliste | `%APPDATA%\HDRpilot\config.json` |
| Gemerkter HDR-Zustand während eines Spiels | `%LOCALAPPDATA%\HDRpilot\session-state.json` |
| Protokoll (täglich neue Datei, höchstens 5 MB je Datei, 14 Tage) | `%LOCALAPPDATA%\HDRpilot\logs\` |
| Protokoll der Steam-Startoption (eine Zeile je Start, 256 KB) | `%LOCALAPPDATA%\HDRpilot\logs\wrap.log` |
| HDR-Angaben je Spiel | `%LOCALAPPDATA%\HDRpilot\hdr-support.json` |
| Vollständige HDR-Liste (nur mit Konto) | `%LOCALAPPDATA%\HDRpilot\hdr-catalog.json` |
| PCGamingWiki-Konto (verschlüsselt, nur mit Konto) | `%LOCALAPPDATA%\HDRpilot\pcgw-account.dat` |
| Eigene Spielsymbole (je Spiel eine PNG mit 96 px, für Verknüpfungen dazu eine ICO) | `%LOCALAPPDATA%\HDRpilot\icons\` |
| Gemerkte Quelle der Spielsymbole | `%LOCALAPPDATA%\HDRpilot\icon-cache.json` |
| Spielbilder: Verzeichnis `index.json` (Herkunft und Farben je Spiel), erzeugte Ersatzbilder, zugeschnittene Logos, helle HDR-Fassungen, eigene Bilder und Bilder von SteamGridDB | `%LOCALAPPDATA%\HDRpilot\art\` |
| SteamGridDB-Schlüssel (verschlüsselt, nur wenn eingetragen) | `%LOCALAPPDATA%\HDRpilot\steamgriddb-key.dat` |
| Zuletzt bei eingeschaltetem HDR gemessene Helligkeit je Bildschirm (Maximum der Nit-Anzeigen) | `%LOCALAPPDATA%\HDRpilot\display-luminance.json` |
| Auto-HDR-Schalter von Windows samt altem Wert, solange HDRpilot ihn für einen Spielstart hält | `%LOCALAPPDATA%\HDRpilot\autohdr-global.json` |
| Vermerk „erledigt“, falls sich `autohdr-global.json` nicht löschen ließ (etwa vom Virenscanner gesperrt); beide werden gelöscht, sobald es geht | `%LOCALAPPDATA%\HDRpilot\autohdr-global.done` |
| Auto HDR von Windows (globaler Schalter, Einstellung von Windows) | `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, Wert `DirectXUserGlobalSettings` |
| Autostart | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, Wert `HDRpilot` |
| Verknüpfungen | Desktop bzw. `%APPDATA%\Microsoft\Windows\Start Menu\Programs\HDRpilot\`, „<Spiel> (HDR).lnk“ |
| Entpackte Grafik-Bibliotheken (rund 8 MB je Version) | `%TEMP%\.net\HDRpilot\` |
| Dasselbe für den Setup (rund 8 MB je Version) | `%TEMP%\.net\HDRpilot-Setup-<Version>\` |
| Dasselbe für das Entfernen (rund 8 MB, löscht sich danach selbst) | `%TEMP%\.net\HDRpilot-Uninstall\` |

Wer schon eine frühere Version hatte: Windows unterscheidet bei Ordnernamen nicht nach Groß- und Kleinschreibung, die
bisherigen Ordner `HdrPilot` werden also weiter benutzt, auch als Programmordner des Setups. Setup und Entfernen
ändern nichts an Einstellungen und Daten; nur beim Entfernen mit Haken (siehe „Entfernen“) werden sie gelöscht. Einzige
Ausnahmen: Das Entfernen stellt einen gemerkten HDR-Zustand wieder her und verwirft danach `session-state.json`, und es
stellt einen von HDRpilot gehaltenen Auto-HDR-Schalter zurück und löscht danach `autohdr-global.json` samt einem
Vermerk `autohdr-global.done` (ließ er sich nicht zurückstellen, bleibt die Datei stehen).

Zu jedem Spiel stehen in `config.json` auch die Symbol-Hinweise der Launcher (`iconCandidates`, meist eine Datei),
bei Quiver-Spielen die Cover-Dateien aus Quivers Zwischenspeicher (`coverCandidates`), und gegebenenfalls das eigene
Symbol (`customIcon`, nur der Dateiname im Ordner `icons`).

Einstellungen gelten je Spiel über Quelle und ID (etwa `Steam:1091500`), nicht über den Namen. `config.json` wird
atomar geschrieben. Von Hand bearbeitest du sie am besten bei beendetem HDRpilot. Ist sie danach kein gültiges JSON,
sichert HDRpilot sie beim nächsten Start als `config.json.defekt-<Zeit>.bak` und beginnt mit Standardwerten; deine
Auswahl steckt dann nur noch in dieser Sicherung. Spiele einer Quelle, die diese Version nicht kennt (etwa aus einer
neueren Version), lässt HDRpilot dagegen unverändert in der Datei stehen und liest den Rest wie gewohnt.

Die Grafik-Bibliotheken entpackt HDRpilot beim ersten Start und nach jedem Update auf eine neue Version. Ordner
älterer Versionen bleiben liegen und können bei beendetem HDRpilot gelöscht werden. Setup-Datei und die Kopie zum
Entfernen entpacken ihre eigenen (Ordner nach dem Namen der EXE). Den der Setup-Datei
(`%TEMP%\.net\HDRpilot-Setup-<Version>`) darfst du löschen, wenn gerade kein Setup läuft; den der Kopie zum Entfernen
(`%TEMP%\.net\HDRpilot-Uninstall`) löscht sie am Ende zusammen mit ihrem Temp-Ordner selbst.

## Befehlszeile

### HDRpilot.exe

Läuft HDRpilot schon, reicht ein zweiter Aufruf seine Befehle an die laufende Instanz weiter.

| Aufruf | Wirkung |
|---|---|
| `HDRpilot.exe` | Starten (nur im Infobereich); läuft es schon, öffnet sich die Spielebibliothek |
| `--minimized` | Starten (nur im Infobereich); läuft es schon, passiert nichts. So startet der Autostart |
| `--show` / `--settings` | Spielebibliothek bzw. Einstellungen öffnen |
| `--run <Quelle:ID>` | Spiel über HDRpilot starten (HDR vorher an) |
| `--toggle-hdr` | HDR jetzt umschalten |
| `--pause` / `--resume` | Automatik pausieren bzw. fortsetzen |
| `--rescan` | Spiele neu einlesen |
| `--exit` | Laufende Instanz beenden |
| `--wrap -- <befehl>` | Steam-Startoption (`--wrap -- %command%`): HDR einschalten lassen, dann den Befehl starten |
| `--config-dir <ordner>` | Eigene Ablage statt `%APPDATA%`/`%LOCALAPPDATA%` (Tests); eigene Instanz je Ordner |
| `--autohdr-test-root <name>` | Nur mit `--config-dir` (Tests): Auto-HDR-Schalter unter `HKCU\Software\HDRpilot.Tests\AutoHdr\<name>`. Mit eigener Ablage schreibt HDRpilot nie in die echte Windows-Einstellung, ohne diesen Schalter unter einem aus dem Ordner abgeleiteten Namen |

Den Schlüssel `<Quelle:ID>` zeigt die Oberfläche nicht an. Du findest ihn in der ersten Spalte von
`cli\hdrpilot.exe games list` oder im Ziel einer „(HDR)“-Verknüpfung hinter `--run`. Meist ist es einfacher, die
Verknüpfung in den Details anlegen zu lassen.

### Setup-Datei und Entfernen

Für Skripte und die Fehlersuche; im Alltag reichen das Setup-Fenster und „Installierte Apps“.

| Aufruf | Wirkung |
|---|---|
| `HDRpilot-Setup-1.5.0.exe --quiet` | Installieren oder aktualisieren ohne Fenster; das Ergebnis ist der Exit-Code. Danach startet HDRpilot (lief es nicht, mit der Spielebibliothek), außer mit `--no-launch` oder mit Administratorrechten. Skripte, die auf das Ende warten oder die Ausgabe lesen, nehmen `--no-launch` |
| `--autostart on\|off` | Mit Windows starten, danach mit der installierten EXE (ohne Angabe: wie bisher, also an, wenn schon eine HDRpilot.exe mit Windows startet, auch aus einem anderen Ordner; bei einer neuen Installation an) |
| `--desktop-shortcut on\|off` | Desktop-Verknüpfung (ohne Angabe: wie bisher, bei einer neuen Installation aus) |
| `--no-launch` | HDRpilot danach nicht starten. Für Skripte, die auf das Ende warten (etwa `Start-Process -Wait`, das auch auf gestartete Programme wartet) oder die Ausgabe lesen |
| `--allow-downgrade` | Eine ältere Version über eine neuere installieren |
| `HDRpilot.exe --uninstall` | Entfernen wie aus „Installierte Apps“ (läuft aus einer Kopie im Temp-Ordner) |
| `--quiet` | Entfernen ohne Fenster: kehrt sofort zurück. 0 heißt, die Kopie im Temp-Ordner entfernt jetzt; ihr Ergebnis steht danach als `ERGEBNIS: <Exit-Code>` im Protokoll. 1 (Kopie nicht gestartet), 2 (Aufruf falsch) und 7 (Container) kommen direkt vom ersten Aufruf, ohne `ERGEBNIS`-Zeile; dann wurde nichts entfernt |
| `--remove-data` | Beim Entfernen auch Einstellungen, Spieleliste, Protokolle und HDR-Liste löschen |
| `--test-root <ordner>` | Prüfläufe: Programmordner, Verknüpfungen und Ablage unter diesem Ordner, dazu ein eigener Registry-Zweig unter `HKCU\Software\HDRpilot.Tests` |

Exit-Codes: 0 ok, 1 Fehler, 2 Aufruf falsch, 3 ältere Version abgelehnt, 4 HDRpilot ließ sich nicht beenden,
5 abgebrochen, 6 nicht installiert, 7 aus dem Container einer anderen App gestartet, nichts geändert (siehe
„Fehlersuche“). Beide schreiben ihr Protokoll nach `%TEMP%\HDRpilot-Setup.log`.

### cli\hdrpilot.exe

Werkzeug für Fehlersuche und Skripte, im Alltag nicht nötig.

```
hdrpilot hdr status [--json] [--api modern|legacy]
hdrpilot hdr on|off|toggle [--display <auswahl>] [--api modern|legacy] [--hotkey-fallback] [--json]
hdrpilot scan [--source <quelle>] [--search "<ordner>;<ordner>"] [--config-dir <ordner>]
hdrpilot games list [--json]
hdrpilot games import
hdrpilot games add --path <exe|ordner> [--name <name>] [--hdr on|off]
hdrpilot games set <schlüssel> [--hdr on|off] [--restore on|off] [--display <monitorDevicePath>|global]
hdrpilot watch [--verbose]
hdrpilot run <schlüssel>
```

`<auswahl>`: `primary` (Standard), `all`, Nummer aus `hdr status`, Teil des Namens oder `monitorDevicePath`.
`<quelle>`: Steam, Epic, Xbox, Gog, Ubisoft, EA, BattleNet, Quiver. `scan`, `games`, `watch` und `run` verstehen
außerdem `--config-dir <ordner>`; `scan` nimmt dann die Suchordner und gemerkten Quiver-Ordner dieser Ablage hinzu
(gelesen, nicht geschrieben) und sucht sonst wie immer. `hdrpilot --help` zeigt die vollständige Hilfe.

Exit-Codes: 0 ok, 1 Fehler, 2 Aufruf falsch, 3 Bildschirm nicht gefunden, 4 Bildschirm ohne HDR,
5 Schalten fehlgeschlagen, 6 Spiel nicht gefunden.

Die `games`-Befehle kannst du neben der laufenden App benutzen; HDRpilot liest die Änderungen selbst neu ein.
`hdrpilot watch` und `hdrpilot run` betreiben dagegen dieselbe Automatik wie die App und nutzen ohne `--config-dir`
dieselbe Ablage. Sie prüfen nicht, ob die App läuft: Dann schalten beide unabhängig und überschreiben sich den
gemerkten HDR-Zustand. Vorher HDRpilot beenden oder bei laufender App `HDRpilot.exe --run <Quelle:ID>` verwenden.

## Bekannte Grenzen

- **Erkennung per Abfrage:** Zwischen Spielstart und HDR liegt bis zu ein Abfrageintervall (Standard 1 s) plus die
  Schaltzeit, außer bei Epic und Steam mit Frühwarnung. Spiele, die HDR nur beim Start prüfen, über „Starten“,
  eine „(HDR)“-Verknüpfung oder (bei Steam) die Steam-Startoption starten.
- **Spiel-EXE außerhalb des Installationsordners:** Manche Starter starten die eigentliche EXE woanders. Solche Spiele
  erkennt HDRpilot erst, wenn du die EXE in den Details hinzufügst; bis dahin stellt auch „Starten“ HDR nach
  der Startfrist wieder zurück.
- **Mehrere Bildschirme:** HDRpilot erkennt nicht, auf welchem Bildschirm ein Spiel läuft, sondern schaltet die
  eingestellten (Standard: Hauptbildschirm). Läuft ein Spiel woanders, in den Details den richtigen **Bildschirm**
  wählen. Auf echter Hardware geprüft wurde nur mit einem Bildschirm.
- **Nicht automatisch gefunden** werden: in Steam eingetragene Fremdspiele, ältere Store-Spiele direkt unter
  `C:\Program Files\WindowsApps`, Xbox-Spiele auf USB- oder Netzlaufwerken, GOG-Spiele ohne Registry-Eintrag und
  Epic-Spiele ohne Manifest. Solche Spiele über „Spiel hinzufügen“ eintragen.
- **Start über die Launcher:** Mit echten Spielen geprüft: Steam (Dying Light: The Beast), Epic (Dead Island 2), GOG
  (Cyberpunk 2077), Ubisoft Connect (Avatar: Frontiers of Pandora), EA app (Battlefield 6, mit EA Javelin) und Xbox
  (Palworld), jeweils über „Starten“ und über die Verknüpfung. HDR war jedes Mal an, bevor der Launcher das
  Spiel gestartet hat. Battle.net ist ungeprüft (siehe unten).
- **Umschalten während des Spielstarts:** Bei der automatischen Erkennung schaltet HDRpilot HDR, während das Spiel
  gerade hochfährt. Das verkraften fast alle Spiele; Dead Island 2 blieb im Test bei einem von zwei automatischen
  Starts hängen (ob wegen des Umschaltens, ist nicht sicher). Der Neustart-Puffer hilft beim zweiten Versuch, wenn der
  Neustart in seine Zeit fällt (Standard 1 Minute; wer länger für den Neustart braucht, stellt ihn unter „Einstellungen“
  höher); sicherer sind „Starten“, die Verknüpfung oder die Steam-Startoption.
- **Frühwarnung:** nur für den Epic Games Launcher und Steam. Sie beruht auf deren Protokollformat, das sich mit einem
  Launcher-Update ändern kann; dann greift wieder die normale Erkennung.
- **Steam-Startoption:** wirkt nur, wenn HDRpilot läuft (Autostart empfohlen) und das Spiel beim letzten Einlesen schon
  bekannt war. Läuft Steam mit Administratorrechten, HDRpilot aber nicht, kann sich der Start um bis zu 8 s verzögern.
- **HDR-Liste:** Ohne Konto werden Spiele ohne Steam- oder GOG-ID nur gefunden, wenn ihr Name zur PCGamingWiki-Seite
  passt. Der Konto-Weg ist nur gegen nachgebildete Antworten getestet, weil beim Bau kein Konto vorhanden war. Die
  Angaben stammen von Freiwilligen und können lückenhaft sein.
- **Hänger-Hinweis:** Ältere Store-Spiele (UWP), deren Fenster einem Windows-Rahmenprozess gehört, könnten im
  minimierten Zustand fälschlich als hängend gemeldet werden. Es bleibt bei einem Hinweis.
- **GOG:** „Starten“ startet die Spiel-EXE direkt, an GOG Galaxy vorbei. Cloud-Speicherstände, Overlay und
  Spielzeit von Galaxy sind dann womöglich nicht aktiv.
- **EA app:** Die Bibliotheksdatei der EA app ist verschlüsselt und an die Hardware gebunden; HDRpilot entschlüsselt
  sie bewusst nicht und findet EA-Spiele über die Installationsdaten der Spiele selbst.
- **Battle.net:** Am Testrechner war kein Blizzard-Spiel installiert. Das Einlesen von Blizzard-Spielen ist deshalb nur
  mit nachgebildeten Daten getestet, das Starten über Battle.net gar nicht. Fehlt ein Blizzard-Spiel, seinen Ordner
  über „Spiel hinzufügen“ eintragen.
- **Quiver Launcher:** Keine Frühwarnung und keine Startoption wie bei Steam: Startest du ein Spiel in Quiver, schaltet
  HDRpilot HDR erst, wenn es den Spielprozess sieht (bis zu ein Abfrageintervall). Spiele, die HDR nur beim Start
  prüfen, über „Starten“ oder die „(HDR)“-Verknüpfung starten. Einen Quiver-Ordner außerhalb der durchsuchten Orte
  findet HDRpilot erst, wenn Quiver einmal läuft, während HDRpilot läuft, oder wenn er (oder der Ordner darüber) als
  Suchordner eingetragen ist; nach dem Verschieben gelten seine Spiele bis dahin als „Nicht installiert“. Quivers
  Dateien sind keine offizielle Schnittstelle und können sich mit einem Update ändern; was HDRpilot nicht lesen kann,
  meldet die Bibliothek als Fehler beim Einlesen. Titelbild und Logo hat Quiver nicht, die Cover sind meist WebP
  (braucht die WebP-Erweiterung von Windows). Startskripte (BAT, CMD) startet HDRpilot nie, auch wenn Quiver ein Spiel
  so startet. Ersetzt Quiver eine Datei genau in der Millisekunde, in der HDRpilot sie liest (ein Bild in `Cache` oder
  die Verknüpfung `windows-installer.json`), meldet Windows Quiver einmal „Zugriff verweigert“; HDRpilot hält jede Datei
  nur so kurz offen, wie das Lesen dauert. Am Testrechner geprüft mit Quiver 3.5.1 und einem Spiel („Black“), nur
  lesend; Spiele aus dem Windows Installer und Quiver bis 2.4.x nur mit nachgebildeten Daten, der Start aus Quiver mit
  HDR noch nicht.
- **Ältere Version nach Quiver-Spielen:** HDRpilot 1.4.0 und älter kennen die Quelle „Quiver“ nicht. Steht ein
  Quiver-Spiel in `config.json`, halten sie die ganze Datei für defekt, sichern sie als
  `config.json.defekt-<Zeit>.bak` und beginnen leer. Der Setup installiert eine ältere Version deshalb nur mit
  **Trotzdem die ältere Version installieren**; ab dieser Version bleiben Spiele unbekannter Quellen erhalten.
- **Windows 10:** Die ältere Schnittstelle ist mit Tests abgedeckt und wurde auf echter Hardware unter Windows 11
  (Build 26200) mitgeprüft; ein echtes Windows 10 stand nicht zur Verfügung.
- **Tastenkürzel-Notlösung:** HDRpilot sendet Win+Alt+B nur, wenn sie eingeschaltet ist, die Windows-Schnittstelle
  gescheitert ist und gerade kein Spiel läuft (siehe „Anti-Cheat“). Das Kürzel braucht die Xbox Game Bar, schaltet nur
  um und wirkt auf alle HDR-fähigen Bildschirme; HDRpilot bestätigt aber nur den Zielbildschirm. Auf echter Hardware
  ist die Notlösung nicht getestet.
- **Spielsymbole:** Für manche Steam-Spiele liegt auf dem PC nur ein 32-px-Symbol; in der Liste wirkt es etwas
  unscharf (gemessen: 24 von 636). Bilder im WebP-Format (GOG Galaxy) braucht die WebP-Erweiterung von Windows; fehlt
  sie, nimmt HDRpilot die nächste Quelle. Ein eigenes Symbol gilt nur auf diesem PC, weil die PNG im lokalen Datenordner
  liegt; auf einem anderen PC zeigt das Spiel wieder das automatische Symbol.
- **Windows Auto HDR:** wirkt nur bei Spielen mit DirectX 11 oder 12 (nicht bei DirectX 9, OpenGL und Vulkan), nur bei
  Spielen, die Windows dafür zulässt (vorab nicht abfragbar), nur bei einem Start über HDRpilot („Starten“,
  „(HDR)“-Verknüpfung, Steam-Startoption, Frühwarnung) und nur, wenn Windows-HDR schon vor dem Spielstart an ist; ein
  Spiel, das schon läuft, bekommt es erst beim nächsten Start (Livetest unter Windows 11 26H2). Solange HDRpilot den
  Schalter hält, gilt Auto HDR auch für andere Spiele, die in dieser Zeit starten. Das Format der Windows-Einstellung ist
  nicht dokumentiert und kann sich mit einem Windows-Update ändern; einen Wert in fremdem Format lässt HDRpilot dann
  stehen. Ob Auto HDR wirkt, sieht HDRpilot nicht selbst (Prüfen über die Xbox Game Bar). Für Anti-Cheat-Systeme gibt es
  keine Herstelleraussage. Intensität und Erzwingen für nicht zugelassene Spiele stellt HDRpilot nicht ein; RTX HDR von
  NVIDIA gehört nicht zu HDRpilot.
- **Nicht signiert:** SmartScreen und manche Virenscanner warnen bei unbekannten, unsignierten EXEs, auch beim Setup.
- **Nur auf Deutsch:** Oberfläche, Hinweise, Fehlermeldungen der Launcher-Scanner, Kommandozeile und Protokoll.

## Fehlersuche

- **HDR schaltet gar nicht:** Lässt sich HDR in den Windows-Anzeigeeinstellungen von Hand einschalten? Zeigt der
  Tooltip „kein HDR-fähiger Bildschirm“, meldet Windows für keinen Zielbildschirm HDR. Mit
  `cli\hdrpilot.exe hdr status` siehst du, was Windows meldet; `hdr on` und `hdr off` schalten zum Test von Hand.
  Hinweise während eines Vollbild-Spiels stehen nur im Protokoll.
- **Spiel wird nicht erkannt:** Ist „HDR automatisch“ an und die Automatik nicht pausiert? Liegt die laufende EXE im
  Installationsordner? Wenn nicht, die EXE in den Details hinzufügen. Ist sie dort als „ausgeschlossen“ markiert, mit
  „Wieder aufnehmen“ zurückholen. Steht sie unter „Hilfsprozesse ausschließen“, das Muster dort entfernen.
- **HDR kommt für ein Spiel zu spät oder das Spiel hängt beim automatischen Start:** über „Starten“, eine
  „(HDR)“-Verknüpfung oder (bei Steam) die Steam-Startoption starten; dann ist HDR schon vor dem Spiel an.
- **Steam-Startoption wirkt nicht:** Läuft HDRpilot? In `logs\wrap.log` steht je Start, ob HDR vorbereitet wurde und
  warum nicht. Kennt HDRpilot das Spiel noch nicht, einmal „Neu einlesen“.
- **HDR bleibt nach dem Spiel an:** Endete das Spiel weniger als 5 Minuten nach dem Start, hält der Neustart-Puffer HDR
  noch an (Standard 1 Minute, der Tooltip zeigt die Restzeit). Ist dir das zu lang, stelle unter „Einstellungen“ beim
  Neustart-Puffer eine kürzere Zeit oder 0:00 (aus) ein. Sonst die Nachlaufzeit abwarten (Standard 10 s). Nennt der
  Tooltip das Spiel danach noch, läuft noch ein Programm aus seinem Ordner (etwa ein Launcher-Rest): im Task-Manager
  nachsehen und diese EXE in den Details entfernen. Manche Spiele beenden sich nicht vollständig und laufen unsichtbar
  weiter (im Test bei Palworld); HDRpilot weist nach 2 Minuten darauf hin. Dann den Spielprozess im Task-Manager
  beenden. Weitere Gründe: Ein anderes HDR-Spiel läuft noch; HDR war schon vor dem Spiel an; „Nach dem Spiel
  zurückstellen“ ist für dieses oder ein gleichzeitig gelaufenes Spiel aus. Nach „Starten“ ohne Spielstart
  bleibt HDR bis zum Ende der Startfrist an.
- **„Starten“ ist gesperrt:** Das Spiel ist nicht installiert, oder bei einem selbst eingetragenen Ordner kennt
  HDRpilot keine Start-EXE. Dann das Spiel über seine EXE hinzufügen. Bei einem Spiel aus dem Quiver Launcher liegen
  meist mehrere EXE-Dateien im Spielordner, die EXE liegt sehr tief, oder Quiver startet ein Skript (BAT oder CMD): das
  Spiel einmal in Quiver starten, dort die EXE wählen, wenn Quiver danach fragt, und danach „Neu einlesen“.
- **Windows Auto HDR wirkt nicht:** Hast du das Spiel über HDRpilot gestartet („Starten“, „(HDR)“-Verknüpfung
  oder Steam-Startoption)? Ein Start direkt im Launcher ohne Startoption bekommt Auto HDR nur über die Frühwarnung, und
  dann nur, wenn Windows-HDR schon an ist. Das Spiel muss DirectX 11 oder 12 nutzen und von Windows für Auto HDR
  zugelassen sein; ob es greift, zeigt der Intensitätsregler in der Xbox Game Bar (Win+G). Das Einstellungsfenster der
  Game Bar zeigt einen alten Stand, bis du es ganz schließt und neu öffnest. Wann HDRpilot den Schalter gesetzt und
  zurückgestellt hat, steht mit dem Wert von vorher im Protokoll („Auto HDR global“). Den aktuellen Stand des
  Windows-Schalters zeigt der Eintrag **Auto HDR** im Menü des Infobereichs.
- **Keine HDR-Kennzeichen in der Liste:** Der erste Abgleich läuft 30 s nach dem Start; ohne Konto sind je Start nur
  100 Spiele dran. Den Stand und den letzten Fehler zeigt der Bereich „HDR-Liste (PCGamingWiki)“ in den Einstellungen.
- **HDRpilot war abgestürzt oder beendet:** einfach neu starten, der gemerkte Zustand wird dann wiederhergestellt.
- **Setup oder Entfernen meldet einen Fehler:** Einzelheiten stehen in `%TEMP%\HDRpilot-Setup.log`; nach einem Fehler
  im Setup ist die bisherige Version unverändert. Lässt sich HDRpilot nicht beenden, im Infobereich „HDRpilot beenden“
  wählen und es erneut versuchen.
- **HDRpilot startet nicht von selbst, oder Einstellungen sind weg nach einem Start aus einer anderen App:** Manche
  Apps aus dem Microsoft Store oder als MSIX-Paket (etwa die Claude-App) lassen alles, was aus ihnen heraus startet, in
  ihrem eigenen Container laufen. Windows leitet dann Registry-Einträge wie den Autostart in den privaten Speicher dieser
  App um (`%LOCALAPPDATA%\Packages\<App>\LocalCache`), Einstellungen und Protokolle dann, wenn ihr Ordner außerhalb
  noch fehlte oder schon dort liegt. Außerhalb fehlt, was umgeleitet wurde, und der Autostart wirkt nicht. HDRpilot
  erkennt das, egal wo seine Ordner liegen: Beim Start steht eine Warnung „HDRpilot läuft im Container der App …“ im
  Protokoll (je nach Ordner in der umgeleiteten Kopie unter `…\LocalCache\Local\HDRpilot\logs`), Setup und Entfernen
  ändern nichts und sagen es (still: Exit-Code 7). Abhilfe: HDRpilot beenden und über das Startmenü oder die
  Verknüpfung starten, Setup-Dateien per Doppelklick im Explorer ausführen, Entfernen über „Installierte Apps“.
- **Ein Spiel fehlt in der Liste:** „Neu einlesen“ drücken; fehlt es weiter, über „Spiel hinzufügen“ eintragen oder
  seinen übergeordneten Ordner als Suchordner angeben. Fehlen alle Spiele aus dem Quiver Launcher, kennt HDRpilot den
  Quiver-Ordner noch nicht: Quiver einmal starten, während HDRpilot läuft, oder den Quiver-Ordner als Suchordner
  eintragen (siehe „Quiver Launcher“). Fehler einzelner Launcher stehen in der Bibliothek und im Protokoll. Ist unter
  „Einstellungen“ › „Bibliothek“ der Schalter „Nicht installierte Spiele ausblenden“ an, stehen nicht installierte
  Spiele nicht in der Liste; ist sie deshalb leer, sagt sie das, und findet die Suche nur ausgeblendete Spiele, sagt es
  der Hinweis „Keine Spiele für diese Auswahl“.
- **Ein installiertes Spiel heißt „Nicht installiert“:** Liegt es auf einem Laufwerk, das gerade nicht verbunden ist,
  steckt es im Launcher mitten in einem Update, oder hat das Einlesen den Launcher nicht gefunden (siehe Protokoll)?
  Dann nicht „Einstellungen entfernen…“ wählen (die Rückfrage warnt, wenn das Laufwerk nicht verbunden ist, eine EXE
  des Spiels noch da ist, Steam es noch als installiert führt oder sich das nicht prüfen lässt; ein Restordner nur mit
  Spielständen zählt nicht): Nach dem Verbinden bzw. dem Update und „Neu einlesen“ gelten seine Einstellungen wieder.
- **Protokoll:** Infobereich → „Protokollordner öffnen“. Für mehr Einzelheiten unter Einstellungen → Erweitert die
  Protokollstufe auf „Ausführlich“ stellen. Jede Schaltung steht dort mit Auslöser, Bildschirm, Zustand vorher und
  nachher.
- **Alles zurücksetzen:** HDRpilot beenden und `%APPDATA%\HDRpilot\config.json` löschen. Die Spiele werden beim
  nächsten Start neu eingelesen; deine Auswahl geht dabei verloren.

## Entfernen

Mit dem Setup installiert:

1. In Steam die Startoption `--wrap -- %command%` bei den Spielen löschen, bei denen du sie eingetragen hast
   (Rechtsklick auf das Spiel → Eigenschaften → Allgemein → Startoptionen), sonst starten diese Spiele nicht mehr.
   Das Entfernen nennt dir die Spiele, bei denen sie noch steht.
2. Unter *Einstellungen → Apps → Installierte Apps* bei HDRpilot *Deinstallieren* wählen. HDRpilot fragt nach: Ohne
   Haken bei **Auch Einstellungen, Spieleliste und Protokolle löschen** (Standard) bleiben `%APPDATA%\HDRpilot` und
   `%LOCALAPPDATA%\HDRpilot` erhalten und werden bei einer neuen Installation wieder benutzt; mit Haken sind sie weg,
   samt HDR-Liste und gespeichertem PCGamingWiki-Konto.
3. **Entfernen** beendet HDRpilot, stellt einen gemerkten HDR-Zustand wieder her, stellt den Auto-HDR-Schalter von
   Windows zurück, falls ein Absturz von HDRpilot ihn eingeschaltet hinterlassen hat (Schritt **Auto HDR zurückstellen**),
   und entfernt das Programm, seine Verknüpfungen und den Autostart. Das erledigt eine Kopie im Temp-Ordner, die danach
   ihren Ordner und ihre entpackten Grafik-Bibliotheken (`%TEMP%\.net\HDRpilot-Uninstall`) selbst löscht.
4. Bleiben „(HDR)“-Verknüpfungen für einzelne Spiele übrig, kannst du sie löschen, ebenso die entpackten
   Grafik-Bibliotheken in `%TEMP%\.net\HDRpilot` und `%TEMP%\.net\HDRpilot-Setup-<Version>` (rund 8 MB je Ordner).

Ohne Installation:

1. In den Einstellungen **Mit Windows starten** ausschalten und gegebenenfalls **Konto entfernen**.
2. In Steam die Startoption `--wrap -- %command%` bei den Spielen wieder löschen, bei denen du sie eingetragen hast.
3. Warten, bis kein Spiel mehr läuft und HDR zurückgestellt ist, dann im Infobereich **HDRpilot beenden**.
4. Den Ordner `HDRpilot` löschen (`HDRpilot.exe`, `cli`, README, `LICENSE` und Lizenzhinweise), dazu die Ordner
   `%APPDATA%\HDRpilot`, `%LOCALAPPDATA%\HDRpilot`, `%TEMP%\.net\HDRpilot`, den Startmenü-Ordner
   `%APPDATA%\Microsoft\Windows\Start Menu\Programs\HDRpilot` und die „(HDR)“-Verknüpfungen auf dem Desktop.

## Aus dem Quelltext bauen

Benötigt das .NET 10 SDK. Die Strukturtests brauchen außerdem vier Header des Windows SDK:
`tools\fetch-sdk-headers.ps1` lädt sie einmalig (rund 153 MB von nuget.org) nach `.sdk\`. Alternativ zeigt die
Umgebungsvariable `HDRPILOT_SDK_INCLUDE` auf den `um`-Ordner eines installierten Windows SDK. Ohne die Header
überspringt der Testlauf diese Tests, und `test.ps1` meldet einen Fehler.

```
dotnet build HdrPilot.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File tools\fetch-sdk-headers.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\gates\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\publish.ps1
```

`tools\publish.ps1` legt die fertige Auslieferung unter `artifacts\publish\HDRpilot\` ab und daneben den Setup
`artifacts\publish\HDRpilot-Setup-1.5.0.exe` (dieselbe EXE mit angehängtem Paket aus README, Lizenztext,
Lizenzhinweisen und Kommandozeile, siehe E83 in `docs\entscheidungen.md`). Projekte und Namensräume im Quelltext heißen weiter
`HdrPilot.*`. Begründungen für Designentscheidungen stehen in `docs\entscheidungen.md`, die Prüfnachweise in
`GATES.md`.

## Lizenz

HDRpilot steht unter der PolyForm Strict License 1.0.0, Copyright Dante_GE. Maßgeblich ist allein der Lizenztext in
[`LICENSE`](LICENSE), der auch dem Setup und dem Ordner ohne Installation beiliegt; kurz gefasst:

- Erlaubt ist die Nutzung für nicht-kommerzielle Zwecke, etwa privat, zum Lernen und Ausprobieren, als Hobby oder in
  gemeinnützigen und öffentlichen Einrichtungen.
- Nicht erlaubt sind Änderungen und darauf aufbauende Werke, jede Weitergabe (verändert oder unverändert) und die
  kommerzielle Nutzung.

Fremdbestandteile behalten ihre eigenen Lizenzen (siehe „Lizenzen von Fremdbestandteilen“). Eine Ausnahme von der
PolyForm-Lizenz sind auch die Testdaten aus PCGamingWiki (vor allem `tests\HdrPilot.Tests\Fixtures\Pcgw\`): Sie stehen
wie ihre Quelle unter CC BY-NC-SA 3.0.

Fertige Versionen (Setup-Datei und Ordner ohne Installation) erscheinen unter
[GitHub Releases](https://github.com/DanteGE/HDRpilot/releases).

## Lizenzen von Fremdbestandteilen

HDRpilot enthält die .NET-Laufzeit samt WPF und Windows Forms sowie die Pakete
`Microsoft.Extensions.Logging.Abstractions` und `Microsoft.Extensions.DependencyInjection.Abstractions`, alle unter
der MIT-Lizenz. Die HDR-Angaben stammen von PCGamingWiki (pcgamingwiki.com) unter der Lizenz CC BY-NC-SA 3.0; sie
werden zur Laufzeit abgerufen, nicht mitgeliefert. Die Lizenztexte und die Hinweise zu Fremdcode innerhalb der
.NET-Laufzeit liegen der Auslieferung als `THIRD-PARTY-NOTICES.txt` bei. Keine Bestandteile stehen unter GPL oder AGPL.
