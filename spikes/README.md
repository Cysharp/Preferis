# Spikes

`spikes/` 配下は Design.md §12 のスパイク (技術検証) 用の使い捨てコードです。

- 製品コードではないため、Guidelines の TDD ルールの対象外とします。検証が終わったら結果を
  `docs/Platform.md` §5 と `docs/Design.md` §12 に記録し、コード自体は参考として残します。
- メインソリューション (`Preferis.slnx`) には含めません。各スパイクは単独の `dotnet publish` で動かします。
- 成果物は `artifacts/spikes/` に出力します (`.gitignore` 対象)。

| ディレクトリ | 対象 (Design.md §12) | 内容 |
| --- | --- | --- |
| `UrlActivation/` | macOS 1 | objc ランタイム直接呼び出しで `application:openURLs:` を受け取る NativeAOT アプリ。起動から受信までの時間を計測。`run.sh` で実行 |
| `UrlActivation/baseline/` | macOS 1 | ランタイムを持たない C の最小アプリ。LS 経由起動の床を測る比較用 |
| `UrlActivation/ls-query.swift` | macOS 4 | LS が https / public.html ハンドラーとして認識しているアプリの一覧を表示 (`swift ls-query.swift`) |
| `PickerHelper/` | macOS 2, 5 | Microsoft.macOS + NativeAOT の AppKit ピッカー。stdin の JSON を読み stdout に応答。`UrlActivation` の mode `pick` / `pick-assist` から子プロセスとして起動される |

Windows のスパイク (Design.md §12 の 6〜9) は使い捨てのコードを作らず、M4 の製品コード (`artifacts/win-x64/Preferis/`) で
行いました。

`UrlActivation` のモードは `~/Library/Logs/PreferisSpike/mode` ファイルで切り替えます (`nsapp` 既定, `ae`, `pick`, `pick-assist`)。

結果は `docs/Platform.md` §5 を参照。

## 後始末

スパイクの `.app` は `CFBundleURLTypes` に http/https を宣言するため、`open` した時点で Launch Services に
http/https ハンドラー候補として登録されます。検証後は登録を解除してください。

```bash
/System/Library/Frameworks/CoreServices.framework/Versions/A/Frameworks/LaunchServices.framework/Versions/A/Support/lsregister -u artifacts/spikes/UrlActivation.app
```
