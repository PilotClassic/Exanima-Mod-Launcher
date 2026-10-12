# Exanima Mod Launcher

A launcher for [Exanima](https://store.steampowered.com/app/362490/Exanima/) with a Play button, a mod manager and the game's graphics and audio settings. It is styled with the game's own logo, fonts and menu background, which it reads from your installed copy of the game. No game files are included.

This is a fan-made tool. It is not made or supported by Bare Mettle Entertainment.

## Features

- **Play:** starts the game.
- **Mods:** lists the mods in the game's `mods` folder. You can enable or disable each one, change the load order and edit its settings.
- **Settings:** the game's graphics and audio options.
- **Updates:** on start, the launcher checks this repository for a newer release and offers to update itself.

## Installing

1. Download `ExanimaLauncher.exe` and `ExanimaLauncher.ini` from the [latest release](../../releases/latest).
2. Put both files in the Exanima game folder, next to `Exanima.exe`. In Steam, you can find the folder with right-click on Exanima > Manage > Browse local files.
3. Run `ExanimaLauncher.exe`.

To have Steam's Play button open the launcher, set the game's launch options (right-click on Exanima > Properties > Launch options) to:

```
"<game folder>\ExanimaLauncher.exe" %command%
```

Requirements: Windows 10 or 11 (the launcher uses .NET Framework 4.8, which is part of Windows).

## Updates

When a newer release exists, the launcher shows its version and release notes with **Update**, **Skip this version** and **Later**. Updating replaces only `ExanimaLauncher.exe`; your mods, load order and settings are kept. The launcher never updates while the game is running, and it refuses a download that does not match the release's checksum.

The repository it checks is set in `ExanimaLauncher.ini`:

```ini
[Update]
Repo=PilotClassic/Exanima-Mod-Launcher
```

With `Repo` empty, the launcher never goes online. Each check is logged to `%LOCALAPPDATA%\ExanimaLauncher\update.log`.

## Building

Run `build.ps1` in PowerShell. It uses the C# compiler included with Windows and writes `release-files\`:

- `ExanimaLauncher.exe`
- `ExanimaLauncher.exe.sha256`
- `ExanimaLauncher.ini`

## Publishing a release

1. Raise the version in `src\AssemblyInfo.cs` (for example `1.1.0.0`).
2. Run `build.ps1`. It prints the release tag to use (for example `v1.1.0`).
3. On GitHub, create a release with that tag and your release notes. Attach `ExanimaLauncher.exe`, `ExanimaLauncher.exe.sha256` and `ExanimaLauncher.ini` from `release-files\`.

The launcher only offers a release whose tag matches its version format (`v1.1.0` or `1.1.0`) and that has `ExanimaLauncher.exe` attached. Drafts and pre-releases are ignored.
