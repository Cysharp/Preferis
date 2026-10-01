# Preferis 設計ドキュメント

Preferis は OS の既定ブラウザーとして登録され、受け取った URL をルールに従って実際のブラウザー
(またはブラウザープロファイル、任意の実行ファイル) に振り分けるトランポリン型アプリケーションです。

本書は全体アーキテクチャと設計判断をまとめたものです。関連ドキュメント:

- [InitialPrompt.md](InitialPrompt.md) — 元となる要求仕様 (本書の入力)
- [Configuration.md](Configuration.md) — 設定ファイルの仕様 (スキーマ、解決規則、例)
- [Platform.md](Platform.md) — OS 固有事項 (ブラウザー登録、検出、プロファイル列挙、起動方法、バンドル構成)
- [Guidelines.md](Guidelines.md) — リポジトリ運用ルール

## 1. ゴールと非ゴール

### ゴール

- URL を受け取ってからブラウザー起動 API を呼ぶまでを可能な限り短くする (トランポリン性能)
- Windows / macOS で同一の設定ファイルを使え、クラウドストレージ経由で同期できる
- ブラウザーとそのプロファイルを自動検出し、GUI でルールを組み立てられる
- ピッカーは OS ネイティブの UI で、使用頻度の低さに見合う遅延コストで表示する
- どのような失敗時も URL を黙って捨てない (必ずどこかで開くか、ユーザーに提示する)

### 非ゴール (初期リリース)

- `mailto:` など http/https 以外のスキームの既定ハンドラーになること
- ブラウザー拡張機能との連携、ページ内容の解析
- Linux 対応 (設計上は Platform 層の追加で対応可能にしておく)

## 2. 用語

| 用語 | 意味 |
| --- | --- |
| プロファイル (Profile) | Preferis 上の起動対象。ブラウザー + ブラウザープロファイル + オプション、または任意の実行コマンド |
| ブラウザープロファイル (BrowserProfile) | Chrome/Edge/Firefox 等が持つユーザープロファイル。Preferis のプロファイルとは別物 |
| ブラウザー (Browser) | 検出されたブラウザー実体。カタログ上の安定 ID (`edge`, `chrome` など) で参照する |
| ルーティング (Routing) | URL を入力としてアクションを決定する処理 |
| ルーティングルール (Rule) | 条件 (Condition) とアクション列 (Actions) の組。上から順に評価し最初にマッチしたものを適用 |
| キャッチオールルール | 常に末尾に存在し削除できないルール。どのルールにもマッチしなかった場合のフォールバック |
| Router | URL を受け取りルーティングと起動を行う実行ファイル。UI フレームワークを持たない |
| ピッカー (Picker) | 起動先候補が複数ある時に表示するプロファイル選択ダイアログ。Router とは別の実行ファイル |

## 3. 技術スタック

| 項目 | 選定 | 理由 |
| --- | --- | --- |
| ランタイム | .NET 10 (LTS), NativeAOT | 起動速度要件。全プロジェクト `IsAotCompatible=true` |
| Router | UI フレームワークなし。macOS の Apple Event 受信は objc ランタイム直接呼び出し | 高速経路に余計な初期化を一切載せない |
| ピッカー (Windows) | WinUI 3 (Windows App SDK 1.6+, MIT) | Windows 11 ネイティブの外観。NativeAOT 対応。表示時間が目標を超える場合は Win32 TaskDialog に切り替える |
| ピッカー (macOS) | AppKit (Microsoft.macOS, `net10.0-macos`) | OS 標準の外観。NativeAOT は .NET 9 以降正式サポート |
| 設定 UI | Avalonia 12.x (MIT) | 両 OS で 1 つの実装。使用頻度が低く、起動速度の制約を受けない。将来ネイティブ化する余地は残す |
| MVVM | CommunityToolkit.Mvvm 8.x (MIT) | ソースジェネレーターベースで AOT 安全。WinUI 3 / Avalonia 共通 |
| 設定・IPC | System.Text.Json (ソース生成, `JsonPolymorphic`) | リフレクション不要、AOT 安全。Router とピッカー間の IPC にも同じコンテキストを使う |
| 正規表現 | `System.Text.RegularExpressions` (`NonBacktracking` 優先) | ユーザー入力パターンによる破滅的バックトラックを防ぐ。AOT で動作 |
| テスト | xunit (Apache 2.0) | FluentAssertions v8 は商用ライセンスのため使わない。必要なら Shouldly (BSD) |

依存ライブラリは Guidelines のライセンス条件 (MIT/Apache 2.0/BSD/ISC/MPL/PD) を満たすものに限ります。
Windows App SDK と Microsoft.macOS は Microsoft 製の SDK であり、Guidelines の Windows SDK と同じ扱いです。

## 4. アーキテクチャ概要

```
 OS シェル ── URL ──▶ ┌──────────────────────────────┐
                      │ Preferis (Router)            │  UI フレームワークなし
                      │  受信 → ルーティング →       │
                      │  プロファイル解決 → 起動     │
                      └──────┬───────────────┬───────┘
                             │ 候補が複数     │ 引数なし起動
                       stdin/stdout JSON      │
                             ▼               ▼
              ┌───────────────────┐  ┌───────────────────┐
              │ Preferis.Picker   │  │ Preferis.Settings │
              │ WinUI 3 / AppKit  │  │ Avalonia          │
              └───────────────────┘  └───────────────────┘

 ────────────────────── 共有ライブラリ ──────────────────────
  Preferis.Core      設定モデル / ルーティングエンジン / プロファイル解決 / IPC メッセージ
  Preferis.Platform  ブラウザー検出 / プロファイル列挙 / 起動 / OS 登録 / パス (Windows, macOS)
```

