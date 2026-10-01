# OS 固有事項

Windows / macOS それぞれで必要な登録、検出、起動、実行ファイル配置の具体的な方法をまとめます。
本書の内容は Platform 層 (`Preferis.Platform`) と各実行ファイルの実装仕様であり、実機確認で
判明した差異は随時この文書に反映します。

## 1. Platform 層のインターフェース

```
IAppPaths                   local.json / 既定 config.json / ログ / キャッシュ / 同梱実行ファイルの場所
IBrowserDetector            DetectBrowsers() -> DetectedBrowser[]
                            TryResolve(browserId) -> DetectedBrowser?       // 高速経路用 (1 件だけ)
IBrowserProfileEnumerator   Enumerate(DetectedBrowser) -> BrowserProfileInfo[]
IBrowserLauncher            Launch(LaunchPlan)
ISystemOpener               OpenWithSystemDefault(Uri)                      // `system` アクション
                            IsSelfDefaultHandler(scheme) -> bool            // ループ検出
IDefaultBrowserRegistrar    Register() / Unregister() / GetStatus() / OpenSystemSettings()
IPickerLauncher             Show(PickerRequest) -> PickerResponse?          // ピッカーを子プロセスで起動 (Design.md §9.3)
IMessageBox                 ShowError(title, message, url)                  // 最終フォールバック。UI フレームワーク不要
IUrlActivationSource        URL の受信 (argv / Apple Event)。Design.md §5.3 参照
```

`IPickerLauncher` の実装は共通で、同梱されたピッカー実行ファイルのパスだけが OS ごとに異なります
(`IAppPaths.PickerExecutable`)。起動に失敗した場合や応答が解釈できない場合は `null` を返し、
Router がフォールバックします。

## 2. Windows

### 2.1 実行ファイル配置

```
<install dir>/
  Preferis.exe              Router。NativeAOT 単一 exe。依存 DLL なし
  Preferis.Picker.exe       WinUI 3。NativeAOT。Windows App SDK 自己完結モードの DLL 群を同じフォルダーに同梱
  Preferis.Settings.exe     Avalonia。NativeAOT 単一 exe
  Microsoft.ui.xaml.dll ほか Windows App SDK ランタイム (Preferis.Picker.exe のみが使用)
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
      Preferis Picker.app/           bundle id: net.mayuki.Preferis.Picker (仮)。CFBundleURLTypes なし
        Contents/Info.plist
        Contents/MacOS/Preferis Picker
      Preferis Settings.app/         bundle id: net.mayuki.Preferis.Settings (仮)。CFBundleURLTypes なし
        Contents/Info.plist
        Contents/MacOS/Preferis Settings
    Resources/Preferis.icns
```

- ヘルパーに独自の bundle id と Info.plist を持たせることで、Launch Services が URL の配送先を
  メインの Router 以外に向けることがなくなり、Dock 表示やアクティベーションポリシーをヘルパーごとに
  設定できる。
- `dotnet publish -r osx-arm64` は素の実行ファイルを出力するため、`build/Preferis.Build.proj` の
  `PublishMacOS` ターゲットでバンドルを組み立てる (Design.md §4.4)。M2 の時点から `artifacts/osx-arm64/Preferis.app`
  として一式を生成し、ローカルテストはこれを `open` して Launch Services に登録して行う。
  NativeAOT ではユニバーサルバイナリが作れない既知の問題があるため、arm64 と x64 を別々に publish する。
- 署名は内側 (ヘルパー) から順に行い、`--deep` は使わない。開発中は ad-hoc 署名 (`codesign -s -`)、
  リリース (M5) では `SigningIdentity` プロパティで Developer ID に切り替え、公証を追加する。
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
手元の Chrome / Edge Dev の Info.plist と同じ構成です。システム設定の「デフォルトの Web ブラウザ」
一覧に表示される条件はこの 2 つを満たすことと理解していますが、`LSUIElement=true` との組み合わせは
スパイクで確認します (Design.md §12)。

ヘルパーの `Info.plist`: ピッカーは一瞬だけ表示されるダイアログなので `LSUIElement=true` とし、
ウィンドウ表示時に `NSApp.activate` で前面化する。設定 UI は通常のアプリとして `LSUIElement=false`。

配布: Developer ID による署名と公証 (notarization) がないと Gatekeeper に止められます。
これはリリース段階 (M5) の作業であり、開発中の ad-hoc 署名でもローカルでは問題なく動作します。

### 3.2 既定ブラウザーとして登録する

- `NSWorkspace.shared.setDefaultApplication(at: <Preferis.app の URL>, toOpenURLsWithScheme: "http")` (macOS 12+)。
  呼ぶと OS が確認ダイアログを表示する。`https` も同様に呼ぶ。
- 旧 API `LSSetDefaultHandlerForURLScheme` は非推奨。最低対応を macOS 12 とすることで新 API のみ使う。
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
終了するため、起動中でもプロファイル指定が効きます (Finicky 等の実績あり)。

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
objc ランタイム経由で自前で行います。

1. `objc_allocateClassPair(NSObject, "PreferisAppDelegate")` でデリゲートクラスを作り、
   `application:openURLs:` と `applicationDidFinishLaunching:` を `class_addMethod` で登録する。
   実装は `[UnmanagedCallersOnly]` な静的メソッド。
2. `[NSApplication sharedApplication]` → `setDelegate:` → `run`。
3. `application:openURLs:` で受け取った `NSArray<NSURL>` を列挙し、`absoluteString` → `UTF8String` で
   文字列化して URL ごとにルーティングを開始する。Finder から `.html` を開いた場合は `file://` URL が届く。
4. 処理中の URL 件数をカウントし、ピッカー待ちを含めてすべて完了したら `terminate:` を呼ぶ。
   処理中に次のイベントが届いた場合は同じプロセスで並行して処理する。

必要な objc 呼び出しは十数個に収まる想定です。実装が難航する場合は Router の macOS ビルドだけを
Microsoft.macOS (`net10.0-macos`) に切り替え、`NSApplicationDelegate.OpenUrls` を使います。
その場合はランタイム初期化コストを計測して目標 (Design.md §5.2) に収まることを確認します。

### 3.7 ピッカー (`Preferis Picker.app`, AppKit)

- Microsoft.macOS (`net10.0-macos`) で AppKit を直接使う。`PublishAot=true`。NativeAOT は .NET 9 以降正式サポート。
- NSPanel 上に NSTableView または NSStackView で候補を並べ、アイコンは `NSWorkspace.icon(forFile:)`、
  アバターは Chromium プロファイルディレクトリ内の画像から読む。
- Router は `Contents/Helpers/Preferis Picker.app/Contents/MacOS/Preferis Picker` を直接 `posix_spawn` し、
  標準入出力でリクエストと応答をやり取りする (Design.md §9.3)。`open` 経由だと標準入出力を
  つなげないため使わない。
- **アクティベーション**: ヘルパーは `LSUIElement=true` のまま `NSApp.activate(ignoringOtherApps: true)`
  で前面化する。macOS 14 以降はアクティベーションが協調型になったため、Router 側で
  `NSRunningApplication.activate` や `yieldActivation(to:)` 相当を呼ぶ必要があるか、スパイクで確認する。
- 設定 UI (`Preferis Settings.app`) は Avalonia の実行ファイルを同じ形式でバンドル化し、Router が
  引数なしで起動された時に `open` で起動する。

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

(未計測)
