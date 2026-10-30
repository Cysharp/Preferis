# OS 固有事項

Windows / macOS それぞれで必要な登録、検出、起動、実行ファイル配置の具体的な方法をまとめます。
本書の内容は Platform 層 (`Preferis.Platform`) と各実行ファイルの実装仕様であり、実機確認で
判明した差異は随時この文書に反映します。

## 1. Platform 層のインターフェース

抽象は `Preferis.Core.Hosting` (と `Preferis.Core.Browsers`) にあり、Platform 層はその実装だけを持ちます。
Router のオーケストレーション (`RoutingService`) もフェイクでテストできるよう Core にあります。

```
Preferis.Core.Configuration.ConfigPaths / ConfigLoader (IConfigStore)
    local.json / 既定 config.json / ログの場所。読み込みは決して例外を投げず、壊れていれば (値が必要な場所の null を
    含む。Configuration.md §4.1) 既定 + エラー文字列
    Lock() で config.json を読んで書き戻す間、このマシンのロック (config.lock) を持つ
    Save() は保存前の内容を config.old.json に残す。無い場合か前回の作成から 24 時間以上経過した場合に作成
    (Configuration.md §1.2)。必要なバックアップに失敗したら設定を上書きしない。SaveLocal() は対象外
Preferis.Core.Browsers.IBrowserLocator        TryResolve(browserId) -> DetectedBrowser?   高速経路用 (1 件だけ)
Preferis.Core.Hosting.IBrowserDetector        : IBrowserLocator + DetectBrowsers()         設定 UI とフォールバック用
Preferis.Core.Browsers.IBrowserProfileEnumerator  Enumerate(browser) -> list | null (読めない)
Preferis.Core.Hosting.IBrowserLauncher        Launch(LaunchPlan)
Preferis.Core.Hosting.ISystemOpener           IsSelfDefaultHandler(scheme) / OpenWithSystemDefault(url)
Preferis.Core.Hosting.IPickerLauncher         Show(PickerRequest) -> PickerResponse?       子プロセス (Design.md §9.3)
Preferis.Core.Hosting.IUserNotifier           ShowError(title, message, url)               最終フォールバック
Preferis.Core.Hosting.IDefaultBrowserRegistrar GetStatus / Register / Unregister / OpenSystemSettings
Preferis.Core.Hosting.IRoutingLog             Write(level, message)
```

| 実装 (Preferis.Platform) | OS | 備考 |
| --- | --- | --- |
| `Browsers.BrowserProfileEnumerator` | 共通 | `ChromiumLocalStateReader` / `FirefoxProfilesIniReader` を使う。読めなければ null |
| `Launching.LaunchPlanExecutor` | 共通 | `ProcessStartInfo.ArgumentList` に要素単位で渡す。macOS の `OpenWithApplication` は `open -a` |
| `Launching.ProcessPickerLauncher` | 共通 | stdin/stdout JSON、タイムアウト、stderr をログへ転送。実行ファイルが無ければ null |
| `MacOS.MacBrowserDetector` | macOS | `NSWorkspace` の bundle id 解決と https ハンドラー列挙。Preferis 自身とヘルパー (`net.mayuki.Preferis` とその配下の bundle id) は、どのプロセスから呼んでも除外 |
| `MacOS.MacSystemOpener` | macOS | 既定ハンドラーの bundle id を自分と比較。委譲は `open <url>` |
| `MacOS.MacDefaultBrowserRegistrar` | macOS | `LSRegisterURL` + `setDefaultApplicationAtURL:toOpenURLsWithScheme:completionHandler:` (ブロック) 。失敗時は `LSSetDefaultHandlerForURLScheme`。対象の `Preferis.app` をパスで受け取る (Router は `ForMainBundle()`、設定 UI は自分を含む `Preferis.app`) |
| `MacOS.MacApplicationIcons` | macOS | `iconForFile:` → `CGImageForProposedRect:` → PNG。UI 非依存の設定 ViewModel がアイコンを PNG で持つため |
| `MacOS.MacUrlActivationSource` | macOS | NSApplication + `application:openURLs:` デリゲート (スパイク 1 の製品版) |
| `MacOS.MacUserNotifier` | macOS | 暫定で `osascript` の `display dialog` (「Copy URL」ボタンつき)。後で NSAlert に置換可 |
| `MacOS.ObjC` | macOS | objc ランタイム / Foundation / AppKit / Launch Services への最小限の P/Invoke。グローバルブロック生成を含む |
| `Windows.WindowsBrowserDetector` | Windows | `StartMenuInternet` (HKCU → HKLM → WOW6432Node) とカタログの既知パス。Preferis 自身と Internet Explorer は除外 (§2.3) |
| `Windows.WindowsSystemOpener` | Windows | 既定ハンドラーの ProgId (`AssocQueryString`) が `PreferisURL` かを見る。委譲は `ShellExecute` |
| `Windows.WindowsDefaultBrowserRegistrar` | Windows | HKCU への登録と解除、`ms-settings:defaultapps` を開く (§2.2) |
| `Windows.WindowsUserNotifier` | Windows | `MessageBoxW`。「はい」で URL をクリップボードにコピー |
| `Windows.WindowsApplicationIcons` | Windows | `SHDefExtractIcon` → `GetDIBits` → PNG (自前の `PngEncoder`)。System.Drawing を使わない。`LoadApplicationIcon` は自分の exe のアイコン (リソース 32512) を `LoadImage` で読む (§2.7) |
| `Windows.WindowsProcess` | Windows | Router のコンソール接続、標準ハンドルの継承止め、`AllowSetForegroundWindow` |
| `Windows.CommandLine` | Windows | `--single-argument` の後ろを生のコマンドラインから取り出す (§2.2) |
| `Windows.IRegistry` / `IUrlAssociations` | Windows | レジストリと関連付けの薄い抽象。テストはフェイクで検出と登録を検証する |

Router 実行ファイル (`Preferis.Router`) は `RouterHost` でこれらを配線し、`FileLog` でログを書きます。
設定 UI は `Preferis.Settings.Services.ISettingsPlatform` を通して同じ実装を使います (macOS は `MacSettingsPlatform`、§3.8、
Windows は `WindowsSettingsPlatform`、§2.8)。

設定ファイルのロック (Configuration.md §1.1) は OS ごとの実装を持たず、Core の `ConfigLoader.Lock` が `config.lock` を
`FileShare.None` の `FileStream` で開いて取ります。排他の仕組みは .NET が OS ごとに選びます。

- Windows: 共有モードなしで開いたファイルは、閉じるまで他のハンドルから開けない (共有違反。同じプロセスでも同じ)。
- macOS: .NET が開いた直後に `flock(LOCK_EX | LOCK_NB)` を掛け、掛からなければ `EWOULDBLOCK` の `IOException` になる。
  アドバイザリロックなので Preferis 以外がファイルを開くことは止めないが、ロックを取るのは Preferis だけなので足りる。
  `flock` は開いたファイルごとのロックなので、同じプロセスの別の `FileStream` とも排他になる (Router は 1 つの Apple Event
  の URL を並行に処理するため、これが要る)。.NET の `DOTNET_SYSTEM_IO_DISABLEFILELOCKING` を設定した環境や、`flock` に
  対応しないファイルシステムでは .NET がロックを掛けずに開くため、排他にならない (`~/Library` は APFS なので通常は問題ない)。

どちらもプロセスの終了で解放されます。取れない間は 20 ms ごとに開き直します。テストは同じプロセスの 2 つの `ConfigLoader`
で排他を確かめており、Windows では実行済み、macOS では未実行 (2026-10-03)。

## 2. Windows

### 2.1 実行ファイル配置

```
<install dir>/
  Preferis.exe              Router。NativeAOT 単一 exe (4.9 MB)。依存 DLL なし
  Preferis.Picker.exe       WinUI 3。NativeAOT (7.6 MB)。リソースは Preferis.Picker.pri
  Preferis.Settings.exe     WinUI 3。NativeAOT (10 MB)。リソースは Preferis.Settings.pri
  Microsoft.ui.xaml.dll ほか Windows App SDK ランタイム (自己完結モード。2 つの WinUI プログラムが共有)
  <言語>/                   WinUI 自身の文字列 (*.mui)。90 言語ほど
```

全体で 82 MB、239 ファイル (M4)。Router は同じフォルダーの DLL を一切インポートしないため、Windows App SDK の DLL が
隣にあっても起動コストに影響しません。WinUI の 2 つはそれぞれ自己完結で publish し、同じ版の Windows App SDK の
ファイルが同じ名前で重なるので 1 フォルダーにまとめられます。シンボル (.pdb) は `artifacts/win-x64/publish/` に残します。

このフォルダー一式は `build/Preferis.Build.proj` の `PublishWindows` ターゲットで `artifacts/win-x64/Preferis/`
に生成します (Design.md §4.4)。ローカルテストではそのフォルダーの `Preferis.exe --register` を実行し、
再ビルド後も同じパスに出力されることで登録を維持します。

配布 (Design.md §4.4、`PackageWindows` ターゲット): ポータブル版はこのフォルダーを `Preferis/` ごと zip にしたもので、
展開した場所の `Preferis.exe --register` (または設定 UI) で登録します。インストーラーは Velopack
(`Preferis-win-x64-Setup.exe` / `Preferis-win-arm64-Setup.exe`) で、管理者権限なしに `%LocalAppData%\Preferis\` に入れます。

```
%LocalAppData%\Preferis\
  Preferis.exe              Velopack のスタブ。current\Preferis.exe を起動する (スタートメニューのショートカットの先)
  Update.exe                Velopack の更新ツール
  current\                  上記のフォルダー一式。更新では中身だけが置き換わり、パスは変わらない
