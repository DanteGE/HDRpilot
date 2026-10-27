# HDRpilot
A sleek, 100% vibe-coded Windows app that automatically enables HDR for selected games. Features optional Auto HDR for SDR games, shortcut and Steam launch option integration, an intuitive GUI, and support for Steam, Epic Games, Ubisoft, Battle.net, GOG, and Quiver Launcher.

Deutsch: [README.de.md](README.de.md)

HDRpilot turns Windows HDR on automatically when you launch a selected game and restores the previous state afterwards.
It finds your installed games by itself (Steam, Epic, Xbox/Game Pass, GOG, Ubisoft Connect, EA app, Battle.net, Quiver
Launcher). You add games without a launcher yourself, or HDRpilot suggests them from your search folders. It shows you
which games support HDR according to PCGamingWiki. In everyday use it runs unobtrusively in the notification area.

- A setup without administrator rights, or alternatively a single EXE with no installation at all; no Windows service.
- No telemetry and no interference with game processes. HDRpilot only goes online to sync the HDR list with
  PCGamingWiki; you can turn that off.
- Light in the background: one process query per second, measured at about 0.1–0.4% of one CPU core and about 40–50 MB
  of memory.

## Requirements

- Windows 11 or Windows 10 version 21H2 or later, 64-bit.
- An HDR-capable display for which “Use HDR” can be turned on under *Settings → System → Display*.
- No .NET installation needed; the runtime is built into the EXE.

## Installation

The recommended way is the setup `HDRpilot-Setup-1.6.0.exe`. It installs for your Windows user only and needs no
administrator rights. If you prefer to work without an installation, use the `HDRpilot` folder (see “Without
installation”). Ready-made releases are available on [GitHub Releases](https://github.com/DanteGE/HDRpilot/releases).

1. Run `HDRpilot-Setup-1.6.0.exe` (setup and uninstall use the language of Windows: German on a German Windows, English
   otherwise). The file isn't signed, so Windows SmartScreen asks the first time you run it (“More info” → “Run
   anyway”).
2. The setup shows the program folder `%LOCALAPPDATA%\Programs\HDRpilot` and three options: **Start with Windows**
   (preselected), **Desktop shortcut** (not preselected) and **Launch HDRpilot afterwards** (preselected).
   **Install** copies HDRpilot there, creates a Start menu shortcut and adds HDRpilot to *Settings → Apps → Installed
   apps*. The command line is then in `<program folder>\cli`, next to it the README (English `README.md` and German
   `README.de.md`), the license text (`LICENSE`) and the third-party notices.
3. With the third option, the game library opens after installation (not if the setup runs with administrator rights;
   in that case, start HDRpilot from the Start menu). In everyday use, HDRpilot runs as an icon in the notification area
   (bottom right, next to the clock) and scans your games every time it starts. If you don't see the icon, Windows has
   hidden it behind the arrow (˄); from there you can drag it onto the taskbar. Double-clicking the icon (or starting
   HDRpilot from the Start menu) opens the game library.
4. For the games that should get HDR, turn on the **HDR on launch** switch. Scanned games always start with it off;
   games you add yourself have it on right away. The **HDR-capable** filter shows which of your games support HDR
   according to PCGamingWiki.

Closing the window doesn't exit HDRpilot. To exit, use the notification area menu → **Exit HDRpilot**.

Settings, game list, logs and HDR list aren't stored in the program folder but in `%APPDATA%\HDRpilot` and
`%LOCALAPPDATA%\HDRpilot`. The setup never touches them: if you already had HDRpilot, they stay in effect unchanged after
the installation. If your previous `HDRpilot.exe` was already in `%LOCALAPPDATA%\Programs\HDRpilot`, the setup replaces
it there, and autostart, shortcuts and Steam launch options keep pointing to the same EXE. If it was somewhere else, the
setup closes a running old version and then starts the new one; you can then delete the old EXE and create its shortcuts
and Steam launch options again.

### Updating

Run the setup of the new version. It shows the installed and the new version; autostart and desktop shortcut are
preselected as before. **Update** briefly closes a running HDRpilot, replaces the program files and starts HDRpilot
again if it was running before (again only in the notification area); if it wasn't running, **Launch HDRpilot
afterwards** decides. If something fails along the way, the previous version stays unchanged and working. Settings, game
list, logs and HDR list are kept because they're stored in `%APPDATA%\HDRpilot` and `%LOCALAPPDATA%\HDRpilot`. Running
the same setup again repairs the installation. It only installs an older version if you check **Install the older
version anyway**. Be careful after playing games from the Quiver Launcher: HDRpilot 1.4.0 and older don't know this
source, consider your game list broken and start empty (see “Known limitations”). The notification area menu (gray line
below the menu header) and the header of the settings show which version is running.

### Without installation

