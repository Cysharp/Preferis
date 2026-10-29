---
title: Configuration Files
---

Preferis uses two configuration files: **config.json** and **local.json**.

- **config.json**: Stores the main Preferis settings, such as profiles and rules. You can place this file in cloud storage and access it from each device.
- **local.json**: Stores settings specific to each device.

:::note
Before editing the JSON files directly, make sure Preferis is not running. Otherwise, you could unintentionally lose settings.
:::

## Configuration file paths

### Default path for **config.json**

- Windows: `%APPDATA%\Preferis\config.json`
- macOS: `~/Library/Application Support/Preferis/config.json`

### Default path for **local.json**

- Windows: `%LOCALAPPDATA%\Preferis\local.json`
- macOS: `~/Library/Application Support/Preferis/local.json`

## Backing up **config.json**

Preferis automatically keeps one backup of `config.json` once 24 hours have passed since it was last edited.
If you accidentally delete rules or other settings, you may be able to restore them from a backup from the previous day or earlier.
