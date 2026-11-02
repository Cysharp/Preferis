---
title: URL を開いたアプリによって振り分ける
---

Preferis に URL を渡してきたアプリケーションによって振り分ける条件を設定できます。例えば同じ URL であっても Slack から開かれた場合と Teams から開かれた場合で異なるプロファイルを使用するといった設定が可能です。

## 設定
アプリケーションの ID またはアプリケーションのパスを条件として使用できます。

値はホストや URL と同様に「が次と等しい」「が次で始まる」「が次で終わる」「が次を含む」「が次のパターンに一致」「が次の正規表現に一致」といった比較が可能です。

![](/img/docs/settings-rule-condition-01.png)

### App ID: アプリケーションの ID
Windows の場合はパッケージ化されたアプリの Application User Model ID を指定できます。例えば Windows Terminal は `Microsoft.WindowsTerminal_8wekyb3d8bbwe!App` です。通常の Windows デスクトップアプリケーションでは次の App path を使用してください。

macOS の場合はバンドル ID (Bundle Identifier) を指定できます。例えば Slack は `com.tinyspeck.slackmacgap` です。


### App path: アプリケーションのパス
アプリケーションの実行ファイルのパスを指定できます。例えば Windows では `C:\Program Files\Microsoft VS Code\Code.exe` のような値で、macOS では `/Applications/Slack.app` のようになります。

:::note
Windows と macOS で「アプリのID」や「アプリのパス」は同じ設定項目を利用しますが、それぞれ指定する ID やパスの書式が異なり、被ることはないため OS ごとの固有の項目を持ちません。
:::