### 4.1 実行ファイル構成

UI フレームワークを 1 つも含まない Router と、UI ごとの実行ファイルに分けます。

| 実行ファイル | 役割 | UI |
| --- | --- | --- |
| `Preferis` (`Preferis.exe` / `Preferis.app` のメイン実行ファイル) | URL の受信、ルーティング、プロファイル解決、ブラウザー起動、ピッカーと設定 UI の子プロセス起動、OS 登録 | なし (最終フォールバックの OS メッセージボックスのみ) |
| `Preferis.Picker` (`Preferis.Picker.exe` / `Preferis Picker.app`) | 候補からプロファイルを選ばせる | WinUI 3 / AppKit |
| `Preferis.Settings` (`Preferis.Settings.exe` / `Preferis Settings.app`) | プロファイルとルールの編集 | Avalonia |

分離の理由:

- Router のバイナリに UI フレームワークの静的初期化や依存 DLL が一切入らない。特に Windows App SDK の
  Bootstrapper はモジュール初期化子で自動起動するため、同一 exe に同居させると URL を開くたびに
  コストを払うことになる。
- WinUI 3 プロジェクトは Windows でしか、AppKit プロジェクトは macOS でしかビルドできない。
  Router と共有ライブラリは `net10.0` 単一 TFM のまま全 OS でビルドできる。
- ピッカーが異常終了しても Router が URL を保持しているのでフォールバックできる。
- ピッカー・設定 UI をそれぞれ単体で起動して確認できる。

### 4.2 プロジェクト構成

Guidelines の `src` / `test` 構成に従います。

```
Preferis.slnx
src/
  Preferis.Core/              設定モデル、JSON シリアライズ、条件マッチャー、ルーティングエンジン、
                              プロファイル解決、ピッカー IPC メッセージ。OS・UI 非依存。
  Preferis.Platform/          IBrowserDetector / IBrowserLauncher / IDefaultBrowserRegistrar /
                              IPickerLauncher / IAppPaths の抽象と Windows / macOS 実装。
  Preferis.Router/            Router 実行ファイル (AssemblyName=Preferis)。net10.0。
                              macOS の Apple Event 受信 (objc 直接呼び出し) を含む。
  Preferis.Picker.Windows/    WinUI 3。net10.0-windows10.0.xxxxx。Windows でのみビルド。
  Preferis.Picker.MacOS/      AppKit (Microsoft.macOS)。net10.0-macos。macOS でのみビルド。
  Preferis.Settings/          Avalonia 設定 UI。net10.0。
test/
  Preferis.Core.Tests/
  Preferis.Platform.Tests/    フィクスチャ (Local State, profiles.ini) を使った解析テスト
  Preferis.Settings.Tests/    ViewModel テスト (必要なら Avalonia.Headless)
tools/
  bench/                      起動時間計測スクリプト
docs/
```

OS 固有のピッカープロジェクトは、ソリューションフィルター (`Preferis.Windows.slnf`, `Preferis.MacOS.slnf`)
または `Condition` 付きの参照で、ビルド可能な OS 上でのみ対象にします。これにより Guidelines の
「常にビルドできる状態を保つ」を OS ごとに満たします。

Platform 層は単一プロジェクトとし、`OperatingSystem.IsWindows()` 等による実行時分岐と
`[SupportedOSPlatform]` 注釈で実装します。

### 4.3 起動モード

| 起動 | 動作 |
| --- | --- |
| `Preferis <url>` | Router。OS シェルからの起動。最速経路 |
| `Preferis` (引数なし) | `Preferis.Settings` を起動して終了 |
| `Preferis --dry-run <url> [--config <path>]` | 設定を読み、ルーティングの過程 (トレース) と決定、候補プロファイルを標準出力に表示して終了 (起動しない)。デバッグ・テスト用。終了コードは決定できれば 0、失敗なら 2 |
| `Preferis --register` / `--unregister` | OS への登録/解除 |
| `Preferis.Picker` | 標準入力からリクエスト JSON を読み、標準出力に応答 JSON を書く (§9.3) |
| `Preferis.Picker --request <path>` | リクエストをファイルから読む。単体での動作確認用 |
| `Preferis.Settings` | 設定 UI |

### 4.4 ビルドと成果物

ローカルや内部テストのために、**どのマイルストーンの時点でも `.app` / `.exe` の一式を 1 コマンドで
作れる**ようにします。署名 (Developer ID)、公証、インストーラーはリリース段階 (M5) で追加しますが、
成果物の形はそれ以前から最終形と同じにします。

```
build/
  Preferis.Build.proj     MSBuild オーケストレーションプロジェクト。追加ツールのインストール不要
artifacts/                 出力先 (.gitignore 対象)
  osx-arm64/Preferis.app
  osx-x64/Preferis.app
  win-x64/Preferis/        Preferis.exe, Preferis.Picker.exe (+ Windows App SDK DLL), Preferis.Settings.exe
```

| コマンド | 内容 |
| --- | --- |
| `dotnet build build/Preferis.Build.proj -t:PublishMacOS -p:RuntimeIdentifier=osx-arm64` | Router / Picker.MacOS / Settings を NativeAOT で publish し、`Preferis.app` (ヘルパーバンドル込み) を組み立て、ad-hoc 署名する |
| `dotnet build build/Preferis.Build.proj -t:PublishWindows -p:RuntimeIdentifier=win-x64` | 3 つの実行ファイルと Windows App SDK の自己完結 DLL を 1 フォルダーにまとめる |
| `dotnet build build/Preferis.Build.proj -t:PublishMacOS -p:SigningIdentity="Developer ID Application: ..." -p:Notarize=true` | M5 で追加するリリース用。ad-hoc 署名を Developer ID に置き換え、公証まで行う |

