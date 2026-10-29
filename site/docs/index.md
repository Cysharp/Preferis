---
slug: /
sidebar_position: 1
title: Preferis
description: Open links in the right browser or browser profile on Windows and macOS.
---

Preferis routes links to a browser, a browser profile, or another application according to your rules. Register Preferis as your default browser to choose where each HTTP or HTTPS link opens.

## How it works

1. Define **profiles** for the browsers, browser profiles, or commands you want to use.
2. Add **rules** that match URLs and select an action. Rules run from top to bottom; the first matching rule wins.
3. When a rule offers several profiles, use the native **picker** to choose where to open the link. A catch-all rule handles URLs that do not match another rule.

Preferis supports Windows and macOS. Its routing engine is separate from the settings window and picker to keep link handling fast.

## Configuration

Preferis keeps shared settings in `config.json` and machine-specific settings in `local.json`. You can place `config.json` in cloud storage and point each machine's `local.json` to it.

The repository contains detailed specifications in Japanese:

- [Configuration reference](https://github.com/Cysharp/Preferis/blob/main/docs/Configuration.ja.md): profiles, routing rules, and settings files.
- [Architecture and design](https://github.com/Cysharp/Preferis/blob/main/docs/Design.ja.md): components and design decisions.
- [Platform integration](https://github.com/Cysharp/Preferis/blob/main/docs/Platform.ja.md): browser registration, detection, and launching on each OS.

## Source code

Preferis is available under the [MIT license](https://github.com/Cysharp/Preferis/blob/main/LICENSE). See the [GitHub repository](https://github.com/Cysharp/Preferis) for source code and development updates.
