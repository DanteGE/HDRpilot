# Gate G10.8: Einstellungen im Showcase-Stil per UI-Automation an einer eigenen Testinstanz (eigene Ablage, echte
# Bibliothek nur gelesen eingelesen, nur Fenster dieser Prozess-ID) und als Bilder in 1280×800, 1920×1080 und 3440×1440,
# dunkel und hell. Schaltet kein HDR, startet kein Spiel, sendet keine Tasten und drückt nie „Prüfen“ (kein Netzwerk).
#   - alle bisherigen Kennungen der Einstellungen da, dazu Navigation und Seite „Darstellung“ mit allen neuen Kennungen
#   - Vorlagen: 8 in der Reihenfolge des Prototyps; Cyber, Duoton/Tritonus, Hell, Reduziert landen sofort in der Konfiguration
#   - SteamGridDB ist aus (Schalter und Konfiguration); „Standard“ setzt die Darstellung zurück
#   - Raster: jeder Bereich im Fenster und höchstens 760 px (DIP) breit; ab 1920 px Fensterbreite mindestens zwei nebeneinander
#   - Bilder: jede Datei da, genau in Pixelgröße
#   PHASE10_SETTINGS_UI_OK
param([string]$ShotsDir = (Join-Path $env:TEMP 'HDRpilot-Gates\phase10-settings'))
. (Join-Path $PSScriptRoot 'Phase4Common.ps1')
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -Namespace HdrGate -Name Dpi -MemberDefinition '[DllImport("user32.dll")] public static extern uint GetDpiForWindow(System.IntPtr hwnd);'
$ok = $true
function Check([bool]$cond, [string]$what) { if ($cond) { Note "ok   $what" } else { Note "FEHLER $what"; $script:ok = $false } }
function Rect($el) { $el.Current.BoundingRectangle }

# Höchstbreite eines Bereichs (SettingsGridLayout.MaxColumnWidth) in DIP
$maxSection = 760
$oldIds = 'TargetDisplays', 'PollInterval', 'GracePeriod', 'RestartBufferMinutes', 'RestartBufferSeconds', 'LaunchTimeout', 'AutostartToggle',
    'NotificationsToggle', 'HotkeyFallbackToggle', 'SearchFolders', 'AddSearchFolderButton', 'RemoveSearchFolderButton', 'LogLevel', 'HelperPatterns',
    'ResetHelperPatternsButton', 'OpenLogFolderButton', 'SaveSettingsButton', 'CancelSettingsButton', 'SettingsVersion', 'CaptionMinimize',
    'CaptionMaximize', 'CaptionClose', 'HdrListSyncToggle', 'HdrListSyncNowButton', 'HdrListStatus', 'PcgwUserName', 'PcgwBotPassword',
    'PcgwSaveAccountButton', 'PcgwRemoveAccountButton', 'PcgwSiteLink', 'PcgwLicenseLink'
$newIds = 'SettingsNav', 'NavGeneral', 'NavDisplays', 'NavAutomation', 'NavSearchFolders', 'NavHelpers', 'NavAppearance', 'NavAppearanceColors',
    'NavAppearanceArtwork', 'NavAppearanceTray', 'NavAdvanced', 'NavLogs', 'SectionPalette', 'SectionPreview', 'SectionTheme', 'SectionEffects',
    'SectionArtwork', 'SectionTray', 'PaletteModeDuotone', 'PaletteModeTritone', 'PaletteSwapButton', 'PalettePresets', 'AutoTextOnAccentToggle',
    'BackgroundFollowsGameToggle', 'ThemeSystem', 'ThemeLight', 'ThemeDark', 'EffectFull', 'EffectReduced', 'EffectOff', 'PauseWhilePlayingToggle',
    'PauseInBackgroundToggle', 'RespectWindowsAnimationsToggle', 'MinimizeToTrayAfterLaunchToggle', 'ArtworkCoverageTable', 'FallbackExamples',
    'SteamGridDbToggle', 'SteamGridDbKey', 'SteamGridDbCheckButton', 'SteamGridDbStatus', 'TrayPreview', 'TrayMenuPreview', 'TaskbarPreviewLight',
    'TaskbarPreviewDark', 'PreviewHdrToggle', 'AppearanceResetButton', 'AppearanceAppliedHint'