```

- このフォルダーはインストーラーだけのもの: Setup は「空でないフォルダー = インストール済み」としか判定せず (マニフェストの有無は
  見ない)、Repair と更新とアンインストールはフォルダーごと置き換える・消す。Preferis 自身のファイルは `%LocalAppData%\Cysharp\Preferis`
  (`local.json`、ログ) と `%AppData%\Cysharp\Preferis` (`config.json`) にあり (Configuration.md §1)、重ならない。2026-10-30 より前は
  `%LocalAppData%\Preferis` に `local.json` を置いていたため、ポータブル版や開発ビルドを使った PC では Setup が「Preferis is already
  installed」と誤検出した。その時代のファイルは起動時に新しいフォルダーへ移す (`ConfigPaths.MoveFromLegacyWindowsFolders`)。
- 登録 (§2.2) は Router が自分の実際のパス (`current\Preferis.exe`) で行うため、更新後も有効なまま。
- アーキテクチャごとに Velopack のチャネル (`win-x64` / `win-arm64`) を分け、x64 のインストールが arm64 の更新を拾わないようにする。
  `--runtime` でアーキテクチャを宣言するので Setup はそのアーキテクチャのネイティブ実行ファイルになり、合わないマシンでは
  インストールを断る。
- Velopack はインストール・更新・アンインストールの時に `Preferis.exe --veloapp-install <version>` のようなフックの引数で
  Router を起動し、30 秒以内の終了を期待する。3 つの実行ファイルは `Main` の先頭で `VelopackInstallation.Start`
  (`Preferis.Platform.Windows`) を呼ぶ。これは親フォルダーに `Update.exe` がある時 (Velopack でインストールされている時)
  だけ `VelopackApp.Build().Run()` を実行し、フックの引数ならそこで終了する。ポータブル zip や `artifacts/` のローカルビルドでは
  何もしない: Velopack の `Run()` はインストールされていなくてもインストール先を探し `%LocalAppData%\velopack\` にログを
  書くので、その分をリンクのクリックに乗せない。インストール配置では `Run()` (マニフェストの読み込み、`packages\` の確認、
  ログ) が Router の起動 1 回あたり約 2 ms (`Preferis --version` の中央値 11.7 → 13.9 ms、2026-10-29)。Velopack のライブラリ
  (MIT、AOT 互換) は `Preferis.Platform` が Windows でビルドする時だけ参照し (`VELOPACK` シンボル)、macOS のバンドルには
  入らない。
- ダウンロード済みで未適用の更新があれば `Run()` はそれを適用して Preferis を再起動する (`SetAutoApplyOnStartup`)。これを
  許すのは設定 UI だけで、Router とピッカーは許さない (リンクを開くのを更新で待たせない。§5.2)。
- アプリ内からの更新チェックは設定 UI の一般ページの「更新」カード (§2.8): `UpdateManager` + `GithubSource`
  (`https://github.com/Cysharp/Preferis`。チャネルは指定せず、インストールのマニフェストにあるビルド時の `win-x64` / `win-arm64`
  を使う) で、最新の公開リリースの `releases.<channel>.json` を読み (`CheckForUpdatesAsync`)、「ダウンロード」
  (`DownloadUpdatesAsync`、進捗バー)、「再起動して更新」(保存待ちの編集を書いてから `ApplyUpdatesAndRestart`。`Update.exe` が
  `current\` を置き換えて `Preferis.exe` を引数なしで起動するので、設定 UI が開き直す) の順。カードは Velopack でインストール
  されている時だけ出す (`ISettingsPlatform.Updater` が null なら出さない: ポータブル zip、ローカルビルド、macOS)。
- リポジトリが非公開の間は GitHub の API がトークン無しの読み取りを拒むため、更新の確認は「404 (Not Found)」で失敗する。公開
  すれば動く (認証なしの制限は IP あたり 60 回/時)。
- Velopack のライブラリのログは `%LocalAppData%\velopack\velopack_Preferis.log`。
- インストールせずにインストール配置を試すには、`artifacts/win-x64/Preferis/` を任意の `current\` にコピーし、
  `Preferis-<version>-win-x64-full.nupkg` の `lib/app/sq.version` (マニフェスト) を `current\` に置き、親フォルダーに
  `Update.exe` という名前のファイル (中身は何でもよい。更新を適用しない限り実行されない) を置く。`current\Preferis.exe
  --veloapp-install 0.0.1` が何も出さずに 0 で終了し、`current\Preferis.Settings.exe` に「更新」カードが出る
  (`PREFERIS_SETTINGS_SNAPSHOT` の `general-end.png`、§2.8)。
- インストールの最後に `Preferis.exe` が引数なしで起動され (設定 UI が開く)、ショートカットはスタートメニューにだけ作る。
- Setup.exe と実行ファイルはコード署名していない (SmartScreen の警告)。署名は `VelopackPackArgs` で `vpk pack` に渡す。

ビルドの注意:

- NativeAOT のリンクに Visual Studio の C++ ツール (「C++ によるデスクトップ開発」) が要る。ILCompiler は
  `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe` でそれを探すが、Git Bash から `dotnet` を起動すると
  括弧を含む環境変数 `ProgramFiles(x86)` が子に渡らず、「'vswhere.exe' is not recognized」でリンクが失敗する。PowerShell か
  コマンドプロンプトから実行する。
- WinUI のプロジェクトには `EnableMsixTooling=true` が要る (非パッケージでも)。無いと publish に `<名前>.pri` が入らず、
  コントロールが Fluent のスタイルを失う (microsoft/WindowsAppSDK#3451)。
- .NET 11 RC2 の NativeAOT publish は参照解決より先に XAML をコンパイルして WMC1007 になるため、WinUI の 2 プロジェクトで
  一時的に `UseOptimizedPublish=false` を設定する。[dotnet/sdk#56649](https://github.com/dotnet/sdk/pull/56649) を含む SDK に
  ビルド環境が移行したら解除する。
- WinRT の AOT 互換性チェック (CsWinRT1032) に対応するため、`IEnumerable<T>` や `IReadOnlyList<T>` の引数へ直接渡す
  コレクション式は、`new[] { ... }` で具体的な配列型を明示する。
- `Microsoft.WindowsAppSDK.WinUI` 2.3.9 は存在しない `InteractiveExperiences` 2.1.8 を指すため、2.1.9 を直接参照して NU1603 を避ける。

### 2.2 既定ブラウザーとして登録する

管理者権限を要求しないため、すべて `HKEY_CURRENT_USER` 配下に書きます。

```
HKCU\Software\Clients\StartMenuInternet\Preferis
  (Default)                              = "Preferis"
  DefaultIcon\(Default)                  = "<Preferis.exe>,0"
  shell\open\command\(Default)           = "<Preferis.exe>"      ; 引数なし → 設定 UI を起動
  Capabilities
    ApplicationName                      = "Preferis"
    ApplicationDescription               = "..."
    ApplicationIcon                      = "<Preferis.exe>,0"
    StartMenu\StartMenuInternet          = "Preferis"
    URLAssociations\http                 = "PreferisURL"
    URLAssociations\https                = "PreferisURL"
    FileAssociations\.htm / .html / .xhtml (任意、既定ではオフ) = "PreferisHTML"

HKCU\Software\Classes\PreferisURL
  (Default)                              = "Preferis URL"
  URL Protocol                           = ""
  DefaultIcon\(Default)                  = "<Preferis.exe>,0"
  shell\open\command\(Default)           = "\"<Preferis.exe>\" --single-argument %1"

HKCU\Software\RegisteredApplications
  Preferis                               = "Software\Clients\StartMenuInternet\Preferis\Capabilities"
```

- URL の受け渡しは `--single-argument %1` (Design.md §5.4)。手元の Edge も `"msedge.exe" --single-argument %1` で登録している。
  Router はこの形で起動されると、生のコマンドラインのマーカーより後ろ全体を 1 つの URL にする (`CommandLine.SingleArgument`)。
- 既定ブラウザーの切り替えは Windows 10 以降プログラムから行えない (UserChoice ハッシュ) ため、
  登録後に `ms-settings:defaultapps?registeredAppUser=Preferis` を開いてユーザーに選んでもらう (Windows 11 では
  アプリのページが直接開く)。既に Preferis が既定なら開かない。登録後は `SHChangeNotify(SHCNE_ASSOCCHANGED)` で
  エクスプローラーと設定アプリに読み直させる。
- 自分が既定かどうかは、http と https の ProgId が `PreferisURL` かで判定する (`system` アクションのループ検出にも使う)。
  ProgId はレジストリの `UserChoice` を直接読まず、シェルの `AssocQueryString(ASSOCF_IS_PROTOCOL, ASSOCSTR_PROGID)` に
  尋ねる: 新しい Windows では選択の保存場所が変わっている (`UserChoiceLatest`) ことがあり、シェルは有効な選択を知っている。
  手元では `MSEdgeHTM` が返った。現在の既定の表示名は `ASSOCSTR_FRIENDLYAPPNAME` (「Microsoft Edge」)。
- 実行ファイルの移動を検出するため、`PreferisURL` の `shell\open\command` のパスが今の `Preferis.exe` と違えば
  「登録されていない」とみなす (既定であっても)。設定 UI はその場合「既定にする」を出し、押すと今のパスで登録し直す。
  登録処理自体は `Preferis.exe --register` が行う (設定 UI もそれを実行する)。
- 解除 (`--unregister`) は上記のキー 2 つと `RegisteredApplications` の値を削除する。

### 2.3 ブラウザー検出

一次情報源はレジストリの `StartMenuInternet` です。HKLM と HKCU の両方を読み、同名キーは HKCU を優先します。

```
HKLM\Software\Clients\StartMenuInternet\<Key>\shell\open\command   ; 実行ファイルパス
HKLM\Software\Clients\StartMenuInternet\<Key>\DefaultIcon           ; アイコン
HKLM\Software\Clients\StartMenuInternet\<Key>\Capabilities\ApplicationName
HKLM\Software\WOW6432Node\Clients\StartMenuInternet\...            ; 32bit 登録 (念のため)
HKCU\Software\Clients\StartMenuInternet\...
```

- まずカタログ (Configuration.md §2.4) のキー名で既知ブラウザーに対応づける。Edge / Chrome の各チャネルは
  実行ファイル名が同じ (`msedge.exe`, `chrome.exe`) なので、キー名 (`Microsoft Edge Dev` 等) で区別する。
  マシン全体のインストールと並べて個人用にインストールした Chrome は `Google Chrome.<接尾辞>` という名前で登録するため、
  キー名の後ろの `.<何か>` は無視する。
- キー名で判定できない場合は実行ファイル名とインストール先のフォルダーで判定する。同名の実行ファイルのうち、
  カタログのユーザーデータの場所から求めたベンダーのフォルダー (`Microsoft\Edge Beta`、`Google\Chrome SxS` など) が
  パスに含まれるものを選び、どれも含まれなければ最初のもの (Stable) にする。`launcher.exe` (Opera) は他のプログラムと
  名前が重なるため、名前だけでは対応づけない。Firefox のキーは `FIREFOX.EXE` やインストールごとのハッシュ付き
  (`Firefox-<hash>`, `Firefox Developer Edition-<hash>`) になるため、キー名の前方一致で判定する。
- `shell\open\command` の値は引用符で囲まれていない場合もある (`C:\Program Files\Internet Explorer\iexplore.exe`)。
  引用符があればその中、無ければ `.exe` までを実行ファイルとし、環境変数を展開する。実行ファイルが存在しないキーは無視する。
- Chrome Canary は既定ブラウザーに設定できず `StartMenuInternet` に登録されないため、カタログの既知パス
  (`%LOCALAPPDATA%\Google\Chrome SxS\Application\chrome.exe`) の存在で検出する。
- Preferis 自身 (キー `Preferis`、または実行ファイル名 `Preferis.exe`) は除外する。Windows 11 にも残っている
  `IEXPLORE.EXE` も除外する (起動すると Edge が開くだけ)。
- カタログ外は `win:<Key>` として扱い、プロファイル列挙は行わない。表示名は `Capabilities\ApplicationName`。
  それが `@<file>,-<id>` のリソース参照ならキーの既定値かキー名を使う。
- `local.json` の `browserOverrides` (実行ファイルのパス) は検出より優先する。キーが無いポータブル版も検出結果に加わる。
- 高速経路の `TryResolve(browserId)` は `StartMenuInternet` のキー名の一覧からカタログのキー名に合うものだけを読む
  (手元で 0.1 ms)。見つからなければ既知パス、それでも無ければ (キー名の違うカスタムインストールのため) 全キーを読む。
- 手元 (Windows 11, 2026-10-02) の HKLM には `Microsoft Edge`、`Microsoft Edge Dev`、`IEXPLORE.EXE` があり、
  `edge` と `edge-dev` が検出された (全体の検出 25 ms、うち初回の JIT を含む)。

### 2.4 ブラウザープロファイル列挙

| 方式 | 場所 | 読み取り |
| --- | --- | --- |
| chromium (Edge) | `%LOCALAPPDATA%\Microsoft\Edge\User Data\Local State` | JSON `profile.info_cache` |
| chromium (Edge Beta / Dev / Canary) | `%LOCALAPPDATA%\Microsoft\Edge Beta\User Data`、`Edge Dev`、`Edge SxS` (Canary) 配下の `Local State` | 同上 |
| chromium (Chrome) | `%LOCALAPPDATA%\Google\Chrome\User Data\Local State` | 同上 |
| chromium (Chrome Beta / Dev / Canary) | `%LOCALAPPDATA%\Google\Chrome Beta\User Data`、`Chrome Dev`、`Chrome SxS` (Canary) 配下の `Local State` | 同上 |
| chromium (Brave) | `%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\Local State` | 同上 |
| chromium (Vivaldi) | `%LOCALAPPDATA%\Vivaldi\User Data\Local State` | 同上 |
| firefox | `%APPDATA%\Mozilla\Firefox\profiles.ini` | INI |

チャネルごとにユーザーデータディレクトリが別であり、プロファイルは共有されません。カタログエントリが
ディレクトリ名を持ちます。Firefox は Release / Developer Edition / Nightly が `profiles.ini` を共有し、
`installs.ini` がインストールごとの既定プロファイルを指します。プロファイル名は全チャネルで一意です。

Chromium の `Local State` は数百 KB になることがある (手元の Chrome で 143 KB) ため、
`Utf8JsonReader` で `profile.info_cache` のみをストリーミング読み取りし、全体をデシリアライズしない。
各エントリから取り出す項目:

| キー | 用途 |
| --- | --- |
| (エントリ名) | `directory` (`Default`, `Profile 1` ...) |
| `name` | 表示名。`gaia_name` があればそれも保持 |
| `user_name` | `email` 照合用 |
| `avatar_icon`, `gaia_picture_file_name`, `profile_color_seed` | ピッカーのアバター表示 (設定 UI / ピッカーのみ) |
| `profile.profiles_order` (親) | 並び順 |

Edge は上記に加えて `edge_account_*` を持つが、`name` / `user_name` は共通なのでそれだけを使う。手元の Edge では
`gaia_picture_file_name` が `Edge Profile Picture.png` で、ピッカーと設定 UI のアバターに使えた。

### 2.5 起動

| 対象 | 方法 |
| --- | --- |
| ブラウザー (プロファイル指定なし) | `CreateProcess` (`Process.Start`, `UseShellExecute=false`) で `"<exe>" <url>` |
| chromium + プロファイル | `--profile-directory=<dir>` を引数に追加 |
| chromium + private | Edge: `--inprivate`、Chrome/Brave/Vivaldi: `--incognito`、Opera: `--private` |
| firefox + プロファイル | `-P <name> <url>` (起動中の別プロファイルとの共存は要実機確認) |
| firefox + private | `-private-window <url>` |
| `command` | `path` を環境変数展開し、`args` の `{url}` を置換して `ArgumentList` に積む |
| `system` | `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })` |

引数は常に `ArgumentList` に要素単位で渡し、文字列結合によるクォート問題を避けます。
起動後は待たずに終了します。

`Process.Start` (`UseShellExecute=false`) は継承可能なハンドルをすべて子に渡す。設定 UI が出力を読むために Router の
標準出力をパイプにしていると、Router が起動したブラウザーがそのパイプを持ち続け、設定 UI は読み終わりを待ってブラウザーの
終了まで止まった (実測 8 秒、起動したプログラムが終わるまで)。Router は起動時に自分の標準ハンドルの継承フラグを外す
(`WindowsProcess.KeepStandardHandlesFromChildren`)。ピッカーの標準入出力は .NET が別のパイプで渡すので影響しない。

### 2.6 ピッカー (`Preferis.Picker.exe`, WinUI 3)

- プロジェクトは `src/Preferis.Picker.Windows` (実行ファイル名 `Preferis.Picker`)。TFM は `net10.0-windows10.0.19041.0`
  (最低 10.0.17763)。Windows App SDK はメタパッケージではなく WinUI コンポーネント `Microsoft.WindowsAppSDK.WinUI` 2.3.9 を
  参照する (メタパッケージは AI / ML / Search のランタイムまで出力に入れる)。CsWinRT は .NET SDK の Windows SDK
  プロジェクションに含まれるものを使う。`PublishAot=true`、`OutputType=WinExe`。
- 非パッケージ (`WindowsPackageType=None`) かつ自己完結 (`WindowsAppSDKSelfContained=true`) とし、
  ユーザーに Windows App SDK ランタイムのインストールを求めない。`PublishSingleFile` は初回起動時に
  一時フォルダーへ展開するため使わない。
- UI はコードで組み、XAML は `App.xaml` (`XamlControlsResources`) だけ。`{Binding}` も x:Bind も使わない。`Main` は自前
  (`DISABLE_XAML_GENERATED_MAIN`) で、WinUI を起動する前にリクエストを読む。
- **NativeAOT の落とし穴**: CsWinRT は WinUI から返るオブジェクトを、トリミング後もメタデータが残っている最も具体的な
  投影クラスで包む。コードで生成しないクラスは残らないため、リソース辞書から取った `AccentButtonStyle` が
  `DependencyObject` として返り、`Style` へのキャストが InvalidCastException になった (`AppWindow.Presenter` の
  `OverlappedPresenter` も同じ)。`[DynamicDependency(All, typeof(Style))]` で `Style` を残し、プレゼンターは自分で
  生成する。`CsWinRTRcwFactoryFallbackGeneratorForceOptIn` では直らなかった。デバッグビルド (JIT) では起きないので、
  WinUI のプログラムは AOT で publish したもので確認する。
- 見た目: タイトルバーのない角丸のウィンドウ (`OverlappedPresenter.CreateForDialog` + `SetBorderAndTitleBar(true, false)`、
  角は `DWMWA_WINDOW_CORNER_PREFERENCE`)、最前面、背景は Mica (`MicaBackdrop`)。中身は ContentDialog と同じ構成: 内容は
  明るいレイヤーの上に余白 24 px で置き、`ContentDialogSeparatorBorderBrush` の線で区切って、フッターにボタンを等幅で並べる。
  ContentDialog の色 (`ContentDialogTopOverlay` / `ContentDialogBackground`) は不透明で Mica を隠すため、内容は Windows 11 の
  Mica のウィンドウの半透明のレイヤー (`LayerFillColorDefaultBrush`。設定 UI のカードと同じ) に置き、フッターは Mica のまま
  にする (上が明るく下が素材の、ダイアログと同じ階層)。ボタンは Windows の慣習どおり主アクションが左で、Open (アクセント)、Cancel の順。内容はヘッダー (リンクの
  アイコン、ホスト 20 px、URL 全体、コピーボタン。ドラッグすると移動)、警告 (`message`。警告の `InfoBar` と同じ見た目の枠)、
  候補の `ListView` (アイコン、名前、2 行目、キーキャップ風の番号)、「記憶する」。スナップショット
  (`PREFERIS_PICKER_SNAPSHOT`) は Mica を描けないため、テーマの背景色で代える。
- 普通のウィンドウと同じく、最終的な位置と大きさにしてから 1 回だけ `Activate` で表示し、表示と終了はシステムの
  アニメーションのまま。ポインターの近く (横はポインターの中央、上端はポインターの少し上) か画面中央の作業領域内に置く。
  倍率はその位置のモニターの DPI。
  - 大きさは表示する前に `Measure` で測る。テンプレートは表示されるまで適用されないため、テンプレートで大きさが決まる
    コントロールには大きさを与える: ボタンとチェックボックスは 32 (`ControlHeight`)、候補の行は 56、一覧は行数 (6 まで) × 56。
    警告はテンプレートのない枠 (`Border` + アイコン + 折り返す `TextBlock`) で `InfoBar` と同じ見た目に作る (高さは文から
    決まる)。読み込み後にもう一度測り、違っていれば (文字サイズを大きくした設定など) 位置はそのままで高さだけを直し、
    標準エラー (Router の trace ログ) に書く。
  - 以前は内容を読み込んでから測るため、いったん画面外に表示してから移していた。画面外 (-32000, -32000。最小化した
    ウィンドウの位置) から移すとタスクバーから元に戻すウィンドウのように右下から拡大しながら現れ、移した後に隠して
    表示し直すと開くアニメーションが付かないか、WinUI が内容を描き直すまで白い (キャプチャでは黒い) 枠が見えた。
    いずれも最初の表示ではないことが原因で、最初から最終的な大きさで出せば起きない。
  - 終了は、応答を書いてから `AppWindow.Hide` (システムの隠すアニメーション) で隠し、300 ms 後に閉じる。`Window.Close` は
    内容をすぐ破棄するため、すぐ閉じると閉じるアニメーションの間は黒い矩形になった。Alt+F4 などの閉じる操作も取り消して
    同じように終える。
  - 画面を 30 ms ごとにキャプチャして症状と変化を確かめ、最終的な形は実機で目視した。自前のフェード (レイヤードウィンドウの
    アルファ) なら枠も出ないが、普通のウィンドウと違う見た目になるため採らなかった。
- 見出しはタイトルバーの代わりで、`InputNonClientPointerSource.SetRegionRects` でタイトルバー (`Caption`) の領域にする。
  ドラッグでの移動、他のウィンドウからのクリックでのアクティブ化、右クリックのシステムメニューは普通のタイトルバーと
  同じく OS が行う。コピーボタンと URL (全体を出すツールチップがある) は `Passthrough` にして内容のままにし、URL は文字の
  幅だけにする (行の残りはタイトルバー)。領域は物理ピクセルのクライアント座標で、見出しの大きさや倍率が変わるたびに
  設定し直す。タイトルバーの領域の上ではマウスの移動が XAML に届かないため、自動選択の待ちのやり直しは
  `InputNonClientPointerSource` のイベントで行う。
  - 以前は見出しの `PointerPressed` で `ReleaseCapture` + `WM_NCLBUTTONDOWN` (`HTCAPTION`) を送ってシステムの移動を
    始めていたが、ボタンを離したことが移動のループに届かず (WinUI の入力が受け取る)、クリックしただけでその後の
    ポインターにウィンドウが付いて動き、ドラッグして離した後も次のクリックまで付いてきた。アクティブかどうかに
    よらない。VM で実際のマウス入力 (`mouse_event`) を送って確かめた (2026-10-03): クリックしてから動かすと
    以前は (120, 80) 動き、今は動かない。ドラッグした分だけ動き、離した後は動かない。コピーボタンと URL の
    ツールチップは今までどおり。
- タイトルバーを消したウィンドウでも `AppWindow.ResizeClient` はタイトルバーがある前提で枠を足すため、ウィンドウがその分
  (150 % で 45 px) 高くなり、フッターのボタンの下だけ余白が広がった。設定後の `ClientSize` との差を引いてもう一度設定する。
- キー: ↑↓ (一覧にフォーカスが無い時はこちらで動かす)、Enter (Cancel / コピーにフォーカスがある時はそのボタン)、Esc、
  1〜9 (テンキーも)、一覧で Ctrl+C は URL をコピー。Alt+F4 などでの close はキャンセル。マウス移動・ホイール・キーは
  自動選択のカウントをやり直す。
- アイコンは `WindowsBrowserDetector.TryResolve` で実行ファイルを引いて `WindowsApplicationIcons` (64 px)、コマンドは
  `CommandPrompt`、見つからないブラウザーは `Globe` のグリフ。アバターは `Local State` の `gaia_picture_file_name` を
  右下に丸く重ねる。これらの読み取りは約 60 ms かかるため、`Main` でリクエストを読んだ直後からバックグラウンドで始め、
  WinUI の起動と並行させる (`CandidateImages`)。
- 応答を書いたら `CloseHandle` で標準出力を閉じ、ウィンドウを閉じて終了する。クリップボードは Win32 (`SetClipboardData`)
  を使う: WinRT の `Clipboard` は `Flush` しないとプロセスの終了で中身が消え、ピッカーはコピーの直後に終わることがある。
- Router は `Process.Start` でピッカーを起動し、標準入出力をリダイレクトしてリクエストと応答を
  やり取りする (Design.md §9.3)。
- **フォアグラウンド権限の移譲**: シェルから起動された Router はフォアグラウンド権限を持つが、
  子プロセスのウィンドウは既定では前面に出ない (タスクバーで点滅する) ことがある。Router は
  ピッカーを起動した直後 (`ProcessPickerLauncher` の `started`) にそのプロセス ID で `AllowSetForegroundWindow` を呼び、
  ピッカーは表示時に `SetForegroundWindow` を呼ぶ。前面に出たかどうかを trace に書く (`shown` / `shown (not in front)`)。
  Router 自身が権利を持たない起動 (バックグラウンドのターミナル) では前面に出ない (Design.md §12 スパイク 6)。
- 表示時間は目標内 (約 250 ms、§5) のため、`TaskDialogIndirect` による Win32 実装には切り替えない。
- 表示言語はリクエストの `options.language` (Design.md §9.4)。無ければ `GetUserPreferredUILanguages` で Windows の言語に
  従う。日本語のときはルート要素の `Language` を `ja-JP` にする: XAML は CJK の文字のフォールバック先のフォントを要素の言語で
  選ぶため、英語の Windows では漢字が中国語のフォントで描かれることがある。
- `PREFERIS_TRACE=1` で起動からの経過時間 (main, winui, built, loaded, shown) を標準エラーに書く。
  `PREFERIS_PICKER_SNAPSHOT=<png>` で表示 0.5 秒後の内容を PNG に書き出す (`RenderTargetBitmap`)。

### 2.7 その他

- `Preferis.exe` は GUI サブシステム (`WinExe`) にする (Design.md §5.4)。コマンドは `AttachConsole(ATTACH_PARENT_PROCESS)` で
  ターミナルのコンソールに出力する。標準出力が既にパイプやファイルなら (設定 UI が実行した場合など) 接続しない。
- `app.manifest` で PerMonitorV2 DPI 対応と `longPathAware` を宣言する。Router は最終フォールバックのメッセージボックスの
  ためにコモンコントロール v6 も宣言する。
- アプリアイコンは `src/Preferis.Router/Windows/Preferis.ico` (16・20・24・32・40・48・64・128 px の 32 bit DIB と 256 px の
  PNG の 9 エントリ)。元絵は `src/Preferis.Router/Preferis.png` (256 px) で、.ico はそれを縮小して作る (ImageMagick なら
  `magick Preferis.png -define icon:auto-resize=256,128,64,48,40,32,24,20,16 Windows/Preferis.ico`)。Router・ピッカー・設定 UI の
  3 プロジェクトが `ApplicationIcon` で同じファイルを埋め込む (NativeAOT の exe にもリソースとして残る)。レジストリの
  `DefaultIcon` と `ApplicationIcon` は `Preferis.exe,0` を指しているので、既定のアプリ一覧もこのアイコンになる。
  WinUI はウィンドウにアイコンを付けない (`WM_GETICON` もクラスアイコンも 0)。タスクバーのボタンはそれでも exe の
  アイコンになるが、タスクバーのサムネイルの見出しと Alt+Tab はウィンドウのアイコンを使うので、Windows 既定のアイコンに
  なっていた。そのためピッカーと設定 UI は、ウィンドウを作る時に `WindowsApplicationIcons.LoadApplicationIcon` で exe の
  アイコンを 32 DIP (そのモニターの倍率を掛けたピクセル数) で読み、`AppWindow.SetIcon` に渡す。コンパイラーは
  `ApplicationIcon` をアイコングループ 32512 として書き、NativeAOT もそのまま exe に移すので、.ico を別ファイルとして
  同梱する必要はない。`SetIcon` は大小のアイコン (`ICON_BIG` / `ICON_SMALL`) に同じものを設定するため、16 DIP の表示は
  32 DIP の画像の縮小になる。アイコンはプロセスの終了まで保持する。
- 最終フォールバックのメッセージボックスは `MessageBoxW`。URL がある時は「はい / いいえ」で URL のコピーを尋ねる。

### 2.8 設定 UI (`Preferis.Settings.exe`, WinUI 3)

- `src/Preferis.Settings.Windows` (実行ファイル名 `Preferis.Settings`) は WinUI 3 のプログラムで、構成 (パッケージ、自己完結、
  NativeAOT、コードで組む UI) はピッカーと同じ (§2.6)。画面は `Preferis.Settings.Core` の ViewModel の上に組み、
  `Controls/Bindings` が変更通知とコントロールのイベントで双方向に結ぶ (AppKit 版の `Bindings` と同じ形)。
- 見た目は Windows 11 の設定アプリに倣う: タイトルバーまで広げた Mica、左の `NavigationView` (一般 / プロファイル /
  ルール / テスト)、28 px のページ見出しとその下に問題の件数、4 px 間隔のカード (左に名前と説明、右にコントロール)。
  Community Toolkit の `SettingsCard` は使わない (カードは Border と Grid で足り、外部の XAML コントロールを NativeAOT で検証する手間を避ける)。
  プロファイルとルールは左に一覧 (`ListPane`。ドラッグで並べ替え、下に + − ⋯)、右に選択中の項目の編集画面。
- `ListPane` のドラッグ開始イベントは、明示的に追加した `ListViewItem` 自体ではなく、その `Content` を返す。
  行の中身から ViewModel を特定し、完了時に一覧の順序を ViewModel と設定ファイルへ反映する (#20)。
  WinUI が行を削除・挿入して移動する間の一時的な選択解除は無視し、完了時に移動した項目を画面と ViewModel の両方で選択する。
- 狭いウィンドウでも編集画面に幅を残す: ナビゲーションは `PaneDisplayMode.Auto` で、幅が足りないとアイコンだけに畳み
  (開くボタンはその時だけ出す)、一覧の幅は画面の 36 % (240〜320 DIP)。カードはコントロールの横に文字の幅が 200 DIP 残らない時、
  コントロールを文字の下に回す (設定アプリの SettingsCard と同じ考え方)。既定のウィンドウ (1120 × 780 DIP) でも
  プロファイルとルールの編集画面は幅 490 DIP ほどなので、入力欄のカードは折り返す。
- 「+」のメニューは検出したブラウザーごとのサブメニュー (ブラウザー自体と各ブラウザープロファイル)。条件は
  `ConditionEditor` (Design.md §9.1)。幅が狭いときはパターンの入力欄を次の行に回す。
- 一般ページの最後は、Velopack でインストールされていれば「更新」カード (バージョン、状態の行、「更新を確認」→「ダウンロード」
  (進捗バー) →「再起動して更新」のボタンを 1 つずつ)、そうでなければバージョンのフッター (§2.1 の配布)。ViewModel は
  `AppUpdateViewModel` で、ページではなくセッション (`SettingsViewModel.Updates`) が持つ: Router の「記憶する」で `config.json`
  が読み直されるとページの ViewModel は作り直されるが、進行中のダウンロードは失われない。
- 自分と同じフォルダーに `Preferis.exe` があれば、それを登録先として状態を表示し、「既定にする」では
  `Preferis.exe --register` を実行する。無ければ (デバッグ実行) 登録できない旨を表示する。テストページの「Open」も
  `Preferis.exe <url>` を実行し、Router の終了までの出力を表示する (`RouterProcess`)。
- フォルダー・ファイルの選択は Windows App SDK の `Microsoft.Windows.Storage.Pickers` (ウィンドウ ID を渡すだけで
  非パッケージでも動く)、確認は `ContentDialog` (ボタン 3 つまで。最後のボタンが閉じるボタンで Esc に当たる)。
- 自動保存は `DispatcherQueueTimer` (0.6 秒)。ウィンドウを閉じる時は `AppWindow.Closing` で待っている編集を書き込み、
  書けない時だけ尋ねる。アクティブになったら外部の変更を確かめる。
- 表示言語は起動時、ViewModel を作る前に `config.json` から決める (`SettingsConfigLoader.UseConfiguredLanguage`、
  Design.md §9.4)。ルートの `Language` はピッカーと同じく日本語のとき `ja-JP`。ファイル選択ダイアログ、テキストボックスの
  メニューなど WinUI と OS が描く文字列は Windows の言語に従う (自己完結の出力に WinUI の各言語の `.mui` が入っている)。
  ウィンドウのタイトルも表示言語に従う (`Preferis Settings` / `Preferis 設定`)。
- 単一インスタンス: 名前付きミューテックス `Local\Preferis.Settings` で 2 つ目の起動を検出し、起動中のウィンドウ
  (クラス `WinUIDesktopWin32WindowClass`、タイトル `Preferis Settings` か `Preferis 設定`) を前面に出して終了する。言語を変えた後は
  起動中のウィンドウと新しい起動で言語が違うことがあるため、すべての言語のタイトルで探す (`SettingsText.InEveryLanguage`)。Router の `--settings` と
  引数なしの起動は、新しいプロセスが前に出られるよう `AllowSetForegroundWindow(ASFW_ANY)` を呼んでから起動する。
- `PREFERIS_SETTINGS_SNAPSHOT=<フォルダー>` で各ページ (とルール・プロファイル画面のいくつかの状態) を PNG に書き出して
  終了する。ウィンドウはアクティブにせず画面外に出し、`RenderTargetBitmap` で描く (Mica の代わりにテーマの背景色)。
  大きさは通常の起動と同じ既定サイズ (DIP)。`PREFERIS_SETTINGS_SNAPSHOT_WIDTH=<DIP>` で幅を変えて狭いウィンドウを確かめられる
  (最小幅は 900)。長いプロファイルの編集画面と一般ページ (末尾に「更新」カードかバージョン) は末尾までスクロールした状態も書き出す。`PREFERIS_SETTINGS_SNAPSHOT_URL`、
  `PREFERIS_SETTINGS_SNAPSHOT_APPEARANCE=dark`、`PREFERIS_SETTINGS_SNAPSHOT_CONFIG` は macOS と同じ。チェックボックスの
  チェックは `RenderTargetBitmap` では「−」に写る (実際の状態は UI Automation で確認した)。PNG の名前はページ (`general`、
  `profiles` など) で表示言語によらない。日本語の画面は `PREFERIS_SETTINGS_SNAPSHOT_CONFIG` の `config.json` に
  `"language": "ja"` を書いて写す (macOS も同じ)。

#### 並べ替えの UI 回帰テスト

`test/Preferis.Settings.Windows.Tests/Reordering.ps1` は WinApp CLI 0.7 以上を使い、実際のドラッグで表示順と
保存順、選択中の編集欄、上下への移動、同じ位置へのドロップ、メニューによる移動、ページを開き直した後の順序を確認する。
プロファイルとルールの両方を対象にし、失敗すると例外を返す。ViewModel の単体テストだけでは、WinUI のイベントが返す
オブジェクトの違いを検出できないため別に実行する。

設定がまだない使い捨ての Windows VM で、同じフォルダーの `Reordering.config.json` を
`%APPDATA%/Cysharp/Preferis/config.json` に配置する (`local.json` もない状態)。通常権限でビルド済みの
`Preferis.Settings.exe` を開き、VM の対話セッションから次を実行する。管理者権限で起動したアプリでは WinUI のドラッグを検証できない。

```powershell
./test/Preferis.Settings.Windows.Tests/Reordering.ps1 `
    -AppPid (Get-Process Preferis.Settings).Id `
    -ConfigPath "$env:APPDATA/Cysharp/Preferis/config.json"
