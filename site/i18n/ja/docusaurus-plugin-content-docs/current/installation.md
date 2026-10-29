---
title: インストール
sidebar_position: 2
---

Preferis は以下の手順でインストールできます。

## Windows へのインストール

1. GitHub の [リリースページ](https://github.com/cysharp/Preferis/releases) から最新のインストーラーをダウンロードします
    - x64 と Arm 向けのインストーラーが用意されているので利用している環境に合わせてダウンロードしてください
2. ダウンロードしたインストーラーを実行し、画面の指示に従ってインストールを完了させます

## macOS へのインストール

1. GitHub の [リリースページ](https://github.com/cysharp/Preferis/releases) から最新の .dmg ファイルをダウンロードします
2. ダウンロードしたディスクイメージから Preferis をアプリケーションフォルダにドラッグします
3. ターミナルで `xattr -cr /Applications/Preferis.app` を実行して、ダウンロード時のセキュリティ属性を削除します

:::caution
現在 macOS 版の Preferis は署名や Nortarization が行われていないため、ダウンロード後に上記の手順を行う必要があります。これは将来的に解消される可能性があります。
:::