1. Put the `HDRpilot` folder (with `HDRpilot.exe`, `cli\hdrpilot.exe`, the README in English and German, the license
   text `LICENSE` and the third-party notices) in a permanent location, for example `%LOCALAPPDATA%\Programs\HDRpilot\`.
   Autostart, shortcuts and Steam launch options point to this location. If you move the EXE later, HDRpilot fixes the
   autostart entry by itself the next time it starts; you have to create shortcuts and Steam launch options again.
2. Start `HDRpilot.exe` (SmartScreen as above). No window opens: HDRpilot briefly shows up in the notification area and
   scans your games. Double-clicking the icon or starting the EXE a second time opens the game library; continue as in
   step 4 above.
3. Optionally turn on **Start with Windows** in the settings.

You can switch to an installation with the setup at any time; settings and game list are kept.

## Usage

### Game library

- On the left of the header, the selected source is shown in large type (“All games”, “Steam games”, “GOG games”, and so on
  up to “My games”) under the overline “GAME LIBRARY”, with its numbers below: games, of those with **HDR on launch**, of
  those new (for example “641 games · 12 with HDR on launch · 3 new”). With filters or search, only the games shown
  from this source count (“36 of 641 games · 4 with HDR on launch · 1 new”); hidden uninstalled games never count.
  Clicking the title (or Enter, Space or Alt+Down Arrow) opens the sources with their numbers; the choice works like
  the “Source” field in the toolbar. On the right are the display capsule and the buttons **Turn HDR on** or **Turn
  HDR off** (switches immediately, regardless of any game), **Pause automation** and the gear for the settings. The
  capsule shows the target display with its color depth, whether HDR is on, and its values according to Windows: peak,
  full frame and minimum in nits. The scale goes from 0 up to the peak with HDR on, even if HDR is currently off (the
  capability of the display, not a measurement); as long as HDRpilot doesn't know the values, it shows “—”.
- Below it is the title area of the selected game: hero image, logo or name, when it was last played, the source and a
  badge for HDR support. When you switch games, the image cross-fades; the ambient light and glow of the window take
  on the colors of the hero image (can be turned off under “Appearance”, see “Settings”).
- Three views, switchable in the toolbar or with Ctrl+1, Ctrl+2 and Ctrl+3; the choice is remembered:
  - **List** (default): one row per game with a preview, name, source, “Last played”, HDR badge and the **HDR on
    launch** switch. Clicking a column header sorts. At the top left of the list you choose the preview: **16:9
    thumbnail** or **Icon**. In narrow windows, first the source, then the HDR column and “Last played” are dropped. You
    set the height of the title area above the list with the handle at its bottom edge: drag it or, when the handle has
    focus, use Up/Down Arrow (Page Up/Down for larger steps, Home/End for the smallest or largest height); Esc cancels
    dragging, double-click or Enter restores the default (one third of the window height). HDRpilot remembers the
    height; below it, at least five full rows of the list always remain visible. Scrolling leaves the height as it is.
  - **Grid**: covers in 2:3 format. The number of columns and the tile size follow the window width, in full screen
    just as in a small window; nothing is stretched or cropped. The “HDR on launch” switch appears on the selected tile
    and on mouse hover. You set the title area with the same handle as in the list; one full row of tiles always
    remains visible.
  - **Shelf**: a large hero image with **Launch**, **Desktop** and **Start menu**, below it a row of covers to browse
    and on the right the “AUTOMATION” card with the most important switches (only when a game is selected). **Details**
    (or the I key) opens all details as a drawer, Esc closes it. With the handle below the hero image you trade hero
    image size for cover size: a taller hero image makes the covers smaller, down to the smallest size.
  - Each view remembers its own height of the title area; double-click or Enter on the handle only resets the height
    of the view currently shown.
- In List and Grid, the details of the selected game are on the right. In the gap between them sits the scroll bar of
  the list or grid: the pill shows where you are and how much of it is visible. Dragging scrolls, clicking above or
  below the pill scrolls one page, and the mouse wheel also scrolls in the gap; if everything fits in the window, it's
  hidden. You change the width of the details at the thin line that appears when you hover over the gap: drag
  horizontally (Esc cancels), use the arrow keys when the splitter has focus, double-click to reset; HDRpilot remembers
  the width, at most 560 px.
- Keyboard: Ctrl+F shows the toolbar (the cursor is then in the search) and hides it again, the arrow keys select,
  Enter launches the selected game, Space toggles “HDR on launch”, Esc clears the search, and Del asks, for an
  uninstalled game from a launcher, whether its settings should go, and for a game you added yourself, whether it should
  be removed from the library (see below). Right-click, Shift+F10 or the Menu key opens the game menu with **Launch**,
  **HDR on launch**, **Restore after game**, **Windows Auto HDR**, **Choose image…**, **Change icon…** or **Reset
  icon**, **Create desktop shortcut**, **Add to Start menu**, **Open install folder** and, only for uninstalled games
  from launchers, **Remove settings…**. Buttons that open a menu (the title with the sources, “···” and the large icon
  in the details) close it again on a second click, as do Enter or Space as long as nothing in it is selected.
- Hero image, logo and cover come from the launchers' image caches on the PC (Steam, GOG Galaxy, Ubisoft Connect, Xbox
  package, for Quiver only the cover), read-only, never from the internet. If one is missing, HDRpilot generates a
  substitute image from the icon and name; new images from a launcher appear by themselves, for Quiver after the next
  scan (see “How it works”). You choose a different image via right-click › **Choose image…** or in the “IMAGES”
  section of the details: on each of the tabs **Hero image**, **Logo** and **Cover**, “ON THIS PC” offers **Automatic**,
  other local images (such as the splash screen of an Xbox package), the generated image and **Custom file…** (PNG, JPG,
  WebP, BMP); **Apply** saves, **Reset** selects **Automatic** again on the current tab. HDRpilot stores custom images
  downscaled in its data folder; the chosen file stays unchanged. With your own SteamGridDB API key, **Search
  SteamGridDB** in the “STEAMGRIDDB” section searches for more images of the game, always only on click: 24 previews per
  page with the author, **Load more** shows more; if the game is wrong, you choose a different one under “Game”.
  HDRpilot only downloads the chosen image when you click **Apply** and saves it as a custom image. Without a key,
  **Open settings** takes you to the key field (see “Settings”).
- Motion: the hero image slowly zooms and follows the mouse slightly, light rays and specks of light drift across it. If
  HDR turns on while the library is open, a short HDR moment plays (light streak, brighter version of the hero image,
  particles for at most 2.5 s, glowing edge). You set how much moves under “Appearance” › “Effects”; when minimized, in
  the background and while a game is running, everything stands still. The right side of the status bar shows what
  currently applies.
- What PCGamingWiki knows about HDR support is shown in the list's “HDR” column: “NATIVE”, “LIMITED” (restricted) or
  “MOD” (only via a mod). Tiles of games with HDR carry an “HDR” badge, and the title area says “NATIVE HDR”, “LIMITED
  HDR”, “HDR VIA MOD” or “HDR UNKNOWN”.
- The purple “AUTO HDR” badge right after the name marks games for which **Windows Auto HDR** is on. It's only an
  indicator; you switch it in the details, and it takes effect when the game is launched via HDRpilot (see “Windows
  Auto HDR”). Apart from it, only the **Auto HDR** entry in the notification area has its sparkle icon.
- Icons come from files that are already on the PC, never from the internet (for Steam, the official Steam icon, see
  “How it works”). You set a custom image via right-click on the game or by clicking the large icon in the details:
  **Change icon…** takes an image (PNG, JPG, WebP, BMP, GIF, ICO) or an EXE, **Reset icon** shows the automatic icon
  again. The custom icon takes precedence over all other sources; the chosen file stays unchanged, and HDRpilot stores a
  downscaled copy (96 px) in its data folder.
- The toolbar above the list: search, filter by source, sorting, the filters **HDR on launch**, **New** and
  **HDR-capable** as icon toggles with their count (also Alt+A, Alt+N, Alt+H), and on the right the view, **Rescan**,
  **Mark all as seen** and **Add game**. If you point at an element for a second, a tooltip explains it, including its
  shortcut. In narrower windows, search, source and sorting get narrower, then sorting shows only its icon; only when
  even that doesn't fit does the toolbar wrap onto two rows. A filter toggle is only highlighted while its filter is on;
  it only shows the focus frame when you use the keyboard, not after a mouse click.
- The toolbar can be hidden: with the small tab above the right end of the toolbar, with Ctrl+F (also from within the
  search; Ctrl+F1 works too) or via right-click on an empty spot of the toolbar (also Shift+F10) › **Hide toolbar**.
  Then only a capsule sits at the bottom edge of the hero image, showing the search or the active filters with their
  count; search and filters stay active, the edge glows while filtered, and the cross in the capsule resets all
  filters. Clicking the capsule or Ctrl+F shows the toolbar again; with Ctrl+F the cursor goes straight into the
  search. HDRpilot remembers whether it's hidden or shown.
- If search or filters remove the selected game from the list, the hero image and details show the first match (it
  stays “NEW” until you click it yourself); if nothing matches, the list says “No games for this selection” and the
  details say “No game selected”. As soon as your game is back in the list, for example after resetting the filters,
  it's selected again.
  If the selected game disappears from the list entirely (removed or hidden), the next one is selected, or the previous
  one at the end of the list; if that also removes the selected source, the list shows all games again, and the game
  selected is the one that follows the vanished game there.
- Sorting by **Name**, **Last played** or **HDR support**; the choice is remembered. “Last played” knows Steam games
  from Steam's own data and all games HDRpilot sees running.
- Newly found games carry the “NEW” marker. It disappears as soon as you click the game or change its switch; the eye
  icon next to “Rescan” (**Mark all as seen**) removes it from all games.
- **Rescan** scans all launchers again. This also happens in the background every time HDRpilot starts. Uninstalled
  games stay in the list with the note “Not installed”, together with their settings; after a reinstall, the settings
  apply again. If you no longer need them, remove them with **Remove settings…** (see below); uninstalled games can be
  hidden under “Settings” › **Library**.
- **Add game**: add games without a launcher yourself. Preferably choose the game's launch EXE. If you choose a folder,
  HDRpilot can only launch the game itself if exactly one EXE is directly inside it; detection while playing works in
  both cases.
- **Suggestions from your search folders**: subfolders of your search folders that contain EXE files appear at the top
  of the library. Only what you add there is taken over.
- Details of the selected game, from top to bottom:
  - **Launch** (for screen readers **Launch with HDR**): turns HDR on, waits for confirmation and then launches the
    game. If **Go to the notification area after launching a game** is on (see “Settings”), the library closes
    afterwards. For a game from the Quiver Launcher, it's disabled as long as HDRpilot doesn't know a unique launch EXE;
    the note below it says what to do (see “Quiver Launcher”).
  - **Desktop** / **Start menu**: creates a shortcut “<Game> (HDR)” that launches the game via HDRpilot in the same
    way. In the Start menu it's in the “HDRpilot” folder. It shows the icon from the list, including your custom one
    (for that, HDRpilot stores an ICO file next to its PNG, which stays when you reset the icon so the shortcut keeps
    its icon). If the list icon is an image such as an Xbox logo, the shortcut shows the icon of the game EXE.
  - **Copy Steam launch option** (Steam games only): a workaround for games you'd rather launch directly from Steam.
    Copies `"…\HDRpilot.exe" --wrap -- %command%`; in Steam, paste it via right-click on the game → Properties →
    General → Launch Options. Then HDR is on before the game starts even when launched from Steam (HDRpilot has to be
    running for this).
  - **HDR on launch**, **Display** (which display is switched; default: the global setting) and **Restore after game**
    (afterwards, restore the HDR state from before; default: on).
  - The “PICTURE” section with **Windows Auto HDR**: Windows converts the SDR picture of a game without native HDR to
    HDR (DirectX 11/12). This only works if the game is launched via HDRpilot (**Launch**, “(HDR)” shortcut, Steam
    launch option): HDRpilot turns Auto HDR on in Windows right before and restores it after the game; a short note
    under the title says so (“Takes effect when launched via HDRpilot”), the full explanation is behind **Learn more**
    (closed again with **Less**; it stays expanded for all games until you close the window). Switching it alone doesn't
    change anything in Windows yet. Independent of **HDR on launch**; if that's off, a note points out that Windows HDR
    must already be on before the game starts. If Auto HDR is on and **HDR on launch** is off, this note also shows
    when collapsed, otherwise only with the explanations. If the game has native HDR according to PCGamingWiki, the
    title and switch are dimmed, and a note in the explanations advises using the game's own HDR; you can still switch
    it (see “Windows Auto HDR”). If you turn it on for a Steam game whose Steam launch option is still missing, HDRpilot
    asks whether it should copy it (**Copy launch option**, like **Copy Steam launch option**); you have to add it in
    Steam yourself. Declining changes nothing; Auto HDR stays on.
  - **EXE files**: in principle, every program in the install folder counts as this game, except helper processes. The
    list shows the known EXEs with their origin (“from the launcher”, “detected while playing”, “added by you”). You
    can add EXEs (also outside the game folder), remove them (then they never count for this game) and bring them back
    with **Include again**. **Relearn** forgets the EXEs detected while playing.
  - The “IMAGES” section: hero image, logo, cover and icon of the game; a click opens **Choose image…**, for the icon
    the menu with **Change icon…**. Below it, it says where the images come from, for example “Local from the Steam
    cache · nothing downloaded” or “Custom image · only on this PC”.
  - **Remove from library**: only for games you added yourself.
  - **Remove settings…** (at the very bottom, red): only for uninstalled games from launchers, also via right-click or
    Del. After a confirmation, HDRpilot forgets what it has stored about the game: the entry with its switches,
    remembered EXE files and image choice, the custom icon and the custom images. The confirmation lists what will be
    lost (for example that “HDR on launch” is on, or “2 remembered EXE files”), warns if the game may still be
    installed (its drive isn't connected, an EXE of the game is still there, Steam still lists it as installed, for
    example in the middle of an update, or the drive or folder doesn't respond or isn't readable; a leftover folder with
    only saves, settings and logs doesn't count), and has the focus on **Cancel**. If there are “(HDR)” shortcuts of
    the game on the desktop or in the Start menu, a checkbox deletes them too, and their ICO file only if no other
    shortcut can still be using it; without the checkbox they stay and only launch the game again once it's installed
    and scanned again. Nothing changes in the game, the launcher or other shortcuts. Afterwards, the next game in the
    list is selected. If you reinstall the game, it appears as new at the next scan, with “HDR on launch” off. It isn't
    possible while the game is running.

### Notification area

The icon, a sun above the horizon in the colors of your color scheme, shows the state: as an outline with HDR off, as a
filled tile with HDR on, with a yellow pause badge when automation is paused and, while a game with HDR is running,
with a green badge. The window icon in the title bar and taskbar shows the same. The tooltip names the games for which
HDRpilot has currently switched HDR, whether automation is active and, if applicable, how much longer the restart
buffer keeps HDR on. The menu (right-click) offers:

- **Turn HDR on now** / **Turn HDR off now**
- **Auto HDR**: shows whether the Windows “Auto HDR” switch is currently on (read when the menu opens) and toggles it.
  This applies from the next game launch, for every game and also without HDRpilot. A change here is your choice:
  HDRpilot doesn't restore it after a game (see “Windows Auto HDR”). If HDRpilot has just turned the switch on for a
  game only, the tooltip says so: it will be off again after the game. If the value can't be read or has an unknown
  format, the entry is disabled.
- **Pause automation**: no new automatic switching; running games are still restored as usual. “Launch”, “(HDR)”
  shortcuts and the Steam launch option still switch. The pause lasts until you lift it or HDRpilot restarts; it isn't
  saved.
- **Open game list**, **Settings**, **Open log folder**
- **Exit HDRpilot**

Directly below the menu header (icon, “HDRpilot” and status), the version is shown in gray and not clickable (“Version”
with the version number), the same number as in the setup and under “Installed apps”.

Clicking a notification from HDRpilot also opens the game library.

### Settings

| Setting | Meaning | Default |
|---|---|---|
| Displays | Which displays HDR is switched on | Primary display |
| Polling interval | How often HDRpilot checks for game launches | 1000 ms |
| Grace period | Waiting time after the game ends before restoring | 10 s |
| Restart buffer | If a game ends less than 5 minutes after launch, HDR stays on this long (minutes and seconds, at most 30:00, 0:00 = off; see “How it works”) | 1:00 |
| Launch timeout | With “Launch”: how long to wait for the game process | 120 s |
| Start with Windows | Autostart via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | off |
| Language · Sprache | Interface language: Deutsch or English (each named in its own language); takes effect after HDRpilot restarts | new installation: like Windows (German for a German display language, English otherwise); after an update: German |
| Show notifications | Short messages in the notification area when switching and when there are problems; never during full-screen games, presentations or on a locked screen (then only in the log). Off: all messages only in the log | on |
| Search folders | Folders with games that have no launcher; subfolders with an EXE are suggested | none |
| Hide uninstalled games | Uninstalled games aren't shown in the library and aren't counted anywhere there (number of games, sources, filters); how many there are is shown next to the option and in the tooltip for resetting the filters. “HDR on launch” still applies to them | off |
| Exclude helper processes | Programs that never count as a game (crash reporters, installers and runtime packages, anti-cheat installers and services, plain launcher windows, etc.) | default list |
| Color scheme | Duotone or Tritone, one of the eight presets or custom colors; colors the interface, tray icon and window icon | Sunset, Duotone |
| Automatic text color on accent surfaces | Dark text or white on accent surfaces, whichever is more readable | on |
| Background follows the game | Ambient light and glow of the library take on the colors of the selected hero image | on |
| Colors follow the game | Library accents (Launch and HDR buttons, selection, switches, focus frames, badges, toolbar, particles in the HDR moment) take on the colors of the selected game's logo and cover, read once per game and only while the switch is on. Gray images and high contrast use the color scheme; settings, tray menu and dialogs always keep it | off |
| Theme | Automatic (like the Windows app mode), Light or Dark | Automatic |
| Effects | Full, Reduced (only short transitions up to 0.3 s) or Off | Full |
| Pause while playing | While a game is running, all animation stops | on |
| Pause in the background | When minimized, covered or inactive, all animation stops | on |
| Respect Windows animation effects | If “Animation effects” is off in Windows, at least “Reduced” applies | on |
| Go to the notification area after launching a game | After a launch from the library, the window closes and HDRpilot keeps running in the notification area | off |
| SteamGridDB (online) | With your own API key: search in **Choose image…** on click; **Load missing images automatically** also fills in missing hero images, logos and covers | off |
| Sync at every startup | Sync the HDR list with PCGamingWiki 30 s after startup, at most once per hour (see “HDR list”) | on |
| Log level (Advanced) | How detailed the log is | Normal |
| Keyboard shortcut as fallback (Advanced) | Sends Win+Alt+B, only if the Windows API fails and no game is running; requires the Xbox Game Bar and toggles all HDR-capable displays | off |

**Language:** The choice is under “General” › “Startup and notifications” and, like the other general settings,
applies with **Save**. Because texts are fixed when the windows open, the language only changes after HDRpilot
restarts. If the choice differs from the language shown, a note and **Restart now** appear: this saves all settings,
exits HDRpilot and starts it again right away with the settings open. A new installation (or the first start without
installation) uses the Windows display language: German if it's German, English otherwise. If you already use
HDRpilot, it stays German after an update. Date, time and numbers follow the Windows regional settings as long as their
language matches the chosen one (for example English with British format); otherwise the formats of Germany or the US
apply. The command line `cli\hdrpilot.exe` responds in the language of its storage. The log is in German; only messages
that also appear in the interface (such as scanner errors) are written in the interface language. English dates always
appear as month, day, year (“Sep 11, 2026”).

The “HDR list (PCGamingWiki)” section also has **Sync now**, the status of the list and the optional PCGamingWiki
account (see below). There's no setting for “Windows Auto HDR”: it's switched per game in the library and always takes
effect when the game is launched via HDRpilot (see “Windows Auto HDR”).

The language is stored in `config.json` as `language` (`"de"` or `"en"`); if the field is missing (configuration from
1.5.x or older) or contains anything else, German applies. Only adjustable in `config.json`: `launcherSignals` (early
warning from the Epic and Steam logs, default `true`; for games with “Windows Auto HDR” it also sets the Auto HDR switch
before the game). HDRpilot stores the restart buffer there as `restartBufferSeconds` (in seconds); if the value is
missing, the default of 1 minute applies. Quiver Launcher folders that HDRpilot has remembered are in `quiverFolders`
(absent as long as there are none); you can add a folder there or, more simply, start Quiver once while HDRpilot is
running.

In the **Library** section, **Remove settings for all uninstalled games…** removes in one go what **Remove settings…**
would remove for each individual uninstalled game from a launcher. The confirmation lists all affected games, under each
one what will be lost, and warns if games may still be installed; the reason is shown under the game's name: drive not
connected, EXE file still there, still listed in Steam, or can't be checked (drive or folder doesn't respond or isn't
readable). A leftover folder with only saves, settings and logs doesn't count. This takes effect immediately and
independently of **Save**; games you added yourself and games currently running are kept (if all are running, the card
says so).

On the left, a navigation leads through the sections (**General**, **Displays**, **Automation**, **Search folders**,
**Library**, **Helper processes**, **Appearance** with **Colors**, **Images** and **Icons and tray**, **Advanced**,
**Logs**); all of them are on one page, one below the other, and a click jumps there. At the top of the navigation is
the HDRpilot version, the same number as in the notification area menu; at the bottom, the number of games (without
hidden ones) and **Open game library**. In wide windows, the sections sit side by side in up to three columns, each at
most 760 px wide; in full screen the rest stays empty instead of stretching the sections.

**Save** (Ctrl+S) and **Cancel** (Esc) apply to all sections except Appearance. What you change under “Appearance”
applies and is saved immediately (**Applied immediately**); **Cancel** doesn't undo it, **Default** resets the whole
appearance. It contains:

- **Color scheme**: **Duotone** (accent and atmosphere; the shine is derived from the atmosphere) or **Tritone**
  (accent, atmosphere and shine), eight presets (Sunset, Cyber, Neon green, Royal, Glacier, Synthwave, Ember, Aurora)
  and custom colors: clicking a color swatch opens the color picker (field, hue bar, hex value). The accent colors
  buttons and selection, the atmosphere the background and glow, the shine the HDR moment and light streaks. All
  interface colors, the tray icon and the window icon follow the scheme; text on accent surfaces stays readable at a
  contrast of at least 4.5:1, switches and focus frames stand out by at least 3:1.
- **Live preview**: your most recently played games in the chosen scheme. The **HDR** switch shows the HDR moment only
  in the preview and doesn't switch HDR.
- **Theme**: **Automatic** (like the Windows app mode), **Light** or **Dark**.
- **Effects**: **Full**, **Reduced** (only short transitions up to 0.3 s, no continuous animation) or **Off**, plus
  **Pause while playing**, **Pause in the background**, **Respect Windows animation effects** and **Go to the
  notification area after launching a game**. The level applies everywhere: library, settings, dialogs and tray menu.
  Continuous animations run at no more than 30 frames per second and only while some of them are visible; with the
  window closed, nothing runs at all.
- **Images**: how many images per launcher are found locally and used, one example from your library for each
  substitute case, and **SteamGridDB (online)**.
- **Icons and tray**: tray and window icon in all states and at actual size, the tray menu and a taskbar for preview
  (**Light** or **Dark**, only for the preview).

**SteamGridDB (online)** is the only image source from the internet. Without your own API key from steamgriddb.com,
there's no network access for it. You enter the key and confirm it with **Check**; the field and button are always
usable, and **Remove key** deletes it again and also stops a running search. With a key, you can search yourself in
**Choose image…**, always only on click. Below that, **Load missing images automatically** (default: off) additionally
downloads hero images, logos and covers that are missing locally and that you haven't chosen yourself: after turning it
on, after checking a key, with **Download missing images now**, and at most once a day one minute after startup, never
during a game. The key is stored encrypted (DPAPI) for your Windows account and never written to the log; the line
below the key shows the last network access, run or search, with the number of requests. Turning it off or launching a
game stops a running automatic download immediately; when it's off, HDRpilot shows the local or generated images again,
and the automatically downloaded ones are kept in case you turn it on again. Images you applied in **Choose image…**
count as custom images and don't depend on the switch.

## How it works

**Detection (path A):** HDRpilot queries the process list once per second. If a program from the install folder of a
game with “HDR on launch” starts, HDRpilot turns HDR on. Plain launcher windows, crash reporters, installers and runtime
packages don't count. Launch helpers that start the game themselves (such as `gamelaunchhelper.exe` or `PlayRDR2.exe`)
deliberately count as the game already, so HDR is on early. HDRpilot remembers the EXEs detected on the first run and
shows them in the details.

**Early warning for Epic and Steam:** If you launch a game with “HDR on launch” from the Epic Games Launcher or from
Steam, HDRpilot picks this up from their own log files (read-only) and turns HDR on 1–2 s before the game process. If
the game doesn't come after all, the launch timeout restores HDR again. Can be turned off with `launcherSignals` in
`config.json`.

**Launch via HDRpilot (path B):** “Launch” or an “(HDR)” shortcut (`HDRpilot.exe --run <Source:ID>`) turns HDR on
first, waits for confirmation and only then launches the game: via the source's launcher link (such as `steam://`), and
games from GOG and the Quiver Launcher as well as games you added yourself directly via their EXE. This is the safe way
for games that only check HDR at startup. If no recognized game process appears within the launch timeout (default
120 s), for example because the launcher is still updating, HDRpilot restores HDR. As long as a program from the game
folder is running (such as a launcher window waiting for sign-in), HDRpilot keeps waiting, for at most 30 minutes. If
turning HDR on fails, the game launches anyway. HDRpilot ignores duplicate launch requests (an accidental double
double-click).

