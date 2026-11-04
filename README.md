# HDRpilot
A sleek, 100% vibe-coded Windows app that automatically enables HDR for selected games. Features optional Auto HDR for SDR games, shortcut and Steam launch option integration, an intuitive GUI, and support for Steam, Epic Games, Xbox/Game Pass, GOG, Ubisoft Connect, EA app, Battle.net, and Quiver Launcher.

Deutsch: [README.de.md](README.de.md) · Full manual: [docs/manual.md](docs/manual.md)

![HDRpilot game library in the Grid view with a demo library][shot-grid]

## What it does

HDRpilot turns Windows HDR on automatically when you launch a selected game and restores the previous state
afterwards. It finds your installed games by itself and shows which of them support HDR according to PCGamingWiki.
Games that only check HDR at startup can be launched through HDRpilot, so HDR is already on when they start. In
everyday use, HDRpilot runs quietly as an icon in the notification area.

## Features

- **HDR on launch** per game: HDR turns on when the game starts and goes back to how it was after the game ends.
- Launch with HDR already on: **Launch** in the library, “(HDR)” shortcuts on the desktop or in the Start menu, or the
  Steam launch option.
- Game library with List, Grid and Shelf views for Steam, Epic, Xbox/Game Pass, GOG, Ubisoft Connect, EA app,
  Battle.net and Quiver Launcher, plus games you add yourself. Hero images, logos and covers come from the launchers'
  image caches on your PC; missing ones are generated from the game's icon and name.
- HDR list from PCGamingWiki (native, limited or via mod); the **HDR-capable** filter shows which games support HDR.
- Optional **Windows Auto HDR** per game for SDR games (DirectX 11 or 12) launched via HDRpilot.
- Optional image search on SteamGridDB with your own API key (**Search SteamGridDB** in **Choose image…**).
- Optional **Colors follow the game** (off by default): the library's accents take on the colors of the selected
  game's logo and cover.
- English and German interface; a new installation picks German on a German Windows and English otherwise
  (changeable in Settings).
- No administrator rights, no Windows service, no driver. In the background, HDRpilot checks the process list once per
  second by default.

## Screenshots

| List view | Shelf view |
|---|---|
| ![List view with the details of the selected game][shot-list] | ![Shelf view at the moment HDR turns on, with a large hero image, the automation card and a row of covers][shot-shelf] |

| Appearance settings | Colors follow the game |
|---|---|
| ![Appearance settings with color scheme, presets, live preview and theme][shot-settings] | ![Library accents in the colors of the selected game][shot-colors] |

For copyright reasons, the screenshots show a demo library with made-up games and generated images instead of real game art.

## Download and install

Download `HDRpilot-Setup-<version>.exe` from [GitHub Releases](https://github.com/DanteGE/HDRpilot/releases) and run
it. The setup installs HDRpilot for your Windows user only, to `%LOCALAPPDATA%\Programs\HDRpilot`, without
administrator rights. It adds a Start menu shortcut and an entry under *Settings → Apps → Installed apps*.

The setup isn't signed, so Windows SmartScreen asks the first time you run it: choose “More info” → “Run anyway”.

To update, run the setup of the new version. It replaces the program in place and starts HDRpilot again if it was
running. Settings, game list and logs are kept, because they're stored in `%APPDATA%\HDRpilot` and
`%LOCALAPPDATA%\HDRpilot`. To uninstall, use *Settings → Apps → Installed apps*; first remove the Steam launch option
from games where you added it (see [Uninstalling](docs/manual.md#uninstalling)).

## Quick start

1. Run the setup and click **Install**. With **Launch HDRpilot afterwards** (preselected), the game library opens
   when it's done.
2. HDRpilot finds the games of your launchers by itself. Add a missing game with **Add game**.
3. Turn on **HDR on launch** for every game that should get HDR. Found games start with it off; the **HDR-capable**
   filter helps you pick.
4. Play as usual: HDRpilot turns HDR on when the game starts and restores the previous state after it ends. If a game
   only checks HDR at startup, start it with **Launch** or a “(HDR)” shortcut instead.
5. Closing the window keeps HDRpilot running in the notification area (bottom right, next to the clock).
   Double-click the icon to open the library; to exit, right-click the icon and choose **Exit HDRpilot**.

## Requirements

- Windows 11, or Windows 10 version 21H2 or later, 64-bit.
- An HDR-capable display for which “Use HDR” can be turned on under *Settings → System → Display*.
- No .NET installation needed; the runtime is built into the EXE.

## Privacy and network

- No telemetry and no interference with game processes. HDRpilot reads the Windows process list and the launchers'
  data on your PC without changing them.
- PCGamingWiki (on by default): 30 s after every startup (at most once per hour) and when you click **Sync now**,
  HDRpilot asks PCGamingWiki which of your games support HDR. Without an account, this sends the names of your games,
  nothing else; with your own PCGamingWiki account, HDRpilot downloads the complete list and matches it on your PC.
  Turn off **Sync at every startup** to stop the automatic sync.
- SteamGridDB (only with your own API key): HDRpilot searches only when you click **Search SteamGridDB**, and fills in
  missing images in the background only if you turn on **Load missing images automatically**. It sends the game's
  Steam app ID or name. The key is stored encrypted for your Windows account.
- Apart from that, HDRpilot doesn't go online. Without SteamGridDB, game images come only from files on your PC.

## Documentation

The [manual](docs/manual.md) covers everything in detail: [installation](docs/manual.md#installation),
[usage](docs/manual.md#usage), [how it works](docs/manual.md#how-it-works),
[Windows Auto HDR](docs/manual.md#windows-auto-hdr), [command line](docs/manual.md#command-line),
[file locations](docs/manual.md#file-locations), [known limitations](docs/manual.md#known-limitations),
[troubleshooting](docs/manual.md#troubleshooting) and [building from source](docs/manual.md#building-from-source).

The setup also installs this file in the program folder, where images and relative links don't work. The manual is
online at <https://github.com/DanteGE/HDRpilot/blob/main/docs/manual.md>.

Deutsch: [README.de.md](README.de.md) · [Handbuch](docs/handbuch.md)

## License

HDRpilot is licensed under the PolyForm Strict License 1.0.0, Copyright Dante_GE. In short: non-commercial use is
permitted; changes, derivative works, any distribution and commercial use are not. Only the license text in
[`LICENSE`](LICENSE) is authoritative.

Third-party components keep their own licenses: the .NET runtime (including WPF and Windows Forms) and the two
Microsoft.Extensions packages are under the MIT license, and the HDR data from PCGamingWiki is under CC BY-NC-SA 3.0
(retrieved at runtime, not shipped; the PCGamingWiki test data in this repository is under the same license). The
release includes the license texts as `THIRD-PARTY-NOTICES.txt`; see also
[Third-party licenses](docs/manual.md#third-party-licenses).

<!-- Screenshots: all image files in one place (made with tools/readme-screenshots.ps1 from a demo library) -->
[shot-grid]: docs/images/library-grid.png
[shot-list]: docs/images/library-list.png
[shot-shelf]: docs/images/library-shelf.png
[shot-settings]: docs/images/settings-appearance.png
[shot-colors]: docs/images/game-colors.png