$sections = 'StartupSection', 'SectionPalette', 'SectionPreview', 'SectionTheme', 'SectionEffects', 'SectionArtwork', 'SectionTray'
$presetNames = 'Abendrot', 'Cyber', 'Neongrün', 'Royal', 'Gletscher', 'Synthwave', 'Glut', 'Polarlicht'

# Wartet, bis die Konfiguration den erwarteten Wert hat (Darstellung speichert sofort, die Datei folgt in Millisekunden).
function Wait-Appearance([string]$cfg, [scriptblock]$test, [int]$timeoutMs = 4000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do {
        $c = Read-Config $cfg
        if (& $test $c.settings) { return $true }
        Start-Sleep -Milliseconds 150
    } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
    return $false
}

function Find-ByName($parent, [string]$name, [int]$timeoutMs = 3000) {
    $cond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $name)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $e = $parent.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
        if ($e) { return $e }
        Start-Sleep -Milliseconds 100
    }
    return $null
}

# Raster prüfen: jeder Bereich im Fenster, höchstens Höchstbreite, Spalten nebeneinander
function Test-Grid($set, [string]$label) {
    $scale = [HdrGate.Dpi]::GetDpiForWindow([IntPtr]$set.Current.NativeWindowHandle) / 96.0
    if ($scale -le 0) { $scale = 1 }
    $w = Rect $set
    $rects = @{}
    foreach ($id in $sections) { $el = Find-Id $set $id 2000; if ($el) { $rects[$id] = Rect $el } }
    Check ($rects.Count -eq $sections.Count) "$label`: alle Bereiche gefunden ($($rects.Count) von $($sections.Count))"
    $tooWide = @($rects.Keys | Where-Object { $rects[$_].Width / $scale -gt $maxSection + 2 })
    $outside = @($rects.Keys | Where-Object { $rects[$_].Left -lt $w.Left - 1 -or $rects[$_].Right -gt $w.Right + 1 })
    Check ($tooWide.Count -eq 0) ("$label`: Bereiche höchstens {0} px breit (DIP; breitester {1:N0})" -f $maxSection, (($rects.Values | ForEach-Object { $_.Width / $scale } | Measure-Object -Maximum).Maximum))
    Check ($outside.Count -eq 0) "$label`: Bereiche innerhalb des Fensters (außerhalb: $($outside -join ', '))"
    $palette = $rects['SectionPalette']; $preview = $rects['SectionPreview']
    $sideBySide = $palette -and $preview -and [Math]::Abs($palette.Top - $preview.Top) -lt 3 -and $preview.Left -ge $palette.Right
    $dipWidth = $w.Width / $scale
    if ($dipWidth -ge 1920) {
        Check $sideBySide ("$label`: ab 1920 px stehen Farbschema und Live-Vorschau nebeneinander (Fenster {0:N0} px DIP)" -f $dipWidth)
    } elseif ($dipWidth -lt 1440) {
        Note ("info $label`: Fenster {0:N0} px DIP, eine Spalte erwartet; nebeneinander: {1}" -f $dipWidth, $sideBySide)
    }
    $script:gridSizes++
}

