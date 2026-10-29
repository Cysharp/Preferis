---
slug: /
sidebar_position: 1
title: Preferis
description: Windows と macOS で、リンクを適切なブラウザーやブラウザープロファイルで開きます。
---

Preferis はルールに従って、リンクをブラウザー、ブラウザープロファイル、または任意のアプリケーションに振り分けます。既定のブラウザーとして登録することで、HTTP・HTTPS のリンクごとに開く先を選べます。

## 仕組み

1. 利用するブラウザー、ブラウザープロファイル、コマンドを **プロファイル** として定義します。
2. URL の条件とアクションを組み合わせた **ルール** を追加します。ルールは上から順に評価され、最初に一致したルールが適用されます。
3. 候補が複数ある場合は、OS ネイティブの **ピッカー** で開く先を選択します。どのルールにも一致しない URL は、末尾のキャッチオールルールで処理します。

Preferis は Windows と macOS に対応しています。リンクをすばやく処理するために、ルーティングエンジンを設定画面やピッカーから分離しています。

## 設定

共有する設定は `config.json`、マシン固有の設定は `local.json` に保存します。`config.json` をクラウドストレージに置き、各マシンの `local.json` から参照できます。

詳細な仕様は、リポジトリ内のドキュメントを参照してください。

- [設定ファイル仕様](https://github.com/Cysharp/Preferis/blob/main/docs/Configuration.ja.md): プロファイル、ルーティングルール、設定ファイル。
- [設計ドキュメント](https://github.com/Cysharp/Preferis/blob/main/docs/Design.ja.md): コンポーネント構成と設計判断。
- [プラットフォーム仕様](https://github.com/Cysharp/Preferis/blob/main/docs/Platform.ja.md): OS ごとのブラウザー登録、検出、起動方法。

## ソースコード

Preferis は [MIT ライセンス](https://github.com/Cysharp/Preferis/blob/main/LICENSE)で公開されています。ソースコードと開発状況は [GitHub リポジトリ](https://github.com/Cysharp/Preferis)で確認できます。
