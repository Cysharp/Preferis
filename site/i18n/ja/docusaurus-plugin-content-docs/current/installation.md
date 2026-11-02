---
title: インストール
sidebar_position: 2
---

Preferis は以下の手順でインストールできます。

## Windows へのインストール

1. GitHub の [リリースページ](https://github.com/cysharp/Preferis/releases) から最新のインストーラー (`Preferis-win-x64-Setup.exe` または `Preferis-win-arm64-Setup.exe`) をダウンロードします
    - x64 と Arm 向けのインストーラーが用意されているので利用している環境に合わせてダウンロードしてください
2. ダウンロードしたインストーラーを実行し、画面の指示に従ってインストールを完了させます
    - Preferis は現在のユーザー向けに `%LOCALAPPDATA%\Preferis` へインストールされます。管理者権限は必要ありません
    - インストールが完了すると設定画面が開き、スタートメニューに Preferis が追加されます

:::caution
現在 Windows 版のインストーラーと実行ファイルはコード署名されていないため、インストーラーの実行時に Windows SmartScreen の警告が表示されることがあります。「詳細情報」から「実行」を選択して続行してください。これは将来的に解消される可能性があります。
:::

### ポータブル版

インストールせずに使う場合は、代わりに `Preferis-<バージョン>-win-x64.zip` (または `win-arm64`) をダウンロードし、中に入っている `Preferis` フォルダーを任意の場所に展開します。フォルダーに含まれている `Preferis.exe` を実行すると設定画面が開きます。

## macOS へのインストール

1. GitHub の [リリースページ](https://github.com/cysharp/Preferis/releases) から最新の .dmg ファイルをダウンロードします
2. ダウンロードしたディスクイメージから Preferis をアプリケーションフォルダにドラッグアンドドロップします
3. ターミナルで `xattr -cr /Applications/Preferis.app` を実行して、ダウンロード時のセキュリティ属性を削除します

:::caution
現在 macOS 版の Preferis は署名や Apple による公証 (Notarization) が行われていないため、ダウンロード後に上記の手順を行う必要があります。これは将来的に解消される可能性があります。
:::

## アップデート

### Windows (インストーラー版)
Preferis の設定画面から更新できます。設定画面のサイドバーで「一般」を選択し、「更新」のセクションから更新できます。

![](/img/docs/installation-01.png)

### Windows (ポータブル版) と macOS
ポータブル版は自動では更新されません。更新するにはリリースページから新しいバージョンをダウンロードし、以前のファイルと置き換えます。

設定はアプリケーション外に保存されているため、そのまま保持されます。詳しくは[設定ファイル](configuration-files.md) を参照してください。

:::caution
macOS では新しい Preferis をアプリケーションフォルダにドラッグして置き換えたのち、`xattr` の手順をもう一度行う必要があります。
:::
