# OS 固有事項

Windows / macOS それぞれで必要な登録、検出、起動、実行ファイル配置の具体的な方法をまとめます。
本書の内容は Platform 層 (`Preferis.Platform`) と各実行ファイルの実装仕様であり、実機確認で
判明した差異は随時この文書に反映します。

## 1. Platform 層のインターフェース

抽象は `Preferis.Core.Hosting` (と `Preferis.Core.Browsers`) にあり、Platform 層はその実装だけを持ちます。
Router のオーケストレーション (`RoutingService`) もフェイクでテストできるよう Core にあります。

```
Preferis.Core.Configuration.ConfigPaths / ConfigLoader (IConfigStore)
    local.json / 既定 config.json / ログの場所。読み込みは決して例外を投げず、壊れていれば既定 + エラー文字列
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

Router 実行ファイル (`Preferis.Router`) は `RouterHost` でこれらを配線し、`FileLog` でログを書きます。
設定 UI は `Preferis.Settings.Services.ISettingsPlatform` を通して同じ実装を使います (macOS は `MacSettingsPlatform`、§3.8)。

## 2. Windows

### 2.1 実行ファイル配置

```
<install dir>/
  Preferis.exe              Router。NativeAOT 単一 exe。依存 DLL なし
  Preferis.Picker.exe       WinUI 3。NativeAOT。Windows App SDK 自己完結モードの DLL 群を同じフォルダーに同梱
  Preferis.Settings.exe     WinUI 3 (M4)。NativeAOT
  Microsoft.ui.xaml.dll ほか Windows App SDK ランタイム (Preferis.Picker.exe と Preferis.Settings.exe が使用)
```

Router は同じフォルダーの DLL を一切インポートしないため、Windows App SDK の DLL が隣にあっても
起動コストに影響しません。

このフォルダー一式は `build/Preferis.Build.proj` の `PublishWindows` ターゲットで `artifacts/win-x64/Preferis/`
に生成します (Design.md §4.4)。ローカルテストではそのフォルダーの `Preferis.exe --register` を実行し、
再ビルド後も同じパスに出力されることで登録を維持します。

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
  shell\open\command\(Default)           = "\"<Preferis.exe>\" \"%1\""

HKCU\Software\RegisteredApplications
  Preferis                               = "Software\Clients\StartMenuInternet\Preferis\Capabilities"
```

- 既定ブラウザーの切り替えは Windows 10 以降プログラムから行えない (UserChoice ハッシュ) ため、
  登録後に `ms-settings:defaultapps` を開いてユーザーに選んでもらう。Windows 11 では
  `ms-settings:defaultapps?registeredAppUser=Preferis` でアプリのページを直接開ける。
- 自分が既定かどうかは `HKCU\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice\ProgId`
  が `PreferisURL` かで判定する (`system` アクションのループ検出にも使う)。
- 実行ファイルの移動を検出するため、設定 UI 起動時に `shell\open\command` のパスと現在のパスを比較し、
  異なれば再登録を促す。登録処理自体は `Preferis.exe --register` が行う。
- 解除は上記キーを削除する。

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
- キー名で判定できない場合は実行ファイル名とパス (`brave.exe`, `vivaldi.exe`, `opera.exe`, `launcher.exe`,
  `...\Google\Chrome Beta\Application\chrome.exe` 等) で判定する。Firefox のキーは `FIREFOX.EXE` や
  インストールごとのハッシュ付き (`Firefox-<hash>`, `Firefox Developer Edition-<hash>`) になるため、
  キー名の前方一致と実行ファイル名で判定する。
- Chrome Canary は既定ブラウザーに設定できず `StartMenuInternet` に登録されないため、カタログの既知パス
  (`%LOCALAPPDATA%\Google\Chrome SxS\Application\chrome.exe`) の存在で検出する。
- `Preferis` 自身は除外する。
- カタログ外は `win:<Key>` として扱い、プロファイル列挙は行わない。
- 高速経路の `TryResolve(browserId)` はカタログからキー名候補を引いて該当キーだけを読む (1〜2 回のレジストリ
  アクセスで済む)。

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

Edge は上記に加えて `edge_account_*` を持つが、`name` / `user_name` は共通なのでそれだけを使う。

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

### 2.6 ピッカー (`Preferis.Picker.exe`, WinUI 3)

- TFM は `net10.0-windows10.0.19041.0` 以上。`Microsoft.WindowsAppSDK` 1.6 以降 (MIT) と
  `Microsoft.Windows.CsWinRT` 2.1.1 以降。`PublishAot=true`。
- 非パッケージ (`WindowsPackageType=None`) かつ自己完結 (`WindowsAppSDKSelfContained=true`) とし、
  ユーザーに Windows App SDK ランタイムのインストールを求めない。`PublishSingleFile` は初回起動時に
  一時フォルダーへ展開するため使わない。
- AOT 対応のため x:Bind 対象の ViewModel は `partial` にし、CsWinRT のソースジェネレーターに任せる。
- Router は `Process.Start` でピッカーを起動し、標準入出力をリダイレクトしてリクエストと応答を
  やり取りする (Design.md §9.3)。
- **フォアグラウンド権限の移譲**: シェルから起動された Router はフォアグラウンド権限を持つが、
  子プロセスのウィンドウは既定では前面に出ない (タスクバーで点滅する) ことがある。Router が
  ピッカーのプロセス ID に対して `AllowSetForegroundWindow` を呼び、ピッカーは表示時に
  `SetForegroundWindow` を呼ぶ。効果はスパイクで確認する。
