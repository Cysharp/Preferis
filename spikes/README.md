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
| `SourceApp/` | Windows 10 | URL を開いたアプリの手がかり (親プロセスのチェーン、起動時の前面ウィンドウ) を記録する NativeAOT の WinExe。`measure.ps1` でシェルの起動経路を計測 |

Windows のスパイク 6〜9 (Design.md §12) は使い捨てのコードを作らず、M4 の製品コード (`artifacts/win-x64/Preferis/`) で
行いました。

## SourceApp (Windows)

```powershell
dotnet publish spikes/SourceApp -c Release
```

`artifacts/spikes/SourceApp/SourceApp.exe` ができます。ログは同じフォルダーの `source-app.log` に追記されます (URL は
スキームとホストだけ、パスはユーザープロファイルを `%USERPROFILE%` に置き換えて記録)。

- スクリプトで計測する: `pwsh spikes/SourceApp/measure.ps1`。独自スキーム `preferis-probe:` だけを HKCU に登録し、
  ShellExecuteEx、`cmd /c start`、`rundll32 url.dll`、`explorer.exe <url>`、WinRT の `Launcher` で開いてから登録を消します。
  既定のブラウザーは変えません。
- 実際のアプリでリンクをクリックして計測する: `SourceApp.exe --register-browser` で「Preferis Source Probe」をブラウザーとして
  登録すると、既定のアプリの設定が開くので既定のブラウザーに選びます。登録時の既定ブラウザーのコマンドを `forward.txt` に保存し、
  http(s) の URL はそこへ転送するため、リンクは普段どおり開きます。Slack、新しい Teams、新しい Outlook、クラシック Outlook、
  VS Code、Windows Terminal などでリンクをクリックし、`source-app.log` を確認します。
- 1 回だけ試す: `SourceApp.exe <url>` (親はシェル、前面はその時のウィンドウになる)。

NativeAOT のリンクが「'vswhere.exe' is not recognized」で失敗する場合 (Git Bash から起動した、`NoDefaultCurrentDirectoryInExePath`
がある) は、`vswhere.exe` のあるフォルダーを PATH に足します (Platform.md §2.1 のビルドの注意)。

```powershell
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"
```

`UrlActivation` のモードは `~/Library/Logs/PreferisSpike/mode` ファイルで切り替えます (`nsapp` 既定, `ae`, `pick`, `pick-assist`)。

結果は `docs/Platform.md` §5 を参照。

## 後始末

スパイクの `.app` は `CFBundleURLTypes` に http/https を宣言するため、`open` した時点で Launch Services に
http/https ハンドラー候補として登録されます。検証後は登録を解除してください。

```bash
/System/Library/Frameworks/CoreServices.framework/Versions/A/Frameworks/LaunchServices.framework/Versions/A/Support/lsregister -u artifacts/spikes/UrlActivation.app
```

`SourceApp` を既定のブラウザーにした場合は、先に設定の既定のアプリで元のブラウザー (Preferis など) を選び直してから登録を
解除します。解除は `SourceApp.exe --unregister` (`preferis-probe:` とブラウザー登録の両方を HKCU から消す)。