```

## 3. macOS

### 3.1 バンドル構成

Router をメイン実行ファイルとし、ピッカーと設定 UI はネストしたヘルパーバンドルに置きます。

```
Preferis.app/
  Contents/
    Info.plist                       Router の plist。CFBundleURLTypes を宣言する唯一のバンドル
    MacOS/Preferis                   Router (NativeAOT, net10.0)
    Helpers/
      Preferis Picker.app/           bundle id: net.mayuki.Preferis.Picker。CFBundleURLTypes なし。LSUIElement
        Contents/Info.plist          Microsoft.macOS が生成 (src/Preferis.Picker.MacOS/Info.plist をマージ)
        Contents/MacOS/Preferis Picker
        Contents/MonoBundle/         NativeAOT でも .NET のネイティブライブラリ数個と runtimeconfig.bin が置かれる
        Contents/Resources/          Assets.car と Preferis.icns (アプリアイコン。Router と同じもの)
      Preferis Settings.app/         bundle id: net.mayuki.Preferis.Settings。CFBundleURLTypes なし。通常のアプリ (Dock に出る)
        Contents/Info.plist          Microsoft.macOS が生成 (src/Preferis.Settings.MacOS/Info.plist をマージ)
        Contents/MacOS/Preferis Settings
        Contents/MonoBundle/         ピッカーと同じ
        Contents/Resources/          ピッカーと同じアイコン
        Contents/Resources/ja.lproj/InfoPlist.strings   日本語の macOS で表示する名前「Preferis 設定」
    Resources/                       Assets.car と Preferis.icns (アプリアイコン。下記)