- オーケストレーションを MSBuild にするのは、Guidelines の「.NET CLI ツールをグローバルにインストールしない」
  に従い、`dotnet` だけで完結させるためです。シェルや PowerShell のスクリプトは用意しません。
- バンドル組み立て (Info.plist の配置、`Contents/Helpers/` へのヘルパー配置、`codesign` 呼び出し) は
  `Preferis.Build.proj` のターゲットとして実装し、Router プロジェクト自体は素の `dotnet publish` で
  成果物を出すだけにします。
- 開発中は `artifacts/` 配下の成果物をそのまま使ってテストします。macOS は `open artifacts/osx-arm64/Preferis.app`
  で Launch Services に登録され、Windows は `artifacts\win-x64\Preferis\Preferis.exe --register` で
  レジストリにそのパスが書かれます。再ビルドしても同じパスに出力されるため再登録は不要です。
- macOS では同じ bundle id のバンドルが複数あると Launch Services の URL 配送先が不定になります。
  `/Applications` に古いコピーを置いたまま `artifacts/` の成果物を試さないこと。必要なら
  `lsregister -u <path>` で古い登録を解除します。
- ユニバーサルバイナリは NativeAOT で作れない既知の問題があるため、RID ごとに別の `.app` を出力します。

## 5. Router 高速経路

### 5.1 処理フロー

```
argv / Apple Event
  │
  ├─ 1. 入力正規化      ファイルパスなら file:// に変換、Uri パース、Host 小文字化
  ├─ 2. 設定ロード      local.json → configPath 解決 → config.json (ソース生成 JSON)
  ├─ 3. ルーティング    ルールを上から評価 → RoutingDecision
  ├─ 4. プロファイル解決 決定に含まれるプロファイルだけをブラウザー実体に解決
  │                      (この時点で初めてレジストリ / Launch Services / Local State を読む)
  ├─ 5a. 単一候補       → 起動して終了
  └─ 5b. 複数候補       → Preferis.Picker を起動し、リクエストを書き、応答を待つ
                           → 選択されたプロファイルを起動 (「記憶する」ならルールを追記) して終了
```

### 5.2 性能方針

- **Router に UI フレームワークを含めない。** ピッカーと設定 UI は別実行ファイルであり、Router の
  バイナリは Core + Platform + エントリーポイントのみで構成する。
- **必要なものだけ解決する。** ブラウザー検出は「決定されたプロファイルが参照するブラウザー」
  に限定し、全ブラウザーの列挙や全プロファイルの読み取りは設定 UI でのみ行う。
- **Regex は遅延コンパイル**し、評価対象になった条件だけ構築する。`RegexOptions.NonBacktracking`
  を既定にし、未対応構文 (後方参照・先読み) で `NotSupportedException` になった場合のみ
  バックトラック版 + 100ms タイムアウトにフォールバックする。
- 設定の JSON はソース生成コンテキストで読み、未知プロパティは `[JsonExtensionData]` で保持する
  (新旧バージョン間で同期した時に設定を壊さないため)。
- ピッカーの起動コストはプロセス生成 + UI フレームワーク初期化だが、候補が複数ある場合にしか
  発生しない。アイコン等のバイナリは IPC で渡さず、ピッカー側で識別子から解決する。
- ログは既定で無効。`PREFERIS_TRACE=1` 環境変数でフェーズごとの経過時間をログに出す。
- `tools/bench` で「プロセス起動 → 起動 API 呼び出し」と「プロセス起動 → ピッカー表示」を計測する。

目標値 (ブラウザー自身の起動時間は含まない):

| 区間 | Windows | macOS |
| --- | --- | --- |
| プロセス起動 → URL 受信 | argv なので実質 0 | 200 ms 以下 (ウォーム)。スパイク 1 で計測済み。内訳は LS 起動の床 35〜65 ms、NativeAOT 初期化、AppKit ロード 15 ms、`NSApplication` 初期化 50〜60 ms、イベント配送 20 ms (Platform.md §5)。Preferis 側で削れるのは AppKit 以降の約 85 ms のみで、NSApplication なしでは受信できない |
| URL 受信 → ブラウザー起動 API 呼び出し (Preferis 自身の処理) | 20 ms 以下 | 10 ms 以下 |
| プロセス起動 → ピッカー表示 | 300 ms 以下 (WinUI 3 がこれを超えるなら TaskDialog) | 350 ms 以下 (URL 受信 200 ms + ヘルパー起動) |

### 5.3 macOS における URL 受信

macOS では既定ブラウザーに渡される URL は argv ではなく **Apple Event (`application:openURLs:`)**
として配送されます。そのため Router は NSApplication のランループを回す必要があります。

- Router は objc ランタイム (`objc_msgSend`, `objc_allocateClassPair`, `class_addMethod`) を P/Invoke し、
  `application:openURLs:` を実装したデリゲートを `[UnmanagedCallersOnly]` で登録して `NSApplication.run`
  を呼ぶ。必要な呼び出しは十数個で、Microsoft.macOS ワークロードに依存せず `net10.0` のままにできる。
- 受信した URL ごとにルーティングを行う。ピッカー待ちの間に次の URL イベントが届くこともあるため、
  処理中の件数を数え、すべて完了した時点で `terminate:` する。
- `Preferis Picker.app` と `Preferis Settings.app` は独自の bundle id を持ち `CFBundleURLTypes` を宣言しない。
  したがって Launch Services が URL を配送する先は常にメインの Router であり、設定 UI が開いている
  最中でも Router の新しいプロセスが URL を受け取る。
