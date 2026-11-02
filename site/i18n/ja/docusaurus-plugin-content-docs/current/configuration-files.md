---
title: 設定ファイル
sidebar_position: 4
---

Preferis の設定ファイルは **config.json** と **local.json** の 2 つがあります。

- **config.json**: Preferis のプロファイルやルールといった基本的な設定を保存します。このファイルをクラウドストレージに置いて、各デバイスから参照できます
- **local.json**: 各デバイス固有の設定を保存します

:::note
JSON ファイルを直接操作する場合には Preferis が動作中でないことを確認して編集してください。意図せず設定が失われる可能性があります。
:::

## 設定ファイルのパス
### **config.json** のデフォルトパス

- Windows: `%APPDATA%\Cysharp\Preferis\config.json`
- macOS: `~/Library/Application Support/Preferis/config.json`

### **local.json** のデフォルトパス

- Windows: `%LOCALAPPDATA%\Cysharp\Preferis\local.json`
- macOS: `~/Library/Application Support/Preferis/local.json`

## **config.json** 設定ファイルのバックアップ

config.json ファイルは最後に編集されてから24時間が経過すると自動的に1世代バックアップされます。
誤ってルールなどを削除してしまった場合でも前日以前のデータから復元できる場合があります。