```

- ヘルパーに独自の bundle id と Info.plist を持たせることで、Launch Services が URL の配送先を
  メインの Router 以外に向けることがなくなり、Dock 表示やアクティベーションポリシーをヘルパーごとに
  設定できる。
- `dotnet publish -r osx-arm64` は素の実行ファイルを出力するため、`build/Preferis.Build.proj` の
  `PublishMacOS` ターゲットでバンドルを組み立てる (Design.md §4.4)。M2 の時点から `artifacts/osx-arm64/Preferis.app`
  として一式を生成し、ローカルテストはこれを `open` して Launch Services に登録して行う。
  NativeAOT ではユニバーサルバイナリが作れない既知の問題があるため、arm64 と x64 を別々に publish する。
- 署名は内側から順に行い、`--deep` は使わない: ヘルパー内の `.dylib` → 各ヘルパー → `Preferis.app`。最後に
  `codesign --verify --strict` で確かめる。開発中は ad-hoc 署名 (`codesign -s -`)。リリースは `SigningIdentity`
  プロパティで Developer ID に切り替え、その時は公証の要件である hardened runtime とタイムスタンプ (`--options runtime
  --timestamp`) を付ける。公証は `PackageMacOS` の `Notarize=true` (下記「配布」)。
- 同じ bundle id のバンドルが複数登録されていると URL の配送先が不定になるため、テスト中は `artifacts/`
  の 1 つだけを登録しておく。古い登録は `lsregister -u <path>` で解除する。
- アプリアイコンは `src/Preferis.Router/macOS/Preferis.icon` (Icon Composer のファイル。星・窓・窓のタイトルバーの
  3 レイヤー)。`build/AppIcon.targets` が Xcode 26 の `actool` でコンパイルし、`Assets.car` (macOS 26 以降が Liquid Glass
  として描くレイヤーと、それより前の macOS 向けに焼き込んだ 16〜1024 px の画像。ダークと色付きの外観も含む) と、
  Xcode も書く予備の `Preferis.icns` (16〜256 px) を作る。3 つのバンドルの `Contents/Resources` に同じものを置き、各
  `Info.plist` に `CFBundleIconName` と `CFBundleIconFile` (どちらも `Preferis`) を書く。Router のバンドルには
  `PublishMacOS` がコピーし、ピッカーと設定 UI には `BundleResource` として Microsoft.macOS が入れる。
  - Microsoft.macOS (26.5) の `ImageAsset` は `.xcassets` だけが対象で、`.icon` を渡しても ACTool タスクは何も出力せず
    警告も出さないため、`actool` を直接呼ぶ。27.0 の SDK は `.icon` も `ImageAsset` に含めるので、移行後は SDK に任せられる
    見込み。
  - `actool` は `--app-icon` の名前が `.icon` のファイル名と違うと、成功を返してアイコンを書かない。出力が無ければ
    ビルドエラーにする。
  - `Assets.car` は 1.6 MB で、3 つのバンドルで計 5 MB ほど増える。ピッカーは `LSUIElement` でアイコンが見える場面は
    ほぼ無いが、Finder やアクティビティモニタで汎用のアイコンにならないよう同じものを入れる。
  - Windows の `Preferis.png` (§2.7) とは別のデータで、どちらかを変えたらもう一方も合わせる。

Router の `Info.plist` の要点:

```xml
<key>CFBundleIdentifier</key>      <string>net.mayuki.Preferis</string>  <!-- 仮 -->
<key>CFBundleExecutable</key>      <string>Preferis</string>
<key>LSUIElement</key>             <true/>   <!-- Dock に出さない -->
<key>LSMinimumSystemVersion</key>  <string>12.0</string>
<key>CFBundleURLTypes</key>
<array>
  <dict>
    <key>CFBundleURLName</key>     <string>Web site URL</string>
    <key>CFBundleURLSchemes</key>  <array><string>http</string><string>https</string></array>
  </dict>