- `LSUIElement=true` にして Router 起動時に Dock アイコンを出さない。
- スパイク 1 で動作確認済み (§12)。NSApplication を使わず `NSAppleEventManager` + CFRunLoop だけで待っても
  Launch Services はイベントを配送しないため、`NSApplication` の初期化 (約 50〜60 ms) は避けられない。
  `LSBackgroundOnly` にしても短縮されなかった。
- 代替: 自前の objc 呼び出しに問題が出た場合は Router の macOS ビルドだけ Microsoft.macOS に切り替える
  (`NSApplicationDelegate.OpenUrls`)。

### 5.4 Windows における URL 受信

レジストリの `shell\open\command` に `"Preferis.exe" "%1"` を登録するため、URL は argv で届き、
URL ごとに新しいプロセスが起動します。単一インスタンス化は不要です。
`.html` 等のファイル関連付けも登録した場合は argv にファイルパスが来るので `file://` URI に変換します。

## 6. ルーティングエンジン

### 6.1 評価規則

1. 有効なルールを上から順に評価する。条件にマッチした最初のルールのアクション列を実行する。
2. アクション列は先頭から順に実行する。
   - **終端アクション** (`open`, `system`, `none`, `reroute`) に到達したらルーティングは終了する。
   - **非終端アクション** (`rewrite`) は現在の URL を書き換えて次のアクションへ進む。
   - 終端アクションに到達せず列の末尾に達した場合は、書き換え後の URL で **次のルールへ評価を続行**
     する (フォールスルー)。「書き換えだけ行うルール」を自然に表現できる。
3. どのルールにもマッチしなければキャッチオールルールのアクション列を実行する。キャッチオールが
   終端アクションに到達せずに終わった場合はフォールスルー先がないため、暗黙の `open` (デフォルトプロファイル)
   を実行する。
4. `reroute` はルール先頭から評価をやり直す。無限ループ防止のため **最大 10 ホップ** とし、
   再ルーティング時の URL が以前に評価を開始した URL と同じなら (ルーティングは URL の決定的関数なので
   必ずループする) エラーとして §8 のフォールバックへ進む。
5. `open` が参照するプロファイル ID のうち存在しないものは警告つきで無視する。残りが空なら
   プロファイル未指定と同じ扱い (デフォルトプロファイル、なければ全プロファイル) になる。
6. 未知の `type` を持つ条件は決してマッチしない。未知の `type` を持つアクションに到達した場合は、
   新しいバージョンの設定が意図した先を推測せず、ルーティング失敗として §8 のフォールバックへ進む。

### 6.2 条件 (Condition)

条件は木構造です。

| type | 種別 | 意味 |
| --- | --- | --- |
| `and` | グループ | 子条件がすべてマッチ |
| `or` | グループ | 子条件のいずれかがマッチ |
| `not` | グループ | 子条件 (1 つ) にマッチしない (追加提案) |
| `host` | リーフ | `Uri.Host` に対して `match` を適用 |
| `url` | リーフ | 正規化済み `Uri.AbsoluteUri` に対して `match` を適用 |

リーフの `match` 種別:

| match | 意味 | 例 (host) | 例 (url) |
| --- | --- | --- | --- |
| `regex` | .NET 正規表現 | `^[^.]+\.example\.(org\|com)$` | `^https?://[^.]+\.example\.(org\|com)/` |
| `wildcard` | `*` = 0 文字以上の任意文字列、`?` = 任意の 1 文字。内部で Regex に変換 | `*.example.com` | `https://example.com/signin/*` |
| `startsWith` | 前方一致 | `www.example` | `https://www.example.com/admin/` |
| `endsWith` | 後方一致 | `.example.com` | `.pdf` |
| `contains` | 部分一致 | `example` | `/admin/` |
| `equals` | 完全一致 (追加提案。ピッカーの「記憶する」が生成する) | `login.microsoftonline.com` | — |

大文字小文字: `host` は常に大文字小文字を区別しない。`url` は既定で区別しない (`ignoreCase: false`
で区別可能)。`wildcard` において `*.example.com` は `example.com` 自体にはマッチしない
(ドットを含めて書いた通りに解釈する)。両方にマッチさせたい場合は `or` で並べるか `endsWith`
を使う。

正規化: 入力 URL は `Uri` でパースし、`Host` は小文字化する。IDN ホストは `Uri.IdnHost`
(punycode) と `Uri.Host` (Unicode) の **両方** に対してマッチを試みる。

### 6.3 アクション (Action)

| type | 終端 | 内容 |
| --- | --- | --- |
| `open` | ○ | `profiles` に列挙したプロファイルを開く。空ならデフォルトプロファイル。1 件なら即起動、複数ならピッカーを表示 |
| `system` | ○ | OS の既定ハンドラーへ委譲 (Windows: `ShellExecute`, macOS: `NSWorkspace.openURL` / `open`)。**既定ハンドラーが Preferis 自身なら無限ループになるため、事前に確認して §8 フォールバックへ進む** |
| `none` | ○ | 何もしない (ブロック) |
| `rewrite` | × | URL を書き換える。`mode: regex` (pattern + replacement) または `mode: queryParameter` (指定クエリパラメーターの値を URL デコードして新しい URL にする。Outlook Safe Links 展開などに使う) |
| `reroute` | ○ | 現在の URL でルール先頭から再評価 |

### 6.4 キャッチオールルール

- 常に存在し削除・並べ替え不可。条件は「常にマッチ」。アクション列のみ編集可能。
- 既定のアクションは `open` (プロファイル指定なし = デフォルトプロファイル)。
- デフォルトプロファイルが未設定の場合は、検出済みブラウザーすべてを候補にしたピッカーを表示する
  (初回起動時の体験)。

### 6.5 Core の主要型 (案)

