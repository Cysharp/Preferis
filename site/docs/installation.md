---
title: Installation
sidebar_position: 2
---

Follow these steps to install Preferis.

## Installing on Windows

1. Download the latest installer (`Preferis-win-x64-Setup.exe` or `Preferis-win-arm64-Setup.exe`) from the [GitHub releases page](https://github.com/cysharp/Preferis/releases).
   - Installers are available for x64 and Arm. Choose the one that matches your system.
2. Run the downloaded installer and follow the on-screen instructions to complete the installation.
   - Preferis is installed for the current user under `%LOCALAPPDATA%\Preferis`. Administrator rights are not required.
   - The settings window opens when the installation finishes, and Preferis is added to the Start menu.

:::caution
The Windows installer and executables are not code signed yet, so Windows SmartScreen may warn you when you run the installer. Choose **More info** and then **Run anyway** to continue. This may change in the future.
:::

### Portable version

If you prefer not to install anything, download `Preferis-<version>-win-x64.zip` (or `win-arm64`) instead and extract the `Preferis` folder it contains to any location. Run `Preferis.exe` in that folder to open the settings and set Preferis as the default browser.

The portable version does not update itself. To update it, extract a new version over the same folder. Your settings are kept, because they are stored outside the folder (see [Configuration Files](configuration-files.md)).

## Installing on macOS

1. Download the latest `.dmg` file from the [GitHub releases page](https://github.com/cysharp/Preferis/releases).
2. Drag Preferis from the downloaded disk image into the Applications folder.
3. In Terminal, run `xattr -cr /Applications/Preferis.app` to remove the security attributes applied when the app was downloaded.

:::caution
The macOS version of Preferis is not currently signed or notarized by Apple, so you need to follow the steps above after downloading it. This may change in the future.
:::

## Updating

- **Windows, installed with the installer**: Preferis can update itself. Open the settings, select **General** in the sidebar and use the **Updates** section at the bottom of the page. **Check for Updates** looks for a newer release on GitHub, **Download** fetches it, and **Restart to Update** installs it and starts Preferis again. Your settings and the default browser registration are kept.
- **Windows, portable version** and **macOS**: download the new version from the releases page and replace the previous one (on macOS, drag the new Preferis into the Applications folder and repeat the `xattr` step). Your settings are kept.

:::note
Checking for updates reads the GitHub releases of Preferis. While the repository is private, the check fails with "404 (Not Found)"; download the new installer from the releases page instead.
:::