</array>
<key>CFBundleDocumentTypes</key>
<array>
  <dict>
    <key>CFBundleTypeRole</key>    <string>Viewer</string>
    <key>LSItemContentTypes</key>  <array><string>public.html</string><string>public.xhtml</string></array>
  </dict>
</array>
```

`CFBundleURLTypes` (http/https) と `CFBundleDocumentTypes` (public.html) の両方を宣言しているのは、
手元の Chrome / Edge Dev の Info.plist と同じ構成です。この構成に `LSUIElement=true` を組み合わせても
システム設定の「デフォルトの Web ブラウザ」一覧に表示されることをスパイク 4 で確認しました (§5)。
一覧の表示名には `CFBundleDisplayName` ではなく `.app` のファイル名が使われるため、バンドル名は必ず
`Preferis.app` にします。

ヘルパーの `Info.plist`: ピッカーは一瞬だけ表示されるダイアログなので `LSUIElement=true` とし、
ウィンドウ表示時に `NSApp.activate` で前面化する。設定 UI は通常のアプリとして `LSUIElement=false`。

配布 (Design.md §4.4、`PackageMacOS` ターゲット): `Preferis.app` と `/Applications` へのリンクを入れた圧縮 dmg
(`Preferis-<version>-osx-arm64.dmg`) を `hdiutil` で作り、`SigningIdentity` が ad-hoc 以外ならバンドルと同じ identity で
dmg にも署名します。Developer ID による署名と公証 (notarization) がないと Gatekeeper に止められます。開発中の ad-hoc
署名でもローカルでは問題なく動作します。

- 公証は `-p:Notarize=true` と App Store Connect API キー (`NotaryKeyPath` = .p8、`NotaryKeyId`、`NotaryIssuerId`) で、
  `xcrun notarytool submit --wait` に dmg を送り (中のバンドルも対象になる)、`xcrun stapler staple` でチケットを dmg に
  添付し、`spctl --assess --type open` で Gatekeeper の判定を確かめる。
- リリースワークフローはシークレット `APPLE_CERTIFICATE_P12` (Developer ID Application 証明書の .p12 を base64 にしたもの) と
  `APPLE_CERTIFICATE_PASSWORD` があれば一時キーチェーンに証明書を入れて `security find-identity` で identity を見つけ、
  `APPLE_NOTARY_KEY` (.p8 の base64)、`APPLE_NOTARY_KEY_ID`、`APPLE_NOTARY_ISSUER_ID` があれば公証する。無ければ ad-hoc 署名の
  dmg を作る (配布には向かない)。
- `hdiutil create` は負荷の高いマシンで "Resource busy" になることがあるため 3 回まで試す。

### 3.2 既定ブラウザーとして登録する

- `NSWorkspace.shared.setDefaultApplication(at: <Preferis.app の URL>, toOpenURLsWithScheme: "http")` (macOS 12+)。
  呼ぶと OS が確認ダイアログを表示する。`https` も同様に呼ぶ。
- 旧 API `LSSetDefaultHandlerForURLScheme` は非推奨。最低対応を macOS 12 とすることで新 API のみ使う。
- 実機確認 (2026-10-01, macOS 26): http の確認ダイアログで承認すると、https を含めて既定のブラウザーがまとめて切り替わる。
  その後の https への要求は「The file couldn't be opened.」のエラーを返し、旧 API のフォールバックも -54 で拒否されるが、
  https は既に Preferis になっている。そのため成否は呼び出しの戻り値ではなく各スキームの実際のハンドラーで判定する
  (`DefaultHandlerRequest`): 既に Preferis ならそのスキームは要求しない、エラーなく完了した要求は成功とみなす、
  エラーでも直後に Preferis になっていれば成功、それ以外は旧 API を試し、それでも変わらなければ失敗を返す。
- 自分が既定かどうかは `NSWorkspace.shared.urlForApplication(toOpen: URL("https://example.com"))` の
  bundle id が自分かで判定する。
- 失敗時は「システム設定 > デスクトップとDock > デフォルトのWebブラウザ」を案内する
  (`x-apple.systempreferences:com.apple.Desktop-Settings.extension`)。

これらは Router 内から objc ランタイム (`objc_msgSend`) への P/Invoke で呼びます。

### 3.3 ブラウザー検出

- 一次情報源は Launch Services: `NSWorkspace.shared.urlsForApplications(toOpen: URL("https://example.com"))`
  (macOS 12+)。返ってきた各 `.app` の `Info.plist` から `CFBundleIdentifier`、`CFBundleName`、
  `CFBundleExecutable`、`CFBundleIconFile` を読む。
- カタログの bundle id で既知ブラウザーに対応づける。手元で確認した値:
  `com.google.Chrome` (exe `Google Chrome`)、`com.microsoft.edgemac.Dev` (exe `Microsoft Edge Dev`)、
  `com.apple.Safari`。
- 自分自身 (Preferis とそのヘルパー) は除外。
- 高速経路の `TryResolve(browserId)` は `NSWorkspace.shared.urlForApplication(withBundleIdentifier:)`
  で 1 件だけ引く。
- `local.json` の `browserOverrides` に `.app` パスがあればそれを優先する。

### 3.4 ブラウザープロファイル列挙

| 方式 | 場所 |
| --- | --- |
| chromium (Chrome / Beta / Dev / Canary) | `~/Library/Application Support/Google/Chrome`、`Chrome Beta`、`Chrome Dev`、`Chrome Canary` 配下の `Local State` |
| chromium (Edge / Beta / Dev / Canary) | `~/Library/Application Support/Microsoft Edge`、`Microsoft Edge Beta`、`Microsoft Edge Dev`、`Microsoft Edge Canary` 配下の `Local State` |
| chromium (Brave) | `~/Library/Application Support/BraveSoftware/Brave-Browser/Local State` |
| chromium (Vivaldi) | `~/Library/Application Support/Vivaldi/Local State` |
| firefox | `~/Library/Application Support/Firefox/profiles.ini` |

読み取る項目とチャネルの扱いは Windows (§2.4) と同じです。手元の環境では `Microsoft Edge Dev` 配下に
Stable とは独立した `Local State` があることを確認しています。

**macOS 27 以降の制限**: ブラウザー以外のアプリから上記ディレクトリを読むと `EPERM` で拒否される
との報告があります ([finicky#556](https://github.com/johnste/finicky/issues/556))。`stat` は成功し
`open`/`readdir` だけが失敗するため、存在チェックでは検知できません。対策:

1. 設定には常に `directory` を保存し、読めない場合はそのまま `--profile-directory` に渡す
   (Configuration.md §2.5)。起動自体は影響を受けない。
2. 最後に列挙できた結果をローカルキャッシュ (`~/Library/Caches/Preferis/`) に保存し、
   読めない時はキャッシュを使う。
3. 設定 UI で「macOS がアクセスを拒否しています。フルディスクアクセスを許可してください」と案内する。

### 3.5 起動

macOS では **ブラウザーが既に起動している時の挙動**が重要です。`open -a <App> --args ...` は
アプリが起動中だと `--args` を無視して URL を Apple Event で渡すため、プロファイル指定が効きません。
Chromium 系は実行ファイルを直接起動すれば、新プロセスが起動中インスタンスに引数ごと引き渡して
終了するため、起動中でもプロファイル指定が効きます。スパイク 3 で Chrome と Edge Dev について実機確認済み
(§5)。引き渡しにかかる時間は対象プロファイルが既に開いていれば 200 ms 程度、未ロードなら数秒かかります
(ブラウザー側のプロファイル読み込み時間)。

| 対象 | 方法 |
| --- | --- |
| ブラウザー (プロファイル指定なし) | `NSWorkspace.shared.open([url], withApplicationAt: appURL, configuration:)` または `open -b <bundle id> <url>`。初期実装は `open` でよい (プロセス生成コストは数十 ms なので後で objc 直呼びに置換可能) |
| chromium + プロファイル | `<App>.app/Contents/MacOS/<CFBundleExecutable> --profile-directory=<dir> <url>` を直接 `posix_spawn` |
| chromium + private | Windows と同じフラグ |
| firefox + プロファイル | `<App>.app/Contents/MacOS/firefox -P <name> <url>` (要実機確認) |
| safari | `open -b com.apple.Safari <url>` (プロファイルの CLI 指定は不可) |
| `command` | `path` を展開して直接起動 |
| `system` | `NSWorkspace.shared.open(url)` または `open <url>` |

直接起動する場合は子プロセスを待たずに終了します (Preferis 終了後は launchd に再親子付けされる)。
Preferis は `LSUIElement` なので、起動したブラウザーが前面に来るかどうかはブラウザー側の挙動に
依存します (スパイクで確認)。

### 3.6 Router の URL 受信

Router は UI フレームワークを持たないため、Apple Event を受けるための最小限の AppKit 呼び出しを
objc ランタイム経由で自前で行います (`MacUrlActivationSource`)。Launch Services から起動されたかどうかは
LS が設定する環境変数 `__CFBundleIdentifier` が自分の bundle id (`net.mayuki.Preferis`) かで判定します。
この変数は子プロセスに引き継がれるため、有無だけで判定すると Terminal のシェル (`com.apple.Terminal`) や設定 UI が
起動した Router も「LS から起動された」とみなしてしまう (M3 で修正)。シェルから URL を引数に起動された場合は
NSApplication を使わずその場で処理します。

1. `objc_allocateClassPair(NSObject, "PreferisAppDelegate")` でデリゲートクラスを作り、
   `application:openURLs:` と `applicationDidFinishLaunching:` を `class_addMethod` で登録する。
   実装は `[UnmanagedCallersOnly]` な静的メソッド。
2. `[NSApplication sharedApplication]` → `setDelegate:` → `run`。
3. `application:openURLs:` で受け取った `NSArray<NSURL>` を列挙し、`absoluteString` → `UTF8String` で
   文字列化して URL ごとにルーティングを開始する。Finder から `.html` を開いた場合は `file://` URL が届く。