```
RoutingConfig        { Version, DefaultProfileId, Profiles[], Rules[], CatchAll, Settings }
Profile              { Id, Name, Target }     Target = BrowserTarget | CommandTarget
BrowserTarget        { Browser (string | per-OS map), BrowserProfile?, Private, ExtraArgs[] }
CommandTarget        { Windows?: CommandSpec, MacOS?: CommandSpec }
Rule                 { Id, Name, Enabled, Condition, Actions[] }
Condition            = AndCondition | OrCondition | NotCondition | HostCondition | UrlCondition
RoutingAction        = OpenAction | SystemAction | NoneAction | RewriteAction | RerouteAction
Router               Route(Uri) -> RoutingDecision
RoutingDecision      = Open(Uri, ProfileId[]) | System(Uri) | None | Failed(reason)
ProfileResolver      Resolve(Profile, IBrowserDetector) -> LaunchPlan | ResolveFailure
LaunchPlan           { ExecutablePath, Arguments[], Kind }   // Platform 層が実行する
PickerRequest        { Url, Candidates[], Options }           // §9.3
PickerResponse       { Result, ProfileId?, Remember }
```

`Router` と `ProfileResolver` は純粋関数的に設計し、OS 依存部分はインターフェース経由で注入します。

## 7. プロファイルとブラウザー解決

### 7.1 マシン非依存な参照

設定ファイルを OS 間・マシン間で同期するため、プロファイルは実行ファイルパスを持ちません。
代わりに **ブラウザーカタログ上の安定 ID** (`edge`, `chrome`, `firefox`, `safari`, `brave`, `vivaldi`,
`opera`, `arc` ...) で参照し、実行時に Platform 層がそのマシンでの実体 (パス / バンドル ID) に解決します。
ID はカタログに定義された既知の値に限り、任意の文字列は指定できません。

- Edge / Chrome / Firefox のリリースチャネル (Stable / Beta / Dev / Canary) はそれぞれ別の ID
  (`edge`, `edge-beta`, `edge-dev`, `edge-canary` ...) として扱う。チャネルごとにインストール先と
  ユーザーデータディレクトリが異なり、ブラウザープロファイルも別物だからである。解決は完全一致で、
  チャネル間の暗黙のフォールバックは行わない (Configuration.md §2.4)。
- ブラウザー ID は OS ごとに別の値も指定できる (`"browser": { "windows": "edge", "macos": "safari" }`)。
- カタログ外のブラウザーは検出結果から `win:<StartMenuInternet キー名>` / `mac:<bundle id>` 形式の
  ID で参照できる (本質的にマシン依存なので OS ごとの指定と組み合わせる)。
- 任意の実行ファイルは `command` で OS ごとにパスと引数を指定する。パスでは環境変数 (`%LOCALAPPDATA%`,
  `$HOME`, `~`) を展開する。引数の `{url}` が URL に置換される (引数は配列で渡し、文字列連結しない)。
- 同期しないマシン固有の上書き (ブラウザーのインストール先が特殊など) は `local.json` の
  `browserOverrides` に書く。

### 7.2 ブラウザープロファイルの参照

Chromium 系のプロファイルディレクトリ名 (`Default`, `Profile 1`) はマシンごとに異なり得るため、
設定には **複数の識別子を保存**し、解決時に優先順位をつけて照合します。

| 優先 | 識別子 | 由来 (Chromium `Local State` の `profile.info_cache`) |
| --- | --- | --- |
| 1 | `email` | `user_name` |
| 2 | `name` | `name` (なければ `gaia_name`) |
| 3 | `directory` | キー名 (`Default`, `Profile 1` ...) |

設定 UI で検出結果から選ぶと 3 つすべてが保存されます。どの識別子でも照合できない場合、
あるいはプロファイルデータが読めない場合 (macOS 27 のアクセス制限、§Platform 参照) は
`directory` をそのまま `--profile-directory` に渡します。Firefox は `profiles.ini` の `Name` を使います。

### 7.3 解決失敗時

参照先ブラウザーがそのマシンに無い場合 (例: Windows の設定を Mac で使ったとき Edge が無い):

1. プロファイルの `fallback` に別プロファイル ID があればそれを使う
2. なければ §8 のフォールバックへ

## 8. フォールバックとエラー処理

原則: **URL を黙って捨てない。** 高速経路のどの段階で失敗しても、次の順でリカバリーします。

1. 設定ファイルが無い / 壊れている → 検出済みブラウザーすべてを候補としたピッカー (エラー理由を表示)
2. プロファイル解決に失敗 → `fallback` → デフォルトプロファイル → ピッカー
3. 起動 API が失敗 → ピッカー (失敗したプロファイルに印をつける)
4. `system` が自分自身に戻ってくる → デフォルトプロファイル → ピッカー
5. ピッカーが起動できない、異常終了した、または応答が解釈できない → デフォルトプロファイル →
   自分以外の既定ハンドラー → OS 標準のメッセージボックスに URL を表示 (Windows: `MessageBoxW`,
   macOS: `NSAlert`。いずれも UI フレームワーク不要)

ログ:

- 既定はエラーのみ。ログの保存先は OS のローカルログディレクトリ (同期対象には置かない)。
- 「履歴」(直近 N 件の URL とマッチしたルール) は設定 UI でルールを調整するために有用だが、
  URL は機微情報なのでオプトインとし、ローカルにのみ保存する。

## 9. UI

### 9.1 設定 UI (`Preferis.Settings`)

Avalonia (Fluent テーマ) のシングルウィンドウ、左にナビゲーション、右にコンテンツ。