**Steam launch option:** With `"…\HDRpilot.exe" --wrap -- %command%` in the launch options of a Steam game, Steam starts
HDRpilot first. If HDRpilot is running, it turns HDR on, waits for confirmation and another 1.5 s, then starts the game
and exits immediately (it doesn't hold on to anything in the game). As with “Launch”, this also applies when automation
is paused and for games without “HDR on launch”. If HDRpilot isn't running or the game is unknown, the game starts
immediately without HDR.

**Quiver Launcher:** Quiver is portable and lives wherever you unpacked it. HDRpilot recognizes its folder by
`current\sq.version` with the identifier QuiverLauncher (up to version 2.4.x by `Quiver.exe`) and looks for it
read-only in this order: folders it has remembered; the log of Quiver's updater
(`%LOCALAPPDATA%\velopack\velopack_QuiverLauncher.log`); your search folders and their subfolders;
`%LOCALAPPDATA%\QuiverLauncher` and an entry under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\QuiverLauncher` (in case Quiver ever ships with a setup). If
that finds nothing, it searches Downloads, Desktop, Documents, the user folder and the fixed drives up to two levels deep
(at most 1500 folders, excluding Windows and program folders, only on fixed drives and without following linked
folders, so never on a network share). If Quiver is somewhere else, HDRpilot remembers the folder as soon as Quiver
starts while HDRpilot is running, and rescans a few seconds later. It reads `apps.json` and, from `settings.json`, only a
custom apps folder (also a drive root such as `G:\`); HDRpilot never changes Quiver's files. “Launch” starts the game EXE
directly with its folder as the working folder, just like Quiver itself: the EXE chosen in Quiver, otherwise the only
one at the top of the game folder, otherwise the only one up to three levels deep. A launch script (BAT or CMD) counts
here as it does in Quiver, but it's never started: if Quiver starts a script, if there are several candidates in the
game folder, or if the EXE is even deeper, HDRpilot doesn't know a launch EXE. Then launch the game once in Quiver and
choose the EXE there when Quiver asks; after “Rescan” it works via HDRpilot as well. As in Quiver, a game counts as
installed as soon as there's an EXE, BAT or CMD anywhere in the game folder (searched up to 16 levels deep, at most 3000
folders; if the search stops at this limit, the game counts as installed). Games that Quiver installed via Windows
Installer launch via the EXE that Quiver linked for them; their folder counts as the install folder. A running game is
always detected by its folder. If the same game is in two Quiver folders (for example while moving from Quiver 2.4 to
3.x), the 3.x folder wins, otherwise the most recently played one. If you added a Quiver game yourself earlier, it's in
the library twice; the details of both entries point this out, and nothing is removed automatically. HDRpilot reads the
cover from Quiver's `Cache` folder and “Last played” from Quiver's `LastPlayed.txt` (a date in another calendar, such as
the Thai one with the year 2569, doesn't count).

**Restore:** Before switching, HDRpilot remembers the HDR state of each affected display and saves it immediately.
When the last HDR game has ended and the grace period (10 s) has passed, HDRpilot restores this state. If several games
are running, it restores only after the last one, and only if all games involved have “Restore after game” on. This
setting applies from the next game launch. If HDR was already on before the game, HDRpilot doesn't switch anything at
the end. If you switch HDR manually during the game, HDRpilot doesn't override that; at the end it only turns HDR off
if it was off before the game, and never turns it back on.

**Restart buffer:** If a game ends less than 5 minutes after launch (typical for hangs or crashes) and HDRpilot turned
HDR on for it, HDR stays on for a while instead of just the grace period: default 1 minute, adjustable under “Settings”
in minutes and seconds (0:00 to 30:00; 0:00 turns the buffer off; if it isn't longer than the grace period, it acts like
0:00). A change applies from the next time a game ends, without restarting HDRpilot. If you restart the game during
this time, HDR is already on (if you turned it off manually in between, HDRpilot turns it on again for games with “HDR
on launch”). If a full-screen app is running when the buffer expires, HDRpilot waits with restoring until it ends (at
most 30 minutes; only after the buffer, never after the grace period). Games that HDRpilot takes over after its own
restart never count as short.

**Hanging game process:** If a game keeps running invisibly after you've finished playing (no window, no CPU time),
HDRpilot leaves HDR on, because the game is still running. After 2 minutes without any activity, a notification appears
once saying that the game seems to be hanging in the background; if a full-screen program is running or the screen is
locked, the notification waits until it can be shown. HDRpilot never ends anything itself.

**Exit, crash or restart:** If HDRpilot is closed during a game or during the grace period (also via “Exit HDRpilot”,
by a crash or when signing out), HDR stays on for now. At the next start, HDRpilot restores the remembered state as soon
as no game from that state is running anymore.

**Display without HDR or not connected:** HDRpilot reports this (during a full-screen game only in the log) and lets the
game run normally; it never blocks a game launch. When the displays change (connecting, resolution, waking from sleep),
HDRpilot catches up on anything pending: a display that was missing or couldn't do HDR at game launch is then turned
on, and a failed restore is retried.

**Game icons:** For each game, the first usable source applies, only from files on the PC (read-only): a custom icon;
for Steam, the official Steam icon (`steam\games\<Name>.ico` according to Steam's app data, otherwise the icon of the
“Steam App <ID>” entry under “Installed apps”, a Steam shortcut or the 32 px image from Steam's library cache); for GOG,
`goggame-<ID>.ico` in the game folder or the icon from the GOG Galaxy cache; for Xbox games, their logos; for Ubisoft,
the icon of the uninstall entry; for Epic, EA, Battle.net and the Quiver Launcher, the game EXE. Usable means: not a
known generic icon (EasyAntiCheat, Unreal, Epic Online Services, Godot, Unity, Windows program icon) and at least 48 px
in the file. For Steam, the game EXE therefore only gets its turn if the Steam icon is missing, is a generic icon or is
smaller than 48 px, while the EXE has at least 48 px. Anti-cheat launchers (EasyAntiCheat, BattlEye, EA Anti-Cheat,
GameGuard, PunkBuster, Epic Online Services), crash reporters, installers and tools never provide the icon, even if you
change the list of helper processes; for Unreal games, `<Project>\Binaries\Win64\*-Shipping.exe` comes first.
If no real icon is found at all, HDRpilot searches up to three folder levels deep (at most 300 folders and 1 s). It
remembers the chosen source in `icon-cache.json`, so the list doesn't search any game folders after a restart.
HDRpilot only loads icons for visible rows and only while the library is open.

**Game images:** HDRpilot reads hero image, logo and cover only from the launchers' image caches: Steam
`appcache\librarycache` (German versions first), GOG Galaxy `webcache`, Ubisoft Connect `cache\assets`, for Xbox games
the tile image in the package and for the Quiver Launcher the cover from its `Cache` folder (your own cover in Quiver,
otherwise the copy Quiver downloaded; new images from Quiver appear after the next scan). Epic, EA and Battle.net have no
such cache; there, for Quiver and for games you added yourself, the icon comes from the game EXE. If an image is
missing, HDRpilot generates a substitute: a hero image from the icon's colors with the name as a pattern, a cover from
the hero image or icon with the name, the name in the title font instead of a logo, and a monogram if there's no icon.
HDRpilot recognizes blurred Steam capsules (stretched capsule images as the only cover) and replaces them with a crop of
the hero image. It doesn't crop square covers from Quiver (480 × 480, as is usual), but places them at the very top of a
portrait cover and on the right of the generated hero image, like the tile image of an Xbox game; as with that one,
their center also fills the thumbnail in the list, and the colors come from this image. It trims logos to their visible
part.
It computes ambient colors once per image, and the brighter version of the hero image only while the library window is
active and never while a game is running or launching: shortly after the selection has settled, at low priority, so
it's ready when HDR turns on. It stores both with the substitute images under `%LOCALAPPDATA%\HDRpilot\art\`. It only
keeps launcher image files open until their content is in memory. While a window is open, it watches the launchers'
folders (for Ubisoft Connect also the product cache, but not Quiver's folder), so new images appear by themselves; it
keeps at most 192 MB of images in memory. With the window closed there's neither watching nor images in memory
(HDRpilot also releases the window's graphics memory a few seconds after closing), and it then cleans up images of games
that are no longer in the library; games from a source this version doesn't know count as present. Custom images
(“Choose image…”, at most 32 MB and 100 megapixels; larger photos are downscaled) are recorded per image type in
`config.json` (`artwork`) and stored in `art\custom\`.

**SteamGridDB:** only with your own key (see “Settings”); automatically only when turned on, in **Choose image…** only
on click. HDRpilot asks `www.steamgriddb.com`, for Steam games by their app ID, for others by name (automatically, only
a match with the same name counts; in **Choose image…** you otherwise choose from the list of matches, and for games
without a Steam app ID HDRpilot remembers the choice when you click **Apply**), and only for still images without an
NSFW, humor or epilepsy flag. HDRpilot downloads images only from https addresses under steamgriddb.com, without
redirects and without the key. API requests are at least 1 s apart from the end of the previous one, and every transfer
has time limits. Automatically, at most 15 MB per image and 200 requests per run apply, and anything SteamGridDB
doesn't have isn't requested again for 30 days; downloaded images are stored in `art\sgdb\`. In **Choose image…**, at
most 40 requests per window apply; previews are at most 4 MB, kept only in memory and at most three load at the same
time. An applied image is at most 32 MB and is then stored in `art\custom\`.

**Display values:** HDRpilot reads peak, full frame, minimum, color depth and whether HDR is on via the Windows
graphics API (DXGI), only while a window shows them and only after an HDR or display change. It remembers the peak with
HDR on in `display-luminance.json`; it's the maximum of every nit scale, even if HDR is currently off. If there's no
measurement with HDR yet, the current value applies. If Windows reports no values (for example with the display turned
off), it shows “—”.

**Switching:** via the Windows display API (DisplayConfig); afterwards the state is read again every time and thus
confirmed. From Windows 11 24H2 (build 26100), HDRpilot uses the new HDR API, otherwise the older “Advanced Color” API.
If the new API reports an error, HDRpilot automatically falls back to the older one.

## HDR list (PCGamingWiki)

HDRpilot shows which of your games support HDR according to [PCGamingWiki](https://www.pcgamingwiki.com). It syncs in
the background 30 s after every startup, at most once per hour, and at any time via **Sync now**.

- **Without an account (default):** HDRpilot asks PCGamingWiki for the pages of your games; this sends the game names
  to PCGamingWiki, nothing else. At most 100 games per startup (2 requests plus 2 searches, 2 s apart), new and
  recently played ones first, known ones again every 14 days. With a large library, everything has been checked after
  a few startups. Steam and GOG games are matched by their store ID, others by name.
- **With your own PCGamingWiki account:** Under Settings → “HDR list (PCGamingWiki)”, **Create account** and **Bot
  passwords** lead to PCGamingWiki. Create a bot password named HDRpilot there and select only the grant “Create, query
  and delete data through the Cargo extension”. Enter the user name (`Name@HDRpilot`) and bot password in HDRpilot and
  click **Save and test**. HDRpilot then downloads the complete list and matches it on your PC only; no game names are
  sent. The password is stored encrypted with Windows (DPAPI) in `pcgw-account.dat` and is readable only by your Windows
  user; **Remove account** deletes it.

In both cases, HDRpilot doesn't sync games from the Quiver Launcher: they're mostly ports of older games whose name
would hit the page of the original. So their names aren't sent to PCGamingWiki, and they show as “HDR UNKNOWN”.

Without a network connection, the last downloaded status is kept. PCGamingWiki is a small community project; HDRpilot
respects its request limits and caches the results. The HDR data is licensed under CC BY-NC-SA 3.0.

## Windows Auto HDR

“Windows Auto HDR” is a Windows feature: it converts the SDR picture of a game that can't do HDR itself to HDR. On
request, HDRpilot turns it on for you per game when you launch the game via HDRpilot. This is something different from
“HDR on launch”:

| | “HDR on launch” | “Windows Auto HDR” |
|---|---|---|
| What happens | HDRpilot turns Windows HDR on at game launch and restores it afterwards | Windows converts the SDR picture to HDR; for this, HDRpilot turns the Windows Auto HDR switch on right before the launch and restores it afterwards |
| When | at every detected game launch, also without HDRpilot | only when launched via HDRpilot: “Launch”, “(HDR)” shortcut, Steam launch option (plus the early warning from Steam and Epic) |
| Where | “AUTOMATION” section in the details and switch in the list | “PICTURE” section in the details; in the list only the “AUTO HDR” badge |
| Requires | an HDR-capable display | Windows HDR already on before the game starts, and a DirectX 11 or 12 game that Windows allows for Auto HDR |

The two switches are independent of each other. “Launch”, “(HDR)” shortcuts and the Steam launch option turn HDR on
before the game anyway; if “HDR on launch” is off for the game, a note below the switch says that HDR otherwise has to
be on before the game starts (always when Auto HDR is on, otherwise behind **Learn more**). If a game has native HDR
according to PCGamingWiki (“HDR” or “HDR*” badge), the title and switch are dimmed, and behind **Learn more** a note
advises using the game's own HDR, which usually looks better; you can still turn Auto HDR on.

**How it works:** Windows only applies Auto HDR if Windows HDR and the “Auto HDR” switch of the Windows settings
(System → Display → HDR) are on when the game process starts. This switch is stored as the token `AutoHDREnable=1` in the
value `DirectXUserGlobalSettings` under `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`. If you launch a game with
“Windows Auto HDR” via HDRpilot or the early warning kicks in, HDRpilot sets only this token right before (all other
entries in the value, their order and the terminator stay), turns HDR on and then launches the game. It remembers the
previous value in `autohdr-global.json`. It's restored after the game like the HDR state: after the grace period, or,
for a game that ran for less than 5 minutes, after the restart buffer (default 1 minute); if the game restarts during
that time, the switch stays on. If several such games are running, it stays on until the last one ends. If no game
process appears, HDRpilot restores it when the launch timeout ends, or immediately if the launch command itself fails.
When exiting, HDRpilot restores the switch immediately, after a crash at the next start, and uninstalling via the
setup takes care of any remainder. While the switch is on, Auto HDR applies to every game that starts during that time.

Without a launch via HDRpilot, HDRpilot never switches Auto HDR, not even when it sees a game with the option running:
Windows has already read the switch at game launch by then. For the same reason, a launch via HDRpilot sets nothing
while the game is already running (such as a second click on **Launch** or the “(HDR)” shortcut). HDRpilot only
touches values made of plain text with `key=value;` pairs and doesn't write any per-game-EXE values, only under HKCU,
never under HKLM, and needs no administrator rights.

**Your choice stays:** If Auto HDR was already on in Windows, HDRpilot changes nothing and doesn't turn it off either.
If you change the switch in Windows while HDRpilot is holding it, your setting stays, and the log notes “extern
geändert” (changed externally) (if you turn it off there and right back on, HDRpilot sees no difference and restores it
afterwards). You can also toggle the Windows switch itself in the notification area menu (**Auto HDR**): this applies
from the next game launch, also without HDRpilot, and is always your choice. If HDRpilot is currently holding the switch
for a game, it lets go of it, restores nothing afterwards and doesn't set it again for the running games. Every new
launch via HDRpilot reads the switch again, though: if it's off, that launch sets it for its game and restores your
state afterwards.

**Steam games:** If you launch a Steam game directly in Steam, HDRpilot only learns about the launch reliably via the
Steam launch option; the early warning from Steam's log doesn't always come before the game process. So when you turn
on “Windows Auto HDR” for a Steam game, HDRpilot checks (read-only in Steam's `localconfig.vdf`) whether the launch
option is already set. If it's missing, HDRpilot asks whether to copy it to the clipboard, like **Copy Steam launch
option**. You have to add it in Steam yourself (right-click on the game → Properties → General → Launch Options);
HDRpilot never changes Steam's files. Declining changes nothing; Auto HDR stays on and then takes effect when launched
via HDRpilot or the “(HDR)” shortcut, and when launched directly from Steam only if the early warning comes before the
game process.

**Live test (Windows 11 26H2, build 26300, 2026-10-12):**

- TUNIC (Unity, DirectX 11): with Auto HDR on in Windows and HDR on before the launch, the “Adjust HDR intensity”
  slider in the Game Bar is usable. The registry value is the same switch as in the Windows settings.
- The per-game-EXE value alone (`AutoHDREnable=2097`) with Auto HDR turned off has no effect. That's why HDRpilot uses
  the global switch.
- Turning Auto HDR on only while the game is running, or turning HDR on only after the game has started, only takes
  effect at the next launch. That's why HDRpilot sets both before the game process.

**Check:** If Auto HDR is working, the Auto HDR intensity slider in the Xbox Game Bar (Win+G) is usable. The Game Bar
settings window shows an outdated state until you close it completely and open it again.

NVIDIA's RTX HDR isn't part of HDRpilot; that's done in the NVIDIA app.

## Anti-cheat

HDRpilot doesn't interfere with games and is built so that anti-cheat systems have no reason to step in:

- It only reads the Windows process list. It asks Windows for a program's EXE path without opening the program itself;
  a game doesn't notice HDRpilot at all. For the hang notification, it also asks only Windows for CPU time and visible
  windows.
- It doesn't read or write game memory, inject code, set hooks, draw an overlay over the game, or change the priority
  or processor affinity of games. It only reads the Epic and Steam logs.
- It loads no driver and needs no administrator rights. It only uses the network for the HDR list and, with your own
  key, for SteamGridDB (automatically only if you turn it on; in **Choose image…** only on click).
- It switches HDR via the Windows display settings, like the HDR switch in the Windows settings.
- It switches “Windows Auto HDR” only via the Auto HDR switch in the Windows graphics settings under HKCU, like the
  switch in the Windows settings itself, and does so before the game launches; it doesn't touch the game or its files
  for this. There's no statement from anti-cheat vendors about it.
- Simulated key presses only exist in the keyboard shortcut fallback. It's off by default and never sends anything
  while a game is running or a full-screen app is in the foreground, not even when switching manually.
- It launches launcher games via the launcher link, so their anti-cheat launcher runs as usual. The Steam launch
  option starts exactly the command Steam specifies and exits immediately.

An automated test makes sure such techniques don't get into the source code. There's still no guarantee, because the
anti-cheat vendors' rules aren't public. If an anti-cheat flags HDRpilot (for example “Hack attempt detected” with
nProtect GameGuard), exit HDRpilot before playing that game and report the case to the game publisher. Add games with
anti-cheat that you add yourself (for example with an EasyAntiCheat or BattlEye folder) via their launch program, for
example `start_protected_game.exe`, not via the actual game EXE.

## File locations

| What | Where |
|---|---|
| Program (setup): HDRpilot.exe, `cli\hdrpilot.exe`, README (`README.md`, `README.de.md`), `LICENSE`, third-party notices | `%LOCALAPPDATA%\Programs\HDRpilot\` |
| Entry under “Installed apps” (setup) | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\HDRpilot` |
| Start menu shortcut (setup) | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\HDRpilot.lnk` |
| Log of setup and uninstall | `%TEMP%\HDRpilot-Setup.log` |
| Settings and game list | `%APPDATA%\HDRpilot\config.json` |
| Remembered HDR state during a game | `%LOCALAPPDATA%\HDRpilot\session-state.json` |
| Log (new file daily, at most 5 MB per file, 14 days) | `%LOCALAPPDATA%\HDRpilot\logs\` |
| Log of the Steam launch option (one line per launch, 256 KB) | `%LOCALAPPDATA%\HDRpilot\logs\wrap.log` |
| HDR data per game | `%LOCALAPPDATA%\HDRpilot\hdr-support.json` |
| Complete HDR list (only with an account) | `%LOCALAPPDATA%\HDRpilot\hdr-catalog.json` |
| PCGamingWiki account (encrypted, only with an account) | `%LOCALAPPDATA%\HDRpilot\pcgw-account.dat` |
| Custom game icons (one 96 px PNG per game, plus an ICO for shortcuts) | `%LOCALAPPDATA%\HDRpilot\icons\` |
| Remembered source of the game icons | `%LOCALAPPDATA%\HDRpilot\icon-cache.json` |
| Game images: `index.json` index (origin and colors per game), generated substitute images, trimmed logos, bright HDR versions, custom images and images from SteamGridDB | `%LOCALAPPDATA%\HDRpilot\art\` |
| SteamGridDB key (encrypted, only if entered) | `%LOCALAPPDATA%\HDRpilot\steamgriddb-key.dat` |
| Brightness per display last measured with HDR on (maximum of the nit displays) | `%LOCALAPPDATA%\HDRpilot\display-luminance.json` |
| Windows Auto HDR switch including its old value, while HDRpilot holds it for a game launch | `%LOCALAPPDATA%\HDRpilot\autohdr-global.json` |
| “done” marker in case `autohdr-global.json` couldn't be deleted (for example locked by an antivirus); both are deleted as soon as possible | `%LOCALAPPDATA%\HDRpilot\autohdr-global.done` |
| Windows Auto HDR (global switch, a Windows setting) | `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, value `DirectXUserGlobalSettings` |
| Autostart | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `HDRpilot` |
| Shortcuts | Desktop or `%APPDATA%\Microsoft\Windows\Start Menu\Programs\HDRpilot\`, “<Game> (HDR).lnk” |
| Unpacked graphics libraries (about 8 MB per version) | `%TEMP%\.net\HDRpilot\` |
| The same for the setup (about 8 MB per version) | `%TEMP%\.net\HDRpilot-Setup-<Version>\` |
| The same for uninstalling (about 8 MB, deletes itself afterwards) | `%TEMP%\.net\HDRpilot-Uninstall\` |

If you already had an earlier version: Windows doesn't distinguish upper and lower case in folder names, so the
previous `HdrPilot` folders keep being used, also as the setup's program folder. Setup and uninstall don't change any
settings or data; they're only deleted when uninstalling with the checkbox checked (see “Uninstalling”). The only
exceptions: uninstalling restores a remembered HDR state and then discards `session-state.json`, and it restores an
Auto HDR switch held by HDRpilot and then deletes `autohdr-global.json` together with an `autohdr-global.done` marker
(if the switch couldn't be restored, the file stays).

For each game, `config.json` also contains the launchers' icon hints (`iconCandidates`, usually one file), for Quiver
games the cover files from Quiver's cache (`coverCandidates`), and, if applicable, the custom icon (`customIcon`, only
the file name in the `icons` folder).

Settings apply per game by source and ID (such as `Steam:1091500`), not by name. `config.json` is written atomically.
It's best to edit it by hand while HDRpilot isn't running. If it's no longer valid JSON afterwards, HDRpilot backs it
up at the next start as `config.json.defekt-<time>.bak` and starts with default values; your choices are then only in
this backup. Games from a source this version doesn't know (for example from a newer version), on the other hand, stay
in the file unchanged, and HDRpilot reads the rest as usual.

HDRpilot unpacks the graphics libraries at the first start and after every update to a new version. Folders of older
versions stay and can be deleted while HDRpilot isn't running. The setup file and the copy for uninstalling unpack
their own (folders named after the EXE). You may delete the setup file's folder
(`%TEMP%\.net\HDRpilot-Setup-<Version>`) when no setup is running; the uninstall copy deletes its own folder
(`%TEMP%\.net\HDRpilot-Uninstall`) at the end, together with its temp folder.

## Command line

### HDRpilot.exe

If HDRpilot is already running, a second call passes its commands on to the running instance.

| Call | Effect |
|---|---|
| `HDRpilot.exe` | Start (only in the notification area); if it's already running, the game library opens |
| `--minimized` | Start (only in the notification area); if it's already running, nothing happens. This is how autostart starts it |
| `--show` / `--settings` | Open the game library or the settings |
| `--run <Source:ID>` | Launch a game via HDRpilot (HDR on first) |
| `--toggle-hdr` | Toggle HDR now |
| `--pause` / `--resume` | Pause or resume automation |
| `--rescan` | Rescan games |
| `--exit` | Exit the running instance |
| `--wrap -- <command>` | Steam launch option (`--wrap -- %command%`): have HDR turned on, then start the command |
| `--config-dir <folder>` | Separate storage instead of `%APPDATA%`/`%LOCALAPPDATA%` (tests); separate instance per folder |
| `--autohdr-test-root <name>` | Only with `--config-dir` (tests): Auto HDR switch under `HKCU\Software\HDRpilot.Tests\AutoHdr\<name>`. With a separate storage, HDRpilot never writes to the real Windows setting; without this switch, it uses a name derived from the folder |

The interface doesn't show the `<Source:ID>` key. You'll find it in the first column of `cli\hdrpilot.exe games list` or
in the target of an “(HDR)” shortcut after `--run`. Usually it's easier to have the shortcut created in the details.

### Setup file and uninstall

For scripts and troubleshooting; for everyday use, the setup window and “Installed apps” are enough.

| Call | Effect |
|---|---|
| `HDRpilot-Setup-1.6.0.exe --quiet` | Install or update without a window; the result is the exit code. Afterwards HDRpilot starts (with the game library if it wasn't running), except with `--no-launch` or with administrator rights. Scripts that wait for the end or read the output use `--no-launch` |
| `--autostart on\|off` | Start with Windows, afterwards with the installed EXE (if not specified: as before, i.e. on if an HDRpilot.exe already starts with Windows, even from a different folder; on for a new installation) |
| `--desktop-shortcut on\|off` | Desktop shortcut (if not specified: as before; off for a new installation) |
| `--no-launch` | Don't start HDRpilot afterwards. For scripts that wait for the end (such as `Start-Process -Wait`, which also waits for started programs) or read the output |
| `--allow-downgrade` | Install an older version over a newer one |
| `HDRpilot.exe --uninstall` | Uninstall as from “Installed apps” (runs from a copy in the temp folder) |
| `--quiet` | Uninstall without a window: returns immediately. 0 means the copy in the temp folder is now uninstalling; its result then appears in the log as `ERGEBNIS: <exit code>`. 1 (copy not started), 2 (wrong usage) and 7 (container) come directly from the first call, without an `ERGEBNIS` line; in that case nothing was removed |
| `--remove-data` | When uninstalling, also delete settings, game list, logs and HDR list |
| `--test-root <folder>` | Test runs: program folder, shortcuts and storage under this folder, plus a separate registry branch under `HKCU\Software\HDRpilot.Tests` |
| `--ui-language de\|en` | Only with `--test-root`: fixed window language instead of following Windows |

Exit codes: 0 ok, 1 error, 2 wrong usage, 3 older version declined, 4 HDRpilot couldn't be closed, 5 canceled, 6 not
installed, 7 started from another app's container, nothing changed (see “Troubleshooting”). Both write their log to
`%TEMP%\HDRpilot-Setup.log`.

### cli\hdrpilot.exe

A tool for troubleshooting and scripts, not needed for everyday use.

```
hdrpilot hdr status [--json] [--api modern|legacy]
hdrpilot hdr on|off|toggle [--display <selection>] [--api modern|legacy] [--hotkey-fallback] [--json]
hdrpilot scan [--source <source>] [--search "<folder>;<folder>"] [--config-dir <folder>]
hdrpilot games list [--json]
hdrpilot games import
hdrpilot games add --path <exe|folder> [--name <name>] [--hdr on|off]
hdrpilot games set <key> [--hdr on|off] [--restore on|off] [--display <monitorDevicePath>|global]
hdrpilot watch [--verbose]
hdrpilot run <key>
```

`<selection>`: `primary` (default), `all`, number from `hdr status`, part of the name or `monitorDevicePath`.
`<source>`: Steam, Epic, Xbox, Gog, Ubisoft, EA, BattleNet, Quiver. `scan`, `games`, `watch` and `run` also accept
`--config-dir <folder>`; `scan` then adds the search folders and remembered Quiver folders of that storage (read, not
written) and otherwise searches as always. `hdrpilot --help` shows the full help. Output and scanner messages come in
the language of the storage (`language` in `config.json`; without a file, like Windows).

Exit codes: 0 ok, 1 error, 2 wrong usage, 3 display not found, 4 display without HDR, 5 switching failed, 6 game not
found.

You can use the `games` commands alongside the running app; HDRpilot rereads the changes by itself. `hdrpilot watch`
and `hdrpilot run`, on the other hand, run the same automation as the app and, without `--config-dir`, use the same
storage. They don't check whether the app is running: if it is, both switch independently and overwrite each other's
remembered HDR state. Exit HDRpilot first, or use `HDRpilot.exe --run <Source:ID>` while the app is running.

## Known limitations

- **Detection by polling:** Between game launch and HDR there can be up to one polling interval (default 1 s) plus the
  switching time, except for Epic and Steam with early warning. Launch games that only check HDR at startup via
  “Launch”, an “(HDR)” shortcut or (for Steam) the Steam launch option.
- **Game EXE outside the install folder:** Some launchers start the actual EXE somewhere else. HDRpilot only detects
  such games once you add the EXE in the details; until then, “Launch” also restores HDR after the launch timeout.
- **Multiple displays:** HDRpilot doesn't detect which display a game is running on, but switches the configured ones
  (default: primary display). If a game runs elsewhere, choose the right **Display** in the details. On real hardware,
  only a single display was tested.
- **Not found automatically**: non-Steam games added to Steam, older Store games directly under
  `C:\Program Files\WindowsApps`, Xbox games on USB or network drives, GOG games without a registry entry and Epic games
  without a manifest. Add such games via “Add game”.
- **Launching via the launchers:** Tested with real games: Steam (Dying Light: The Beast), Epic (Dead Island 2), GOG
  (Cyberpunk 2077), Ubisoft Connect (Avatar: Frontiers of Pandora), EA app (Battlefield 6, with EA Javelin) and Xbox
  (Palworld), each via “Launch” and via the shortcut. HDR was on every time before the launcher started the game.
  Battle.net is untested (see below).
- **Switching during game launch:** With automatic detection, HDRpilot switches HDR while the game is starting up. Almost
  all games cope with this; in the test, Dead Island 2 hung on one of two automatic launches (whether because of the
  switch isn't certain). The restart buffer helps on the second attempt if the restart falls within its time
  (default 1 minute; if you need longer to restart, increase it under “Settings”); safer options are “Launch”, the
  shortcut or the Steam launch option.
- **Early warning:** only for the Epic Games Launcher and Steam. It relies on their log format, which can change with a
  launcher update; normal detection then takes over again.
- **Steam launch option:** only works if HDRpilot is running (autostart recommended) and the game was already known at
  the last scan. If Steam runs with administrator rights but HDRpilot doesn't, the launch can be delayed by up to 8 s.
- **HDR list:** Without an account, games without a Steam or GOG ID are only found if their name matches the
  PCGamingWiki page. The account path is only tested against simulated responses, because no account was available
  during development. The data comes from volunteers and can be incomplete.
- **Hang notification:** Older Store games (UWP) whose window belongs to a Windows host process could be wrongly
  reported as hanging while minimized. It stays at a notification.
- **GOG:** “Launch” starts the game EXE directly, bypassing GOG Galaxy. Galaxy's cloud saves, overlay and play time
  may then not be active.
- **EA app:** The EA app's library file is encrypted and bound to the hardware; HDRpilot deliberately doesn't decrypt it
  and finds EA games via the games' own installation data.
- **Battle.net:** No Blizzard game was installed on the test PC. Scanning Blizzard games is therefore only tested with
  simulated data, launching via Battle.net not at all. If a Blizzard game is missing, add its folder via “Add game”.
- **Quiver Launcher:** No early warning and no launch option like Steam's: if you launch a game in Quiver, HDRpilot
  only switches HDR when it sees the game process (up to one polling interval). Launch games that only check HDR at
  startup via “Launch” or the “(HDR)” shortcut. HDRpilot only finds a Quiver folder outside the searched locations once
  Quiver runs while HDRpilot is running, or if it (or its parent folder) is set as a search folder; after moving it, its
  games count as “Not installed” until then. Quiver's files aren't an official interface and can change with an
  update; whatever HDRpilot can't read, the library reports as a scan error. Quiver has no hero image or logo, and the
  covers are mostly WebP (requires the Windows WebP extension). HDRpilot never starts launch scripts (BAT, CMD), even
  if Quiver launches a game that way. If Quiver replaces a file in exactly the millisecond in which HDRpilot reads it
  (an image in `Cache` or the `windows-installer.json` link), Windows reports “access denied” to Quiver once; HDRpilot
  only keeps each file open as long as reading takes. Tested on the test PC with Quiver 3.5.1 and one game (“Black”),
  read-only; games from Windows Installer and Quiver up to 2.4.x only with simulated data, launching from Quiver with
  HDR not yet.
- **Older version after Quiver games:** HDRpilot 1.4.0 and older don't know the “Quiver” source. If a Quiver game is in
  `config.json`, they consider the whole file broken, back it up as `config.json.defekt-<time>.bak` and start empty.
  The setup therefore only installs an older version with **Install the older version anyway**; from this version on,
  games from unknown sources are kept.
- **Windows 10:** The older API is covered by tests and was also checked on real hardware under Windows 11
  (build 26200); a real Windows 10 wasn't available.
- **Keyboard shortcut fallback:** HDRpilot only sends Win+Alt+B if the fallback is turned on, the Windows API has
  failed and no game is running (see “Anti-cheat”). The shortcut requires the Xbox Game Bar, only toggles and affects
  all HDR-capable displays; HDRpilot, however, only confirms the target display. The fallback isn't tested on real
  hardware.
- **Game icons:** For some Steam games, only a 32 px icon is on the PC; it looks a bit blurry in the list (measured: 24
  of 636). Images in WebP format (GOG Galaxy) require the Windows WebP extension; if it's missing, HDRpilot uses the
  next source. A custom icon only applies on this PC, because the PNG is in the local data folder; on another PC the
  game shows the automatic icon again.
- **Windows Auto HDR:** only works for games with DirectX 11 or 12 (not DirectX 9, OpenGL or Vulkan), only for games
  that Windows allows for it (can't be queried in advance), only when launched via HDRpilot (“Launch”, “(HDR)”
  shortcut, Steam launch option, early warning) and only if Windows HDR is already on before the game starts; a game
  that's already running only gets it at the next launch (live test under Windows 11 26H2). While HDRpilot holds the
  switch, Auto HDR also applies to other games that start during that time. The format of the Windows setting isn't
  documented and can change with a Windows update; HDRpilot then leaves a value in an unknown format alone. HDRpilot
  can't see by itself whether Auto HDR is working (check via the Xbox Game Bar). There's no statement from anti-cheat
  vendors. HDRpilot doesn't set the intensity or force Auto HDR for games that aren't allowed; NVIDIA's RTX HDR isn't
  part of HDRpilot.
- **Not signed:** SmartScreen and some antivirus programs warn about unknown, unsigned EXEs, including the setup.
- **German and English:** The interface, notifications, setup, launcher scanner messages and command line are
  available in German and English, the log in German (messages that also appear in the interface are in its language).
  Error texts that Windows or .NET provide themselves (such as “access denied” after a message) come in the language of
  Windows.

## Troubleshooting

- **HDR doesn't switch at all:** Can you turn HDR on manually in the Windows display settings? If the tooltip says “no
  HDR-capable display”, Windows reports HDR for none of the target displays. `cli\hdrpilot.exe hdr status` shows what
  Windows reports; `hdr on` and `hdr off` switch manually for testing. Notifications during a full-screen game only
  appear in the log.
- **Game isn't detected:** Is “HDR on launch” on and automation not paused? Is the running EXE in the install folder?
  If not, add the EXE in the details. If it's marked as “excluded” there, bring it back with “Include again”. If it's
  listed under “Exclude helper processes”, remove the pattern there.
- **HDR comes too late for a game, or the game hangs on an automatic launch:** launch it via “Launch”, an “(HDR)”
  shortcut or (for Steam) the Steam launch option; then HDR is already on before the game.
- **Steam launch option has no effect:** Is HDRpilot running? `logs\wrap.log` shows for each launch whether HDR was
  prepared and why not. If HDRpilot doesn't know the game yet, use “Rescan” once.
- **HDR stays on after the game:** If the game ended less than 5 minutes after launch, the restart buffer keeps HDR on
  (default 1 minute; the tooltip shows the remaining time). If that's too long for you, set a shorter time or 0:00 (off)
  for the restart buffer under “Settings”. Otherwise, wait for the grace period (default 10 s). If the tooltip still
  names the game after that, a program from its folder is still running (such as a launcher remnant): check in Task
  Manager and remove that EXE in the details. Some games don't exit completely and keep running invisibly (in the test,
  Palworld); HDRpilot points this out after 2 minutes. Then end the game process in Task Manager. Other reasons: another
  HDR game is still running; HDR was already on before the game; “Restore after game” is off for this game or for a game
  that ran at the same time. After “Launch” without the game starting, HDR stays on until the launch timeout ends.
- **“Launch” is disabled:** The game isn't installed, or for a folder you added yourself HDRpilot doesn't know a launch
  EXE. In that case, add the game via its EXE. For a game from the Quiver Launcher, there are usually several EXE files
  in the game folder, the EXE is very deep, or Quiver starts a script (BAT or CMD): launch the game once in Quiver,
  choose the EXE there when Quiver asks, and then use “Rescan”.
- **Windows Auto HDR has no effect:** Did you launch the game via HDRpilot (“Launch”, “(HDR)” shortcut or Steam launch
  option)? A launch directly in the launcher without the launch option only gets Auto HDR via the early warning, and
  then only if Windows HDR is already on. The game has to use DirectX 11 or 12 and be allowed by Windows for Auto HDR;
  the intensity slider in the Xbox Game Bar (Win+G) shows whether it applies. The Game Bar settings window shows an
  outdated state until you close it completely and open it again. When HDRpilot set and restored the switch is in the
  log with the previous value (“Auto HDR global”). The **Auto HDR** entry in the notification area menu shows the current
  state of the Windows switch.
- **No HDR badges in the list:** The first sync runs 30 s after startup; without an account, only 100 games are covered
  per startup. The “HDR list (PCGamingWiki)” section in the settings shows the status and the last error.
- **HDRpilot crashed or was closed:** just start it again; the remembered state is then restored.
- **Setup or uninstall reports an error:** Details are in `%TEMP%\HDRpilot-Setup.log`; after an error in the setup, the
  previous version is unchanged. If HDRpilot can't be closed, choose “Exit HDRpilot” in the notification area and try
  again.
- **HDRpilot doesn't start by itself, or settings are gone after a start from another app:** Some apps from the
  Microsoft Store or packaged as MSIX (such as the Claude app) run everything started from within them in their own
  container. Windows then redirects registry entries such as autostart to that app's private storage
  (`%LOCALAPPDATA%\Packages\<App>\LocalCache`), and settings and logs if their folder outside was still missing or is
  already there. What was redirected is missing outside, and autostart doesn't work. HDRpilot detects this wherever its
  folders are: at startup, a warning “HDRpilot läuft im Container der App …” (HDRpilot is running in the container of
  the app …) appears in the log (depending on the folder, in the redirected copy under
  `…\LocalCache\Local\HDRpilot\logs`), and setup and uninstall change nothing and say so (quietly: exit code 7). Fix:
  exit HDRpilot and start it from the Start menu or the shortcut, run setup files by double-clicking them in File
  Explorer, and uninstall via “Installed apps”.
- **A game is missing from the list:** Click “Rescan”; if it's still missing, add it via “Add game” or set its parent
  folder as a search folder. If all games from the Quiver Launcher are missing, HDRpilot doesn't know the Quiver folder
  yet: start Quiver once while HDRpilot is running, or set the Quiver folder as a search folder (see “Quiver
  Launcher”). Errors of individual launchers are shown in the library and in the log. If the “Hide uninstalled games”
  switch is on under “Settings” › “Library”, uninstalled games aren't in the list; if the list is empty because of
  that, it says so, and if the search only finds hidden games, the note “No games for this selection” says so.
- **An installed game says “Not installed”:** Is it on a drive that isn't connected right now, is it in the middle of
  an update in the launcher, or did the scan not find the launcher (see the log)? Then don't choose “Remove settings…”
  (the confirmation warns if the drive isn't connected, an EXE of the game is still there, Steam still lists it as
  installed, or this can't be checked; a leftover folder with only saves doesn't count): after connecting the drive or
  finishing the update and using “Rescan”, its settings apply again.
- **Log:** Notification area → “Open log folder”. For more detail, set the log level to “Detailed” under Settings →
  Advanced. Every switch is recorded there with its trigger, display, and state before and after.
- **Reset everything:** Exit HDRpilot and delete `%APPDATA%\HDRpilot\config.json`. The games are scanned again at the
  next start; your choices are lost.

## Uninstalling

Installed with the setup:

1. In Steam, delete the launch option `--wrap -- %command%` from the games where you added it (right-click on the game
   → Properties → General → Launch Options), otherwise these games won't start anymore. The uninstall lists the games
   where it's still set.
2. Under *Settings → Apps → Installed apps*, choose *Uninstall* for HDRpilot. HDRpilot asks for confirmation: without a
   check mark at **Also delete settings, game list and logs** (default), `%APPDATA%\HDRpilot` and
   `%LOCALAPPDATA%\HDRpilot` are kept and used again by a new installation; with the check mark they're gone, including
   the HDR list and a saved PCGamingWiki account.
3. **Uninstall** closes HDRpilot, restores a remembered HDR state, restores the Windows Auto HDR switch if a crash of
   HDRpilot left it turned on (step **Restore Auto HDR**), and removes the program, its shortcuts and autostart. This is
   done by a copy in the temp folder, which then deletes its folder and its unpacked graphics libraries
   (`%TEMP%\.net\HDRpilot-Uninstall`) itself.
4. If “(HDR)” shortcuts for individual games are left over, you can delete them, as well as the unpacked graphics
   libraries in `%TEMP%\.net\HDRpilot` and `%TEMP%\.net\HDRpilot-Setup-<Version>` (about 8 MB per folder).

Without installation:

1. In the settings, turn off **Start with Windows** and, if applicable, **Remove account**.
2. In Steam, delete the launch option `--wrap -- %command%` again from the games where you added it.
3. Wait until no game is running anymore and HDR has been restored, then choose **Exit HDRpilot** in the notification
   area.
4. Delete the `HDRpilot` folder (`HDRpilot.exe`, `cli`, `README.md`, `README.de.md`, `LICENSE` and the third-party
   notices), as well as the folders `%APPDATA%\HDRpilot`, `%LOCALAPPDATA%\HDRpilot`, `%TEMP%\.net\HDRpilot`, the Start
   menu folder `%APPDATA%\Microsoft\Windows\Start Menu\Programs\HDRpilot` and the “(HDR)” shortcuts on the desktop.

## Building from source

Requires the .NET 10 SDK. The structure tests also need four Windows SDK headers: `tools\fetch-sdk-headers.ps1` downloads
them once (about 153 MB from nuget.org) to `.sdk\`. Alternatively, the environment variable `HDRPILOT_SDK_INCLUDE` points
to the `um` folder of an installed Windows SDK. Without the headers, the test run skips these tests and `test.ps1`
reports an error.

```
dotnet build HdrPilot.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File tools\fetch-sdk-headers.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\gates\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\publish.ps1
```

`tools\publish.ps1` puts the finished release under `artifacts\publish\HDRpilot\` and the setup next to it as
`artifacts\publish\HDRpilot-Setup-1.6.0.exe` (the same EXE with an appended package of both READMEs, license text,
third-party notices and command line, see E83 in `docs\entscheidungen.md`). Projects and namespaces in the source code
are still called `HdrPilot.*`. The reasons behind design decisions are in `docs\entscheidungen.md` and the verification
records in `GATES.md` (both in German).

## License

HDRpilot is licensed under the PolyForm Strict License 1.0.0, Copyright Dante_GE. Only the license text in
[`LICENSE`](LICENSE) is authoritative; it's also included with the setup and the folder without installation. In
short:

- Use for non-commercial purposes is permitted, for example privately, for learning and trying things out, as a hobby
  or in charitable and public institutions.
- Not permitted are changes and derivative works, any distribution (modified or unmodified) and commercial use.

Third-party components keep their own licenses (see “Third-party licenses”). The test data from PCGamingWiki (mainly
`tests\HdrPilot.Tests\Fixtures\Pcgw\`) is also an exception to the PolyForm license: like its source, it's licensed
under CC BY-NC-SA 3.0.

Ready-made releases (setup file and folder without installation) are published on
[GitHub Releases](https://github.com/DanteGE/HDRpilot/releases).

## Third-party licenses

HDRpilot contains the .NET runtime including WPF and Windows Forms as well as the packages
`Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions`, all under the
MIT license. The HDR data comes from PCGamingWiki (pcgamingwiki.com) under the CC BY-NC-SA 3.0 license; it's retrieved
at runtime, not shipped. The license texts and the notices on third-party code within the .NET runtime are included in
the release as `THIRD-PARTY-NOTICES.txt`. No components are licensed under GPL or AGPL.
