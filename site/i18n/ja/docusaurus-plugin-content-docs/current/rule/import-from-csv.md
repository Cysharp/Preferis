---
title: CSV からインポート
---

ルールが多くまとめて登録したい場合は CSV 形式で一括インポートできます。ルールの追加ボタンから「CSVからインポート」を選択し、CSV ファイルを選択します。

![](/img/docs/settings-import-from-csv.png)

CSV の形式は1カラム目がパターン、2カラム目以降がプロファイル名です。以下は Preferis がインポートできる CSV のサンプルです。

```csv
パターン,プロファイル名
https://www.example.com,Personal
https://*.slack.com,Work
*.microsoftonline.com,Work
https://github.com/dotnet/*,Work
https://github.com/mayuki/*,Personal
localhost:3000,Work
/^https?://docs\.example\.com/,Personal,Work
www.example.com,Personal,Work
```

|パターン|プロファイル名|プロファイル名2...|
|-|-|-|
|https://www.example.com|Personal||
|https://*.slack.com|Work||
|*.microsoftonline.com|Work||
|https://github.com/dotnet/*|Work||
|https://github.com/mayuki/*|Personal||
|localhost:3000|Work||
|/^https?://docs\.example\.com/|Personal|Work|
|www.example.com|Personal|Work|

パターンは http または https で始まる場合には URL として、それ以外はホスト名 (ドメイン名) にマッチするパターンとして登録されます。このパターンはデフォルトで「ワイルドカードマッチ」です。

プロファイル名を複数指定すると、ルールにマッチした際に使用されるプロファイルが複数選択されている状態となります。