| ページ | 内容 |
| --- | --- |
| プロファイル | プロファイル一覧 / 追加・編集・削除 / デフォルト指定。「検出されたブラウザー」パネルから 1 クリックでプロファイル化。ブラウザープロファイルは検出結果のドロップダウンから選択。プライベートモード、追加引数、OS 別上書き、フォールバック |
| ルール | 順序付きリスト (ドラッグ並べ替え、有効/無効トグル、複製)。ルールエディターは条件ツリー (グループ追加、リーフの対象/一致種別/パターン入力) とアクション列エディター。末尾にキャッチオールルール (固定) |
| テスト | URL を入力すると、評価の過程 (どのルールがどう判定され、どの URL に書き換わり、どのプロファイルに解決されたか) を表示する。`--dry-run` と同じエンジンを使う |
| 一般 | 設定ファイルの場所 (「クラウドストレージへ移動」)、OS への登録状態と「既定のブラウザーに設定」ボタン、ピッカーの動作、ログと履歴の有無、言語 |

設定 UI は起動速度の制約を受けないため、全ブラウザーと全ブラウザープロファイルの列挙、
アイコンの読み込みはここで行います。OS への登録は Router の `--register` を呼び出して行います
(登録情報が Router の実行ファイルパスを指すため)。

### 9.2 ピッカー UI (`Preferis.Picker`)

Windows の「アプリを選択」ダイアログに近い小さなダイアログ。Windows は WinUI 3、macOS は AppKit で
実装し、OS 標準の外観に合わせます。

- 表示内容: 開こうとしている URL (ホストを強調、長い場合は省略)、候補プロファイルの一覧
  (ブラウザーアイコン + プロファイルアバター + 名前 + ブラウザープロファイル名)。アイコンと
  アバターはリクエストに含まれる識別子からピッカー自身が Platform 層で解決する。
- 操作: ↑↓ で選択、Enter で決定、Esc でキャンセル (= `none`)、`1`〜`9` で直接選択、ダブルクリック。
- 「このホストには今後この選択を使う」チェックボックス。オンにすると応答の `remember` が `true` になり、
  **Router が** `host equals <host>` → `open <選択プロファイル>` のルールを生成して、ピッカーを出した
  ルールの直前に挿入する (末尾に追加するとピッカーを出した元ルールが先にマッチしてしまうため)。
  設定ファイルへの書き込みはピッカーでは行わない。
- 「URL をコピー」ボタン。
- 既定で最前面、マウスカーソル位置付近またはスクリーン中央に表示 (設定で選択)。
- 任意: N 秒無操作で既定候補を自動選択。
- 別プロセスであるため、Router がフォアグラウンド権限を移譲する (Windows: `AllowSetForegroundWindow`、
  macOS: アクティベーションの譲渡)。詳細は Platform.md。

### 9.3 ピッカー IPC プロトコル

Router がピッカーを子プロセスとして起動し、標準入力にリクエスト JSON を 1 件書いて閉じ、
標準出力から応答 JSON を 1 件読みます。シリアライズは Core の JSON ソース生成コンテキストを共有します。

リクエスト:

```json
{
  "url": "https://login.microsoftonline.com/...",
  "candidates": [
    { "profileId": "edge", "name": "Microsoft Edge", "browser": "edge" },
    { "profileId": "chrome-work", "name": "Chrome (Work)", "browser": "chrome",
      "browserProfile": { "directory": "Profile 1", "name": "Work" } },
    { "profileId": "edge-private", "name": "Edge (InPrivate)", "browser": "edge", "private": true }
  ],
  "options": { "position": "cursor", "autoSelectSeconds": 0, "rememberChoiceDefault": false },
  "message": null
}
```

- `candidates[].browser` / `browserProfile` はアイコン・アバター解決用。`browserProfile.directory` は
  起動に使うものと同じ値。
- `message` はフォールバック時の説明 (「設定ファイルを読み込めませんでした」など)。通常は `null`。

応答:

```json
{ "result": "selected", "profileId": "chrome-work", "remember": true }
{ "result": "cancelled" }
```

- プロセスの終了コードは応答を書けた場合 `0`。それ以外、または応答が JSON として解釈できない、
  `profileId` が候補に無い、といった場合は Router が §8 の手順 5 でフォールバックする。
- **プロトコルにバージョン番号は持たない。** Router とピッカーは常に同じビルドを同梱して配布するため、
  不整合はフォールバックで吸収すれば十分であり、バージョン管理の手間に見合わない。
- 複数の URL が同時に開かれた場合は Router とピッカーがその数だけ並列に起動する。Windows の
  「アプリを選択」と同じ挙動であり、初期リリースでは直列化しない。

## 10. OS への登録と検出 (概要)

詳細は [Platform.md](Platform.md) を参照。

- **Windows**: HKCU 配下の `Software\Clients\StartMenuInternet\Preferis` (Capabilities、URLAssociations
  http/https) と `Software\RegisteredApplications`、ProgId `PreferisURL` の `shell\open\command` を
  `Preferis.exe` に向けて書く。既定ブラウザーの切り替え自体はユーザー操作が必要なので
  `ms-settings:defaultapps` を開く。実行ファイルの場所が変わったら設定 UI 起動時に検出して再登録を促す。
- **macOS**: `Preferis.app` の `Info.plist` に `CFBundleURLTypes` (http, https) と `CFBundleDocumentTypes`
  (public.html 等) を宣言。ピッカーと設定 UI は `Contents/Helpers/` 配下のネストしたバンドルに置く。
  `NSWorkspace.setDefaultApplication(at:toOpenURLsWithScheme:)` (macOS 12+) で既定化を要求する
  (OS が確認ダイアログを出す)。配布には Developer ID 署名と公証が必要。
- **ブラウザー検出**: Windows はレジストリ `StartMenuInternet`、macOS は Launch Services
  (`NSWorkspace.urlsForApplications(toOpen:)`) を一次情報源とし、カタログで既知ブラウザーに紐づける。