- 表示時間が目標 (300 ms) を超える場合は、`TaskDialogIndirect` (コマンドリンクボタン + 検証チェックボックス)
  による Win32 実装に切り替える。この場合候補ごとのアイコンは出せないが依存と初期化コストがゼロになる。

### 2.7 その他

- `app.manifest` で PerMonitorV2 DPI 対応と `longPathAware` を宣言する。
- アイコンは `ApplicationIcon` で `Preferis.exe` に埋め込み、レジストリの `DefaultIcon` から参照する。
- 最終フォールバックのメッセージボックスは `MessageBoxW` を P/Invoke する。

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
      Preferis Settings.app/         bundle id: net.mayuki.Preferis.Settings。CFBundleURLTypes なし。通常のアプリ (Dock に出る)
        Contents/Info.plist          Microsoft.macOS が生成 (src/Preferis.Settings.MacOS/Info.plist をマージ)
        Contents/MacOS/Preferis Settings
        Contents/MonoBundle/         ピッカーと同じ
    Resources/                       アイコン (Preferis.icns) は未作成
```

- ヘルパーに独自の bundle id と Info.plist を持たせることで、Launch Services が URL の配送先を
  メインの Router 以外に向けることがなくなり、Dock 表示やアクティベーションポリシーをヘルパーごとに
  設定できる。
- `dotnet publish -r osx-arm64` は素の実行ファイルを出力するため、`build/Preferis.Build.proj` の
  `PublishMacOS` ターゲットでバンドルを組み立てる (Design.md §4.4)。M2 の時点から `artifacts/osx-arm64/Preferis.app`
  として一式を生成し、ローカルテストはこれを `open` して Launch Services に登録して行う。
  NativeAOT ではユニバーサルバイナリが作れない既知の問題があるため、arm64 と x64 を別々に publish する。
- 署名は内側から順に行い、`--deep` は使わない: ヘルパー内の `.dylib` → 各ヘルパー → `Preferis.app`。最後に
  `codesign --verify --strict` で確かめる。開発中は ad-hoc 署名 (`codesign -s -`)、リリース (M5) では
  `SigningIdentity` プロパティで Developer ID に切り替え、公証 (と hardened runtime) を追加する。
- 同じ bundle id のバンドルが複数登録されていると URL の配送先が不定になるため、テスト中は `artifacts/`
  の 1 つだけを登録しておく。古い登録は `lsregister -u <path>` で解除する。

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

配布: Developer ID による署名と公証 (notarization) がないと Gatekeeper に止められます。
これはリリース段階 (M5) の作業であり、開発中の ad-hoc 署名でもローカルでは問題なく動作します。

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

### 3.8 設定 UI (`Preferis Settings.app`, AppKit)

- `src/Preferis.Settings.MacOS` は Microsoft.macOS (`net10.0-macos`) の AppKit アプリで、ピッカーと同じく SDK が組み立てた
  `.app` を `Preferis.Build.proj` がコピーする (§3.7)。画面は `Preferis.Settings.Core` の ViewModel の上に AppKit のビューを
  コードで組む。AppKit には C# のオブジェクトへのバインディングがないため、`Bindings` が `INotifyPropertyChanged` の購読と
  コントロールのイベントで双方向に結び、ビューを差し替えるときに購読を外す。
- 見た目はシステム設定に倣う: `NSSplitViewController` のサイドバー、角丸のグループに「左にラベル、右にコントロール」の行を
  並べたフォーム (`Form`)、ツールバーの Revert / Save、ウィンドウのサブタイトルに未保存とエラー件数。プロファイルとルールは
  左に一覧 (`ListPane`。ドラッグで並べ替え、下に + − ⋯)、右に選択中の項目の編集画面。
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
- Launch Services 経由で開いた場合 `HOME` などの環境変数はシェルのものではない。開発中に別の設定で試すときは、
  実行ファイルを直接 `HOME=<dir>` 付きで起動する。
- `PREFERIS_SETTINGS_SNAPSHOT=<フォルダー>` で各ページ (とルール・プロファイル画面のいくつかの状態) を PNG に書き出して終了する。
  `PREFERIS_SETTINGS_SNAPSHOT_URL=<url>` を併せて指定するとテストページでその URL を試した状態を描く。開発用。
  アプリはアクセサリー (Dock に出ない) として起動し、ウィンドウはデスクトップより後ろのレベルに置くので、画面の見た目も
  フォーカスも変わらない。キャプチャは `screencapture -l <ウィンドウ番号>` で行う (macOS 26 ではビューのビットマップ描画に
  スクロールビューの中身が入らず、画面外のウィンドウはキャプチャできない)。起動元 (ターミナルなど) に画面収録の許可が要る。
- Microsoft.macOS の注意点 (§3.7 に加えて): `NSTableView.Source` を設定した時点で `SelectionDidChange` が呼ばれることがあるので、
  ハンドラーは組み立て途中のフィールドではなく通知の `Object` からテーブルを取る。`DidFinishLaunching` で投げた例外は AppKit が
  ログに出して握りつぶし、ウィンドウのないプロセスが残るため、捕まえて終了する。
- 終了 (⌘Q、AppleScript の quit) は未保存の変更がなければそのまま終わる。`osascript -e 'tell application id
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