4. 処理中の URL 件数をカウントし、ピッカー待ちを含めてすべて完了したら 300 ms の猶予の後に
   `performSelectorOnMainThread:` 経由で `terminate:` を呼ぶ。URL ごとの処理はスレッドプールで行い、
   ランループは次のイベントを受け続ける。
5. `applicationDidFinishLaunching:` から 750 ms 以内に URL が届かなければ「アプリ自体が開かれた」とみなし、
   `open "Contents/Helpers/Preferis Settings.app"` で設定 UI を開いて終了する。起動中の設定 UI があれば
   Launch Services がそれを前面に出す (二重起動しない)。シェルからは `Preferis --settings` で同じことをする。

スパイク 1 (`spikes/UrlActivation`) でこの手順どおりに動作することを確認しました。必要な objc 呼び出しは
`objc_getClass`, `sel_registerName`, `objc_allocateClassPair`, `class_addMethod`, `objc_registerClassPair` と
`objc_msgSend` の数種類のシグネチャだけです。`LibraryImport` で宣言すれば NativeAOT でそのまま動きます。
`NSAppleEventManager` + `CFRunLoopRun` だけで待つ方式は Launch Services からイベントが配送されず不可でした。
Microsoft.macOS への切り替えは、この実装に問題が出た場合の代替として残します。

### 3.7 ピッカー (`Preferis Picker.app`, AppKit)

- Microsoft.macOS (`net10.0-macos`) で AppKit を直接使う。`PublishAot=true`。NativeAOT は .NET 9 以降正式サポート。
  プロジェクトは `src/Preferis.Picker.MacOS`。Core (リクエスト・応答、`PickerSession`) と Platform (アイコン解決) を参照する。
- UI はコードで組み立てる (nib は ibtool が必要になるだけで利点がない)。見た目は Spotlight に合わせ、タイトルバーのない
  浮動パネルに、ホストを大きく示すヘッダー (リンクのシンボル、ホスト、その下に URL 全体、右端にコピーボタン)、
  候補一覧 (アイコン、名前、2 行目、キーキャップ風のショートカット番号。選択は角丸の無彩色の塗り)、
  「記憶する」チェックと「Cancel」(Esc)「Open」(Enter) を縦に並べる。`HidesOnDeactivate=false` にして、
  他のアプリをクリックしても消えないようにし、`MoveToActiveSpace | FullScreenAuxiliary` で現在のスペースに出す。
- **Liquid Glass (macOS 26)**: ボーダーレスの透明な NSPanel (`CanBecomeKeyWindow` を上書きしてキー入力を受ける、
  背景ドラッグで移動) の中に `NSGlassEffectView` (角丸 26) を置く。ガラスは形状の外側に自前の影を描くため、ガラスを
  ウィンドウいっぱいに置くと影がウィンドウの矩形で切れて四角い枠が見える。ガラスの周りに 32 pt の透明な余白を取り、
  ウィンドウの影はオフにする (余白はクリックを背後へ通す)。ガラスの色付け (`TintColor`) はほぼ差が出なかったため
  システム既定のまま。macOS 25 以前は `NSVisualEffectView` (Popover マテリアル、角丸マスク) とウィンドウの影で代える。
  `PREFERIS_PICKER_SNAPSHOT` の PNG にはガラス (ウィンドウサーバーが合成する) は写らず、レイアウトだけが写る。
- **表示と終了のアニメーション** (Spotlight 風): 表示時はウィンドウの不透明度を 0.18 秒でフェードインしつつ、ガラスの
  レイヤーを 94% からスプリング (質量 1、剛性 320、減衰 24) で等倍に戻す。終了時は応答を書いて標準出力を `close(1)` で
  閉じてから、0.15 秒でフェードアウトしつつ 97% に縮め、完了したら終了する (完了通知が来なくても 1 秒で終了する)。
  レイヤー付きビューのレイヤーは角が基準点なので、中心で拡大縮小するよう平行移動を挟む。
  「視差効果を減らす」(Reduce Motion) が有効な場合は拡大縮小をせず、短いフェードだけにする。
- **NSObject のサブクラスには `NativeHandle` を受け取るコンストラクターが要る**: `Animator` はウィンドウとは別の
  プロキシを返し、そのクラスはサブクラス (`KeyablePanel`) を名乗る。Microsoft.macOS はそれを包む C# オブジェクトを
  作るのにこのコンストラクターを使うため、無いと「Failed to marshal the Objective-C object」で失敗する。
  `NSAnimationContext.RunAnimation` のブロック内で同じ例外が起きると、例外がネイティブ境界を越えて
  `EXC_BAD_ACCESS (EXC_ARM_SP_ALIGN)` でプロセスごと落ちた。フェードはブロックを使わない `BeginGrouping` /
  `EndGrouping` で行い、終了後の処理は `NSTimer` で待つ。
- アイコンは `MacBrowserDetector.TryResolve` で `.app` を引いて `NSWorkspace.icon(forFile:)`。コマンドは `terminal`、
  見つからないブラウザーは `globe` のシステムシンボル。アバターは `BrowserProfileEnumerator` で Chromium の
  `Local State` を読み、`BrowserProfileMatcher` で照合したプロファイルの `gaia_picture_file_name` を角の丸いバッジとして重ねる。
- 数字キー 1〜9 は `NSEvent` のローカルモニターで拾う。マウス移動・スクロール・キー入力は `PickerSession.NoteActivity`
  として自動選択のカウントをやり直す。`DidFinishLaunching` で例外が起きた場合は AppKit に握りつぶされて見えないプロセスが
  残るため、標準エラーに書いて終了コード 1 で終える (Router がフォールバックする)。
- 表示言語はリクエストの `options.language` (無ければ `NSLocale.preferredLanguages`。Design.md §9.4)。`Info.plist` の
  `CFBundleLocalizations` に en と ja を宣言し、AppKit 自身の文字列も日本語の macOS では日本語にする。
- `PREFERIS_TRACE=1` で起動からの経過時間 (main, appkit, shown) を標準エラーに書く。Router はこれを trace ログに転送する。
  `PREFERIS_PICKER_SNAPSHOT=<png>` で表示 0.5 秒後のパネル内容を PNG に書き出す (画面収録の権限なしで見た目を確認するための開発用)。
- Router は `Contents/Helpers/Preferis Picker.app/Contents/MacOS/Preferis Picker` を直接 `posix_spawn` し、
  標準入出力でリクエストと応答をやり取りする (Design.md §9.3)。`open` 経由だと標準入出力を
  つなげないため使わない。
- **アクティベーション**: ヘルパーは `LSUIElement=true` のまま `NSApp.activate(ignoringOtherApps: true)`
  で前面化する。スパイク 2 で、LS が起動した Router の子プロセスとして起動したヘルパーがこの呼び出しだけで
  アクティブになることを確認した (macOS 26、§5)。`activateIgnoringOtherApps:` は macOS 14 で非推奨なので、
  実装では macOS 14 以降で協調型の `activate` を呼び、50 ms 後にまだアクティブでなければ非推奨 API を呼ぶ。
  Router 側の `NSRunningApplication activateWithOptions:` は、ヘルパーのウィンドウサーバー登録を待つ必要があり
  (スパイクでは 400 ms 待った)、スパイク 2 で不要と分かったため実装していない。前面に出ない事例が見つかったら追加する。
- **Microsoft.macOS のビルド挙動**: `.app` は `bin/<Configuration>/net10.0-macos/<RID>/` に組み立てられ、
  `$(OutDir)` や `$(PublishDir)` を変えても動かない。既定でインストーラー `.pkg` も生成するため `CreatePackage=false` にする。
  バンドル名は `ApplicationTitle`、実行ファイル名は `AssemblyName` (`Preferis Picker`)。`Contents/MonoBundle/` には NativeAOT でも
  ランタイムのネイティブ dylib が数個置かれ、実行ファイルがそれらをリンクする。RID を指定しないとユニバーサルビルドに
  なるため、プロジェクトの既定 RID を SDK の RID にしている。
- **`AppBundleDir` をプロジェクト外に向けない**: SDK 26.5 では、ネストしたライブラリの署名用の引数ファイルが dylib 自体に
  上書きされ、187 バイトのテキストになった。`build/Preferis.Build.proj` は既定の場所に作らせてからコピーする。