- **ブラウザープロファイル列挙**: Chromium 系は `Local State`、Firefox は `profiles.ini`。

## 11. テスト戦略

Guidelines に従い TDD (Red → Green) で進めます。

- **Core**: 条件マッチャー (各 match × host/url、IDN、大文字小文字)、ルーティング (先頭一致、
  フォールスルー、rewrite → reroute、ループガード、キャッチオール)、設定の往復シリアライズと
  未知プロパティ保持、プロファイル解決の優先順位、ピッカーのリクエスト/応答の往復シリアライズ。
- **Platform**: `Local State` / `profiles.ini` のフィクスチャ解析。レジストリと Launch Services は
  薄いインターフェースで包み、フェイクで検出ロジックを検証。`LaunchPlan` 生成 (引数の組み立て) を
  OS ごとに検証。`IPickerLauncher` はフェイクで差し替え、Router のフォールバック順序を検証。
- **Settings**: ViewModel テスト。
- **Picker**: `--request <path>` で起動する手動確認を基本とし、ロジック (キー操作による選択、
  自動選択タイマー) は UI から切り離した状態機械としてテストする。
- **E2E / 手動**: OS ごとのチェックリスト (既定ブラウザー登録、URL 受信、プロファイル指定起動、
  既に起動中のブラウザーへの受け渡し、ピッカーのフォーカス) と `tools/bench` による起動時間計測。

## 12. 設計上の未決事項とスパイク

実装前に短いスパイクで確認する項目。macOS を先に完成させる方針 (§14) に合わせ、macOS の項目を先に行う。

macOS:

1. **Router の URL 受信** — objc 直接呼び出しで `application:openURLs:` を受け取る最小実装と、
   プロセス起動から受信までの時間。問題があれば Microsoft.macOS 版と比較する。
   **結果 (2026-10-01, `spikes/UrlActivation`)**: 成功。NativeAOT + objc 直接呼び出しで受信できる。ウォームで
   プロセス起動 → 受信 150〜220 ms、うち LS の床 35〜65 ms (C の最小アプリで計測)、AppKit ロード 15 ms、
   `sharedApplication` 50〜60 ms、配送 20 ms。再 publish 後の初回は 600〜900 ms (署名検証と LS 登録)。
   `NSAppleEventManager` のみでは受信できず、`LSBackgroundOnly` でも短縮されない。方式は確定。
2. **ヘルパーバンドルのピッカーへのアクティベーション** — Router から起動した `Preferis Picker.app` の
   ウィンドウがアクティブになるか (macOS 14 以降の協調型アクティベーション)。
   **結果 (2026-10-01, `spikes/PickerHelper`)**: 成功。LS が起動した `LSUIElement` の Router から `posix_spawn`
   相当 (`Process.Start`) で起動したヘルパーが、自身の `activateIgnoringOtherApps:` だけでアクティブかつキー
   ウィンドウになり、最前面アプリもヘルパーになった (macOS 26)。Router 側からの `NSRunningApplication
   activateWithOptions:` による補助は不要だったが、成功を返すので保険として残せる。stdin/stdout の JSON 往復も
   そのまま動作。
3. **起動中ブラウザーへのプロファイル指定受け渡し** — Chrome/Edge の実行ファイルを直接
   `--profile-directory` 付きで起動した時の挙動、Firefox `-P <name>` の挙動。
   **結果 (2026-10-01)**: Chrome / Edge Dev とも成功。起動中に実行ファイルを直接起動すると新プロセスは
   引数を既存インスタンスへ引き渡して終了し (Chrome 183 ms、Edge Dev は未ロードのプロファイルを開いたため 3.1 s)、
   指定したプロファイルのセッションだけが更新された。重複インスタンスは発生しない。Firefox は未検証
   (未インストール)。
4. **既定ブラウザー一覧への表示条件** — `LSUIElement=true` かつヘルパーバンドルを含む構成で
   「デフォルトの Web ブラウザ」に列挙されるか。
   **結果 (2026-10-01)**: 成功。`LSUIElement=true` のスパイクバンドルが `NSWorkspace.urlsForApplications(toOpen:)`
   の https ハンドラー一覧と public.html ハンドラー一覧の両方に Chrome / Edge と並んで列挙され
   (`spikes/UrlActivation/ls-query.swift`)、システム設定「デフォルトの Web ブラウザ」の一覧にも表示されることを
   目視で確認した。一覧の表示名は `CFBundleDisplayName` ではなく `.app` のファイル名が使われる
   (「Preferis Spike」ではなく「UrlActivation」と表示された) ため、製品のバンドル名は `Preferis.app` にする。
   ヘルパーバンドル同梱時の再確認は M2 で行う。
5. **Microsoft.macOS ピッカーの NativeAOT ビルドとバンドル化** — ヘルパーバンドルとして
   `Contents/Helpers/` に配置し、内側から署名して動作すること。
   **結果 (2026-10-01, 部分)**: `net10.0-macos` + `PublishAot=true` でビルドでき、実行ファイル 3.6 MB、バンドル 4.9 MB、
   ad-hoc 署名は自動。ウォームでプロセス起動 → Main 40〜60 ms、→ ウィンドウ表示 190〜235 ms。
   Microsoft.macOS 固有の注意: `.app` は `$(PublishDir)` ではなく `$(OutDir)` にできる、既定で `.pkg` も作る
   (`CreatePackage=false` で抑止)、バンドル名は `ApplicationTitle` に従う。`Contents/Helpers/` へのネスト配置と
   内側からの署名は Router のバンドル組み立て (M2) で確認する。

Windows:

6. **子プロセスのピッカーへのフォーカス移譲** — `AllowSetForegroundWindow` によりピッカーが前面に来るか。
7. **WinUI 3 ピッカーの表示時間** — NativeAOT + 自己完結モードでプロセス起動から表示までを計測し、
   目標 300 ms を超えるなら Win32 TaskDialog (コマンドリンク + 検証チェックボックス) に切り替える。
8. **HKCU 登録** — 管理者権限なしで「既定のアプリ」一覧に表示されること。
9. **起動中ブラウザーへのプロファイル指定受け渡し (Windows)** — 3 と同じ確認を Windows で行う。

ユーザー判断が必要な事項:

- 対応言語 (日本語 / 英語の両方を想定)
- 配布形態 (Windows: zip / winget、macOS: zip / dmg / Homebrew cask) と Apple Developer ID の有無
- Windows で `.html` 等のファイル関連付けも登録するか (既定は URL のみ)
- 送信元アプリケーション (どのアプリが URL を開いたか) を条件に加えるか (将来拡張)
- 設定 UI を将来 WinUI 3 / AppKit でネイティブ化するか (当面は Avalonia)

## 13. 決定事項の記録

| 日付 | 決定 | 理由 |
| --- | --- | --- |
| 2026-10-01 | Router と UI (ピッカー、設定) を別実行ファイルに分ける | Router から UI フレームワークを完全に排除する。OS 固有 UI のビルド制約を閉じ込める |
| 2026-10-01 | ピッカーは WinUI 3 / AppKit のネイティブ UI | OS 標準の外観。使用頻度が低いため初期化コストは許容 |
| 2026-10-01 | ピッカー IPC は stdin/stdout の JSON、バージョン番号なし | 同梱配布のため不整合はフォールバックで吸収する。バージョン管理は使用・実装を煩雑にする |
| 2026-10-01 | ブラウザー起動と設定ファイル書き込みは Router に集約 | 副作用を 1 か所にまとめる。ピッカーは純粋な UI |
| 2026-10-01 | `.app` / `.exe` 一式を作るビルドは各 OS の最初のマイルストーン (M2 / M4) に含める。M5 に残すのは Developer ID 署名、公証、インストーラー、ドキュメント整備のみ | ローカル・内部テストで常に最終形の成果物を使えるようにする。配布形態だけがリリース段階の関心事 |
| 2026-10-01 | macOS で先に機能を完成させ、Windows 対応はその後に行う | 開発環境が macOS であり、Apple Event 受信やヘルパーバンドルなど macOS 固有のリスクを早期に潰す。Core と設定 UI は OS 非依存なので Windows 対応は Platform と ピッカーの追加で済む |
| 2026-10-01 | ブラウザー ID はカタログの既知 ID のみ。チャネル (Stable / Beta / Dev / Canary) は別 ID とし、同系統の別チャネルで開くフォールバックは設けない | チャネルごとにプロファイルが別物であり、暗黙に別チャネルで開くと意図から外れる。マシン差は OS ごとの指定と `fallback` で明示する |

## 14. マイルストーンと現在の状態

macOS で一通りの機能を完成させてから Windows 対応に進みます。Core と設定 UI は OS 非依存に作るため、
Windows 対応で追加するのは Platform の Windows 実装、Router の Windows 経路、WinUI 3 ピッカーです。
`.app` / `.exe` 一式を生成するビルド (§4.4) は各 OS の最初のマイルストーンに含め、以降のマイルストーンでは
常にその成果物でテストします。M5 はリリース段階の作業 (署名・公証・インストーラー・ドキュメント) だけです。

| M | 内容 | 完了条件 | 状態 |
| --- | --- | --- | --- |
| M0 | スパイク (§12 の macOS 項目 1〜5) と計測方法の確立 | macOS 起動経路とピッカー構成の方式が確定している | 完了 (2026-10-01)。残件はヘルパーのネスト配置時の再確認で、M2 で行う。`tools/bench` は測る対象の Router ができる M2 で作る |
| M1 | Core: 設定モデル、JSON、条件、ルーティング、プロファイル解決、IPC メッセージ、`--dry-run` | ユニットテストで §6〜§7 の仕様を網羅。`--dry-run` で設定ファイルの評価結果を確認できる | 完了 (2026-10-01)。`Preferis.Core` + 111 テスト、`Preferis.Router` は `--dry-run` のみ。NativeAOT publish 確認済み |
| M2 | macOS Platform + Router 高速経路: Launch Services 検出、プロファイル列挙、LaunchPlan 実行、既定ブラウザー登録、Apple Event 受信。**`PublishMacOS` ターゲットで ad-hoc 署名済みの `Preferis.app` 一式を生成** | `artifacts/` の `Preferis.app` を既定ブラウザーにし、単一候補のルールで日常利用できる | 未着手 |
| M3 | macOS ピッカー (AppKit, ヘルパーバンドル) + 設定 UI (Avalonia, macOS で検証)。両者を `Preferis.app` に同梱 | macOS で全機能が揃い、設定ファイルを手で編集せずに運用できる | 未着手 |
| M4 | Windows: スパイク (§12 の 6〜9)、Platform の Windows 実装、Router の Windows 経路、WinUI 3 ピッカー、設定 UI の Windows 検証。**`PublishWindows` ターゲットで exe 一式のフォルダーを生成** | `artifacts/` の `Preferis.exe` を既定ブラウザーにし、M3 と同等の機能が揃う。同一の設定ファイルを両 OS で共有できる | 未着手 |
| M5 | リリース: Developer ID 署名と公証、インストーラー (macOS: dmg / Homebrew cask、Windows: zip / winget 等)、ベンチ結果の記録、利用者向けドキュメント | 配布物を第三者が導入できる | 未着手 |

本ドキュメントは設計段階のものです。実装と齟齬が出た場合は実装側の判断を本書に反映してください。