$cfg = New-TestConfig 'phase10-settings' -Import
New-Item -ItemType Directory -Force $ShotsDir | Out-Null
Get-ChildItem $ShotsDir -Filter '*.png' -ErrorAction SilentlyContinue | Remove-Item -Force
try {
    $app = Start-App $cfg @('--settings')
    $set = Find-Window 'SettingsWindow' $app.Id
    Check ($null -ne $set) 'Einstellungen der Testinstanz geöffnet'
    if (-not $set) { throw 'Einstellungen fehlen' }

    $missing = @($oldIds | Where-Object { -not (Find-Id $set $_ 2000) })
    Check ($missing.Count -eq 0) "Alle bisherigen Kennungen vorhanden ($($oldIds.Count); fehlen: $($missing -join ', '))"

    Invoke-El (Find-Id $set 'NavAppearance')
    Start-Sleep -Milliseconds 500
    $missing = @($newIds | Where-Object { -not (Find-Id $set $_ 2000) })
    Check ($missing.Count -eq 0) "Alle neuen Kennungen vorhanden ($($newIds.Count); fehlen: $($missing -join ', '))"

    # Vorlagen in der Reihenfolge des Prototyps
    $presets = Find-Id $set 'PalettePresets'
    $radio = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::RadioButton)
    $items = @($presets.FindAll([Windows.Automation.TreeScope]::Descendants, $radio))
    $names = @($items | ForEach-Object { $_.Current.Name })
    Check ($items.Count -eq 8 -and (($names -join ',') -eq ($presetNames -join ','))) "Vorlagen: $($names -join ', ')"

    # Standard: SteamGridDB aus, nichts im Netz
    $sgdb = Find-Id $set 'SteamGridDbToggle'
    $c = Read-Config $cfg
    Check ((Get-Toggle $sgdb) -eq 'Off' -and -not [bool]$c.settings.artwork.steamGridDb) "SteamGridDB standardmäßig aus (Schalter $(Get-Toggle $sgdb), Konfiguration '$($c.settings.artwork.steamGridDb)')"
    $check = Find-Id $set 'SteamGridDbCheckButton'
    Check (-not $check.Current.IsEnabled) '„Prüfen“ ist ohne SteamGridDB gesperrt (und wird hier nie gedrückt)'

    # Darstellung wirkt und speichert sofort
    Select-El ($items | Where-Object { $_.Current.Name -eq 'Cyber' } | Select-Object -First 1)
    Check (Wait-Appearance $cfg { param($s) $s.appearance.preset -eq 'cyber' }) 'Vorlage Cyber gespeichert (settings.appearance.preset = cyber)'
    Select-El (Find-Id $set 'PaletteModeDuotone')
    Check (Wait-Appearance $cfg { param($s) $s.appearance.paletteMode -eq 'Duotone' }) 'Duoton gespeichert (paletteMode = Duotone)'
    Select-El (Find-Id $set 'PaletteModeTritone')
    Check (Wait-Appearance $cfg { param($s) $s.appearance.paletteMode -eq 'Tritone' }) 'Tritonus gespeichert (paletteMode = Tritone)'
    Select-El (Find-Id $set 'ThemeLight')
    Check (Wait-Appearance $cfg { param($s) $s.appearance.theme -eq 'Light' }) 'Thema Hell gespeichert (theme = Light)'
    Select-El (Find-Id $set 'EffectReduced')
    Check (Wait-Appearance $cfg { param($s) $s.appearance.effectLevel -eq 'Reduced' }) 'Effekte Reduziert gespeichert (effectLevel = Reduced)'
    $hint = Find-Id $set 'AppearanceAppliedHint'
    Check ($null -ne $hint -and $hint.Current.Name -match 'sofort') ('Hinweis neben „Standard“: ' + $hint.Current.Name)

    Invoke-El (Find-Id $set 'AppearanceResetButton')
    Check (Wait-Appearance $cfg { param($s) $null -eq $s.appearance.preset -and $null -eq $s.appearance.theme -and $null -eq $s.appearance.effectLevel -and $null -eq $s.appearance.paletteMode }) '„Standard“ setzt Vorlage, Thema, Anzahl und Effekte zurück'
    $c = Read-Config $cfg
    Check (-not [bool]$c.settings.artwork.steamGridDb) 'SteamGridDB nach allen Schritten weiter aus'

    # Raster bei den Größen, die der Bildschirm zulässt
    $tp = $null
    $wp = $null
    $work = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    Note ("Arbeitsbereich {0}x{1}" -f $work.Width, $work.Height)
    $script:gridSizes = 0
    if ($set.TryGetCurrentPattern([Windows.Automation.TransformPattern]::Pattern, [ref]$tp) -and $tp.Current.CanResize) {
        foreach ($size in @(@(1280, 800), @(1920, 1080))) {
            if ($size[0] -le $work.Width -and $size[1] -le $work.Height) {
                $tp.Resize($size[0], $size[1])
                Start-Sleep -Milliseconds 900
                Test-Grid $set ("{0}x{1}" -f $size[0], $size[1])
            } else {
                Note ("UMGEBUNG: {0}x{1} passt nicht auf den Bildschirm ({2}x{3}), nur in den Bildern geprüft" -f $size[0], $size[1], $work.Width, $work.Height)
            }
        }
    } else { Check $false 'Fenster lässt sich per UI-Automation in der Größe ändern' }
    if ($set.TryGetCurrentPattern([Windows.Automation.WindowPattern]::Pattern, [ref]$wp)) {
        $wp.SetWindowVisualState([Windows.Automation.WindowVisualState]::Maximized)
        Start-Sleep -Milliseconds 900
        Test-Grid $set 'maximiert'
        $wp.SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
        Start-Sleep -Milliseconds 300
    }
    Check ($script:gridSizes -ge 1) ("Raster bei {0} Größe(n) geprüft" -f $script:gridSizes)

    Invoke-El (Find-Id $set 'CancelSettingsButton')
    Start-Sleep -Milliseconds 500
    Start-Process $AppExe -ArgumentList @('--config-dir', "`"$cfg`"", '--exit') -Wait | Out-Null
    Check ($app.WaitForExit(10000)) 'Testinstanz beendet'

    # Bilder: 4 Zustände × 3 Größen × 2 Themen, genau in Pixelgröße (auch größer als der Bildschirm)
    $snap = Start-Process $AppExe -ArgumentList @('--snapshot', "`"$ShotsDir`"", '--config-dir', "`"$cfg`"", '--snapshot-sizes', 'review', '--snapshot-scenes', 'einstellungen', '--snapshot-themes', 'dunkel,hell') -PassThru -Wait
    Check ($snap.ExitCode -eq 0) "Bildschirmfotos erzeugt (Exit-Code $($snap.ExitCode))"
    $bad = @()
    $count = 0
    foreach ($scene in 'allgemein', 'darstellung', 'darstellung-bilder', 'darstellung-tray') {
        foreach ($size in '1280x800', '1920x1080', '3440x1440') {
            foreach ($theme in 'dunkel', 'hell') {
                $file = Join-Path $ShotsDir "einstellungen-$scene-$size-$theme.png"
                $parts = $size -split 'x'
                if (-not (Test-Path $file)) { $bad += "fehlt: $(Split-Path $file -Leaf)"; continue }
                $img = [System.Drawing.Image]::FromFile($file)
                try {
                    if ($img.Width -ne [int]$parts[0] -or $img.Height -ne [int]$parts[1]) { $bad += "$(Split-Path $file -Leaf): $($img.Width)x$($img.Height)" }
                    $count++
                } finally { $img.Dispose() }
            }
        }
    }
    Check ($bad.Count -eq 0 -and $count -eq 24) "24 Bilder in genauer Pixelgröße unter $ShotsDir ($count geprüft; Probleme: $($bad -join '; '))"
}
catch { Note "FEHLER Ausnahme: $($_.Exception.Message)"; $ok = $false }
finally { Stop-Started }
if ($ok) { Write-Output 'PHASE10_SETTINGS_UI_OK'; exit 0 }
exit 1