- **`Info.plist` の変更が反映されない**: SDK 26.5 の `_CompileAppManifest` はビルドのプロパティの一覧 (`_CompileAppManifest.inputs`)
  だけを入力にしており、`Info.plist` を書き換えても、クリーンビルドするまで古いコンパイル済みのマニフェストがバンドルに入った
  (`CFBundleLocalizations` を足しても `artifacts/` の `Preferis.app` に入らなかった)。AppKit の 2 つのプロジェクトは
  `build/AppManifest.targets` を読み込み、`Info.plist` の方が新しければコンパイル済みのマニフェストを消して作り直させる。

### 3.8 設定 UI (`Preferis Settings.app`, AppKit)

- `src/Preferis.Settings.MacOS` は Microsoft.macOS (`net10.0-macos`) の AppKit アプリで、ピッカーと同じく SDK が組み立てた
  `.app` を `Preferis.Build.proj` がコピーする (§3.7)。画面は `Preferis.Settings.Core` の ViewModel の上に AppKit のビューを
  コードで組む。AppKit には C# のオブジェクトへのバインディングがないため、`Bindings` が `INotifyPropertyChanged` の購読と
  コントロールのイベントで双方向に結び、ビューを差し替えるときに購読を外す。
- 見た目はシステム設定に倣う: `NSSplitViewController` のサイドバー、角丸のグループに「左にラベル、右にコントロール」の行を
  並べたフォーム (`Form`)、ウィンドウのサブタイトルにエラー件数。編集は自動で保存する (`TimerSaveScheduler` が編集の 0.6 秒後に
  `NSTimer` で書き込む。Design.md §9.1)。プロファイルとルールは
  左に一覧 (`ListPane`。ドラッグで並べ替え、下に + − ⋯)、右に選択中の項目の編集画面 (先頭にアイコン・名前・概要と問題点のグループ)。
- フォームの寸法と部品は、システム設定が使う SwiftUI の grouped `Form` を macOS 26 で描いて測った値に合わせている:
  ページ余白 20 pt、グループは角丸 12 pt・内側の余白 10 pt・塗りは `quaternarySystemFill`、行の高さは文字だけで 36.5 pt、
  24 pt のコントロールで 37.5 pt (上下の余白 10.25 / 6.75 pt)、見出しは 13 pt 太字で行の内容と揃え、セクション間 28 pt。
  スイッチは mini (HIG の Toggles も grouped form では mini を勧める)、テキストフィールドは枠なし・右寄せで行の残りを占め、
  ポップアップは値の右に `secondarySystemFill` の丸いシェブロン (`MenuPopUpButton`。AppKit に同じ見た目のベゼルはないため
  枠なしのポップアップにバッジを重ねる)。行は `NSStackView` ではなく制約で組む: 横向きのスタックは中央揃えのとき最も高い
  ビューに対して上下のインセットを守らず、説明つきの行が詰まった。
- ルールの条件は `NSPredicateEditor` (`ConditionEditor`)。左の式 `host` / `url` / `urlCaseSensitive` と演算子 `==` /
  `BEGINSWITH` / `ENDSWITH` / `CONTAINS` / `LIKE` (wildcard) / `MATCHES` (regex) の `NSPredicate` を `ConditionPredicates` の木に
  変換する。ポップアップの表示名はテンプレートのビューの項目名を書き換えて変える (エディターは行ごとにそのコピーを使い、
  項目は元の式と演算子を保持する)。`NSPredicateEditor` は自分自身を `NSRuleEditor` のデリゲートにしているので差し替えず、
  変更はアクションと `RowsDidChange` 通知、パターンの入力中は `NSControlTextDidChange` 通知で拾う。パターン欄は
  エディター自身が行の幅に合わせて伸ばす。
- 自分の場所 (`AppContext.BaseDirectory` から 5 階層上) が `.app` なら、それを `Preferis.app` とみなす。`MacDefaultBrowserRegistrar` に
  そのパスを渡して登録状態を表示し、「既定のブラウザーにする」では `Contents/MacOS/Preferis --register` を実行する。
  `dotnet run` で単体起動した場合は登録できない旨を表示する。
- ブラウザー検出 (`MacBrowserDetector.DetectBrowsers`)、ブラウザープロファイル列挙、アイコン (`MacApplicationIcons`, 64 px) は起動時に
  1 回行い、プロファイル画面の「+」メニューの「Detect Browsers Again」でやり直す。フォルダー・ファイルの選択は `NSOpenPanel`、
  確認は `NSAlert` (どちらもモーダル)。
- macOS 27 で Chromium のプロファイルデータが読めない場合 (§3.4) は、検出一覧に「フルディスクアクセスを与える」旨を表示する。
- 表示言語は起動時に `config.json` から決める (§2.8 と同じ)。メニューバーも自前で作るため項目名を訳す (Apple の日本語の
  用語に合わせる: 「… を非表示」「ほかを非表示」「しまう」など)。`NSOpenPanel` や `NSAlert` の既定の部分は AppKit の文字列で、
  `Info.plist` の `CFBundleLocalizations` (en, ja) のうち macOS の言語に合うもので表示される。Preferis の言語の設定には従わない
  ため、英語の macOS で日本語を選んでもこれらは英語のまま。`NSPredicateEditor` の複合条件の行 (「[Any] of the following are
  true」) は AppKit に訳が無く、日本語の macOS でも英語だった (macOS 26 で確認。AppKit の .loctable に該当する文字列が無い)。
  日本語のときは `formattingDictionary` で選択肢を文全体 (「次のすべてを満たす」「次のいずれかを満たす」「次のいずれも満たさ
  ない」、Windows の条件エディターと同じ訳) にする。キーは AppKit が組み立てる行の書式 (`%[Any]@ of the following are true`)、
  値は `%[次のいずれかを満たす]@`。英語は AppKit の既定のまま。比較の行の対象と演算子は項目名を書き換えているので
  Preferis の言語になる。macOS でのビルドと日本語の表示 (設定 UI の各ページ、ピッカー) はスナップショットで確認した。
- アプリ名は日本語の macOS では「Preferis 設定」(Dock、メニューバー、⌘Tab、「Preferis 設定について」のパネル、Finder)。
  `Resources/ja.lproj/InfoPlist.strings` の `CFBundleName` / `CFBundleDisplayName` と `Info.plist` の
  `LSHasLocalizedDisplayName` による。macOS が表示する名前なので Preferis の言語の設定ではなく macOS の言語に従う。
  ファイル名は `Preferis Settings.app` のまま (Router はパスで開く)。Finder の表示は Launch Services が登録し直した時
  (新しいビルドを開いた時など) に変わる。日本語の文言でこのアプリを指す箇所 (メニュー、登録や「開く」の説明、
  フルディスクアクセスの案内) も「Preferis 設定」と書く。
- Launch Services 経由で開いた場合 `HOME` などの環境変数はシェルのものではない。開発中に別の設定で試すときは、
  実行ファイルを直接 `HOME=<dir>` 付きで起動する。
- `PREFERIS_SETTINGS_SNAPSHOT=<フォルダー>` で各ページ (とルール・プロファイル画面のいくつかの状態) を PNG に書き出して終了する。
  `PREFERIS_SETTINGS_SNAPSHOT_URL=<url>` を併せて指定するとテストページでその URL を試した状態を、
  `PREFERIS_SETTINGS_SNAPSHOT_APPEARANCE=dark` でダークモードを描く。`PREFERIS_SETTINGS_SNAPSHOT_CONFIG=<フォルダー>` で
  そのフォルダーの `config.json` と `local.json` を使い、このマシンの設定に触れずに用意した設定 (マシンごとのターゲットなど) を
  写せる。開発用。
  アプリはアクセサリー (Dock に出ない) として起動し、ウィンドウはデスクトップより後ろのレベルに置くので、画面の見た目も
  フォーカスも変わらない。キャプチャは `screencapture -l <ウィンドウ番号>` で行う (macOS 26 ではビューのビットマップ描画に
  スクロールビューの中身が入らず、画面外のウィンドウはキャプチャできない)。起動元 (ターミナルなど) に画面収録の許可が要る。
- Microsoft.macOS の注意点 (§3.7 に加えて): `NSTableView.Source` を設定した時点で `SelectionDidChange` が呼ばれることがあるので、
  ハンドラーは組み立て途中のフィールドではなく通知の `Object` からテーブルを取る。`DidFinishLaunching` で投げた例外は AppKit が
  ログに出して握りつぶし、ウィンドウのないプロセスが残るため、捕まえて終了する。
- 終了 (⌘Q、AppleScript の quit) は書き込み待ちの編集を書き込んでから終わる (保留中で書けない時だけ尋ねる)。`osascript -e 'tell application id
  "net.mayuki.Preferis.Settings" to quit'` は終了したにもかかわらず -128 (ユーザーによるキャンセル) を返すことがある。

## 4. 共通: 起動時間の計測

`tools/bench` に以下を用意します。

- Windows: PowerShell で `Measure-Command` + 起動 API を呼ぶ直前で終了する `--bench` フラグを 20 回実行し
  中央値を取る。ピッカーは `Preferis.Picker.exe --request <file>` の起動から最初の描画までを、
  ピッカー自身が標準エラーに出すタイムスタンプで測る。
- macOS: `hyperfine` があれば使用、なければ zsh ループ + `date +%s%N`。Apple Event 経路は
  `open -b <bundle id> <url>` から Router がログに記録したタイムスタンプまでで測る。
  ピッカーは Windows と同様にヘルパー実行ファイルを直接起動して測る。

計測結果は本書末尾に記録し、Design.md §5.2 の目標値と比較します。

## 5. 計測記録

### 2026-10-03 設定 UI: 1,000 条件のルールの編集 (Design.md §9.1)

環境: Windows 11 Pro (10.0.26200), x64, .NET 10.0.12, Release ビルドの `Preferis.Settings.Core` を UI なしで動かす
(描画は含まない)。Chrome のプロファイル `A` を作り、`*.site0.example,A` 〜 `*.site999.example,A` の 1,000 行を
`RuleCsvImport.Import` で取り込んで保存 (1 つのルールの `or` の下に 1,000 条件) し、`SettingsViewModel` を作り、ルール名を
1 回変えてウォームアップしてから 10 回変える。時間は Stopwatch、割り当ては `GC.GetAllocatedBytesForCurrentThread`。
範囲は修正前 2〜4 回、修正後 6 回の実行の違い (時間はマシンの負荷で揺れる)。修正前は 27af9a1。

| 項目 | 修正前 | 修正後 |
| --- | --- | --- |
| ルール名の変更 (1 回、10 回の平均) | 690〜870 ms、986.6〜986.9 MiB | 1.1〜1.4 ms (中央値 0.9〜1.1 ms)、0.31 MiB |
| 条件のパターンの入力 (当時の `LeafConditionViewModel`。この ViewModel は #7 で削除、1 文字) | 0.6〜1.0 秒、987 MiB | 1.2〜1.4 ms、0.38 MiB |
| 条件エディターの 1 行の入力 (`RuleViewModel.SetCondition`。WinUI の行と NSPredicateEditor の経路) | 0.75〜0.95 秒、987 MiB | 1.3〜2.1 ms、0.48 MiB |
| `SettingsViewModel` の作成 (読み込みと最初の検証) | 1.0〜1.5 秒、990 MiB | 63〜87 ms、8.0 MiB |
| 保存 (保存後の検証を含む) | 0.6〜1.1 秒、988〜989 MiB | 4.7〜7.6 ms、1.8 MiB |
| WinUI の条件エディターがこのルールの 1,000 行に出すパターンの誤りの検査 | 0.7〜1.4 秒、986 MiB | 6〜16 ms、4.5 MiB |
| (別の形) 1 条件のルール 1,000 個で、1 つのルール名の変更 | 1.2 秒、989 MiB | 4.2 ms (中央値 2.9 ms)、1.8 MiB |

修正後の編集 1 回の残りの大半は `config.json` の直列化 (保存した内容と比べて書き込み待ちかを決める。1,000 条件で
0.7〜1.1 ms、0.3 MiB) で、入力の妨げにはならないためそのままにした。

### 2026-10-02 M4: Windows 一式 (`artifacts/win-x64/Preferis/` 82 MB)

環境: Windows 11 Pro (10.0.26200), x64, .NET 10.0.401, Windows App SDK WinUI 2.3.9, NativeAOT。時間は `PREFERIS_TRACE=1` の
各プロセスの記録 (プロセス起動時刻からの経過) と Router のログの時刻。ウォーム 4〜6 回の範囲、初回は別記。

| 項目 | 結果 |
| --- | --- |
| サイズ | Router 4.9 MB、ピッカー 7.6 MB、設定 10 MB、Windows App SDK ほか 60 MB (*.mui の言語フォルダーを含む)。.pdb は含めない |
| Router `--version` (PowerShell から) | 30 ms |
| 空の WinUI ウィンドウ (スパイク 7 の最初の計測) | プロセス起動 → main 15〜18 ms、→ WinUI 起動 50 ms、→ `Activate` 90 ms。初回 285 ms |
| ピッカー単体 (`--request`、4 候補) | main 15〜18 ms、winui 48〜55 ms、built 177〜211 ms、loaded 227〜248 ms、shown 236〜258 ms。初回 281〜330 ms |
| ピッカー単体、ContentDialog 形式に変えた後 (同じリクエスト、10 回) | shown 282〜302 ms、中央値 298 ms。初回 2.7 秒。設定 UI が起動したままなど負荷の違う時に測ったため、上の行との差 (約 40 ms) が変更によるものかは切り分けていない |
| ピッカーの内訳 | ウィンドウ作成 25〜30 ms、タイトルバーを消すフレーム変更 (`SetBorderAndTitleBar` でも `ExtendsContentIntoTitleBar` でも) 65〜70 ms、コントロールの初回生成 55〜60 ms、配置まで 35〜40 ms。アイコンと Local State の読み取り約 60 ms はバックグラウンドで並行 |
| ピッカー単体、最初から最終的な大きさで表示する形にした後 (2 候補、3 回、リモートデスクトップのセッション) | loaded 235〜241 ms、shown 236〜242 ms。画面外に表示して移す形 (shown 310〜330 ms) より速い: 配置のための 2 回目の表示が無い |
| ピッカー単体、背景を Mica にした前後 (2 候補、4 回ずつ、同じセッションで続けて、2026-10-03) | shown: 前 300〜359 ms、後 298〜390 ms (後の 390 ms は 1 回目)。差は揺れの範囲。画面のキャプチャ (ダークテーマ) でフッターが `#202020` → `#1C2122` (壁紙の色の乗った Mica)、内容が `#2B2B2B` → `#262829` (Mica の上のレイヤー) |
| Router → ピッカー (シェルから `Preferis.exe <url>`、3 候補、自動選択 1 秒) | Router 起動 → ピッカー表示 269〜273 ms。ピッカーは "not in front" (Router がバックグラウンドのターミナルから起動されたため) |
| 設定 UI のテストページ「Open」(UI Automation で操作) | Router → ピッカー → 自動選択 → 起動まで 1.65 秒 (うち 1 秒が自動選択の待ち) |
| Router が起動したプログラムが標準出力を継承した時の読み終わり | 修正前 8.15 秒 (起動したプログラムの終了まで)、修正後 0.14 秒 |
| ブラウザー検出 | 全体 25 ms (初回)、`TryResolve` 0.1〜1 ms |
| ルーティング (`new Router(config).Route(url)`、どの条件にも一致しない URL、Release、2026-10-03) | ホストのワイルドカード 1,000 件を 1 つのルールの `or` に: 修正前 572 ms・996 MiB → 修正後 0.43 ms・0.15 MiB。1 条件のルール 1,000 件: 640 ms・997 MiB → 0.70 ms・0.30 MiB。URL の正規表現 1 条件のルール 1,000 件: 740 ms・1,126 MiB → 6.3 ms・5.7 MiB。`NonBacktracking` の構築は 1 つ 0.7〜0.9 ms・0.64〜0.73 MiB、バックトラック版は 4〜8 µs・4〜5 KiB (Design.md §5.2) |
| 破滅的バックトラックを起こす正規表現 (`^https://x/(?:(a+)+b\|a*c)$` に `a` 40 個と `c`) | バックトラック版だけでは 2 秒でタイムアウト。修正後は 10 ms でバックトラック版を打ち切り、`NonBacktracking` が 31 ms で一致と答える |
| `--single-argument` | `https://evil.example/" --config "D:\elsewhere.json` が 1 つの URL のまま扱われ、オプションにならない |

既定ブラウザーとしての登録 (`--register`) と、それを使った実際のリンククリックは利用者の環境を変えるため行っていない
(Design.md §12 スパイク 6, 8)。

### 2026-10-01 M3: ピッカーと設定 UI (`artifacts/osx-arm64/Preferis.app` 37 MB, ad-hoc 署名)

| 項目 | 結果 |
| --- | --- |
| バンドルの内訳 | Router 4.9 MB、`Preferis Picker.app` 4.6 MB (実行ファイル 4.6 MB)、`Preferis Settings.app` 26 MB (実行ファイル 17 MB + Skia 等 9 MB、arm64 のみ) |
| ピッカー単体 (`--request`、シェルから) | プロセス起動 → main 18〜35 ms、→ ウィンドウ表示 128〜150 ms (ウォーム)。初回 (再ビルド直後) 517 ms |
| Router → ピッカー (シェルから `Preferis <url>`、2 候補、自動選択 0.5 秒) | ピッカーのプロセス起動 → main 24〜26 ms、→ ウィンドウ表示 119〜122 ms。URL 受け取り → 自動選択 → 起動まで 636〜641 ms (うち 500 ms が自動選択の待ち) |
| 「記憶する」 | `host equals` のルールが、ピッカーを出したルールの直前に挿入され、同じホストの次の URL はピッカーなしで起動 |
| 履歴 (`logging.history: true`) | 1 URL につき 1 行 `history.jsonl` に追記 |
| `open -a Preferis.app` (URL なし) | Router が設定 UI を開いて終了 |
| Launch Services のハンドラー一覧 | https / public.html とも `Preferis.app` だけが列挙され、ヘルパーは列挙されない |

画面収録の権限がない (かつディスプレイがスリープ中の) 環境で行ったため、見た目は `PREFERIS_PICKER_SNAPSHOT` /
`PREFERIS_SETTINGS_SNAPSHOT` の PNG で確認した。リンクのクリックから Launch Services 経由でピッカーが前面に出ることの
目視確認はスパイク 2 の結果に依っている。

### 2026-10-01 M2: Router 実機確認 (`artifacts/osx-arm64/Preferis.app`, NativeAOT 5.0 MB, ad-hoc 署名)

| 経路 | 結果 |
| --- | --- |
| シェルから `Preferis https://www.example.com/` | Chrome を直接起動。プロセス全体 32 ms (wall)、ルーティング〜起動呼び出し 19.8 ms |
| `open -a Preferis.app https://www.example.com/` | Apple Event で受信し Chrome を起動。受信後 7.4 ms |
| `open -a Preferis.app https://login.microsoftonline.com/` (2 候補) | ピッカー実行ファイル不在を警告し、既定プロファイル Edge Dev へフォールバック。受信後 26 ms |
| `--status` | LS への登録 yes、既定ブラウザーは Edge Dev、設定ファイルの場所とプロファイル/ルール数を表示 |

いずれも処理後にプロセスが残らないことを確認。既定ブラウザー化 (`--register`) は OS の確認ダイアログを伴うため
ユーザー操作で行う。


### 2026-10-01 スパイク 1: macOS Router の URL 受信 (`spikes/UrlActivation`)

環境: macOS 26 (Darwin 25.6), Apple Silicon, .NET 10.0.302, NativeAOT, ad-hoc 署名。`open -a <app> <url>` で起動、
アプリ内で `Process.StartTime` 基準の経過時間を記録。初回 (再 publish 直後) を除いたウォーム 4 回の範囲。

| 計測点 | NSApplication 方式 | 備考 |
| --- | --- | --- |
| プロセス起動 → Main | 76〜120 ms | C の最小アプリを LS 経由で起動すると 34〜66 ms。差分が NativeAOT の初期化 |
| Main → AppKit ロード完了 | 13〜18 ms | `NativeLibrary.Load(AppKit)` |
| → `sharedApplication` 完了 | 50〜60 ms | ウィンドウサーバー接続。`LSBackgroundOnly=true` でも変化なし |
| → `application:openURLs:` 受信 | 17〜25 ms | `run` 開始からイベント配送まで |
| **合計 (起動 → 受信)** | **153〜217 ms** | 初回のみ 630〜940 ms |
| シェルから直接起動した場合の起動 → Main | 9〜35 ms | LS を経由しない参考値 |

`NSAppleEventManager` + `CFRunLoopRun` のみ (NSApplication なし): 3 秒待ってもイベントは届かず、不可。

### 2026-10-01 スパイク 3: 起動中ブラウザーへのプロファイル指定起動

| ブラウザー | 状態 | コマンド | 結果 |
| --- | --- | --- | --- |
| Google Chrome | 未起動 | 実行ファイル直接 `--profile-directory=Default <url>` (detach) | 起動し URL を開いた |
| Google Chrome | 起動中 | 同上 | 183 ms で終了 (引き渡し)。Default のセッションが更新。重複インスタンスなし |
| Microsoft Edge Dev | 起動中 (Default が開いている) | 実行ファイル直接 `--profile-directory=Profile 1 <url>` | 3.1 s で終了。Profile 1 のセッションのみ更新、Default は変化なし。重複インスタンスなし |

Firefox は未インストールのため未検証。

### 2026-10-01 スパイク 2 + 5: ヘルパーバンドルのピッカー (`spikes/PickerHelper`)

Microsoft.macOS (`net10.0-macos`) + NativeAOT。実行ファイル 3.6 MB、バンドル 4.9 MB。ad-hoc 署名。
stdin からリクエスト JSON を読み、ウィンドウ表示後 0.6 秒で先頭候補を自動選択して stdout に応答を書く。

| 計測点 | 単体起動 (シェル) | Router の子プロセス (Router は `open` 経由) |
| --- | --- | --- |
| プロセス起動 → Main | 48〜58 ms | 38〜40 ms |
| → `applicationDidFinishLaunching` | — | 93〜99 ms |
| → ウィンドウ表示 | 218〜234 ms | 187〜192 ms |
| アクティベーション (`isActive` / キーウィンドウ / 最前面) | すべて真 | すべて真 (Router の補助なしでも真) |
| Router: URL 受信 → ヘルパー spawn | — | 0.2 ms 未満 |

初回 (再ビルド直後) は Main まで 1.1 s。Router 側から `NSRunningApplication activateWithOptions:` を呼ぶと
`true` が返るが、呼ばなくてもヘルパーはアクティブになった。

### 2026-10-01 スパイク 4: Launch Services のハンドラー一覧と既定ブラウザー一覧

`LSUIElement=true`、`CFBundleURLTypes` (http, https)、`CFBundleDocumentTypes` (public.html, public.xhtml) を持つ
スパイクバンドルは、`NSWorkspace.urlsForApplications(toOpen:)` の https 一覧と public.html 一覧の両方に
Chrome / Edge Dev / Safari と並んで列挙された。システム設定「デフォルトの Web ブラウザ」の一覧にも表示されることを
目視で確認。表示名は `CFBundleDisplayName` (Preferis Spike) ではなく `.app` のファイル名 (UrlActivation) だった。
