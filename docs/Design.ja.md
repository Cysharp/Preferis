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
| ピッカー (Windows) | WinUI 3 (Windows App SDK 2.x の WinUI コンポーネント `Microsoft.WindowsAppSDK.WinUI`) | Windows 11 ネイティブの外観。NativeAOT 対応。M4 で計測した表示時間 (約 250 ms) が目標内のため TaskDialog への切り替えは不要 (§12 スパイク 7) |
| ピッカー (macOS) | AppKit (Microsoft.macOS, `net10.0-macos`) | OS 標準の外観。NativeAOT は .NET 9 以降正式サポート |
| 設定 UI | macOS: AppKit (Microsoft.macOS)、Windows: WinUI 3。ViewModel は共通 (`Preferis.Settings.Core`) | OS 標準の外観にするため画面は OS ごとに作り、ロジックとテストは 1 つにする。M3 では Avalonia 12 で 1 つの実装にしていたが、macOS のアプリとして見た目が不自然なため置き換えた (§13) |
| MVVM | CommunityToolkit.Mvvm 8.x (MIT) | ソースジェネレーターベースで AOT 安全。画面は AppKit も WinUI 3 もコードで組み、変更通知を受けて手で反映する (`Bindings`)。WinUI 3 で {Binding} (リフレクション) や x:Bind を使わないのは、NativeAOT で型情報が削られて実行時に失敗するのを避け、AppKit 版と同じ構造に保つため (§13) |
| 設定・IPC | System.Text.Json (ソース生成, `JsonPolymorphic`) | リフレクション不要、AOT 安全。Router とピッカー間の IPC にも同じコンテキストを使う |
| 正規表現 | `System.Text.RegularExpressions` (バックトラック版を短いタイムアウトで先に試し、超えたら `NonBacktracking`) | 構築が安く (`NonBacktracking` は 1 つ約 0.8 ms)、ユーザー入力パターンによる破滅的バックトラックも防ぐ。AOT で動作 |
| テスト | xunit (Apache 2.0) | FluentAssertions v8 は商用ライセンスのため使わない。必要なら Shouldly (BSD) |

依存ライブラリは Guidelines のライセンス条件 (MIT/Apache 2.0/BSD/ISC/MPL/PD) を満たすものに限ります。
Windows App SDK と Microsoft.macOS は Microsoft 製の SDK であり、Guidelines の Windows SDK と同じ扱いです。
Windows App SDK はソースが MIT ですが、NuGet パッケージ (自己完結モードで同梱するランタイムを含む) のライセンスは
Microsoft Software License Terms です。WinUI コンポーネントが依存する Microsoft.Web.WebView2 も同様に Microsoft 製の SDK です。

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
              │ WinUI 3 / AppKit  │  │ WinUI 3 / AppKit  │
              └───────────────────┘  └───────────────────┘

 ────────────────────── 共有ライブラリ ──────────────────────
  Preferis.Core           設定モデル / ルーティングエンジン / プロファイル解決 / IPC メッセージ
  Preferis.Platform       ブラウザー検出 / プロファイル列挙 / 起動 / OS 登録 / パス (Windows, macOS)
  Preferis.Settings.Core  設定 UI の ViewModel とサービス (UI フレームワーク非依存)
```

### 4.1 実行ファイル構成

UI フレームワークを 1 つも含まない Router と、UI ごとの実行ファイルに分けます。

| 実行ファイル | 役割 | UI |
| --- | --- | --- |
| `Preferis` (`Preferis.exe` / `Preferis.app` のメイン実行ファイル) | URL の受信、ルーティング、プロファイル解決、ブラウザー起動、ピッカーと設定 UI の子プロセス起動、OS 登録 | なし (最終フォールバックの OS メッセージボックスのみ) |
| `Preferis.Picker` (`Preferis.Picker.exe` / `Preferis Picker.app`) | 候補からプロファイルを選ばせる | WinUI 3 / AppKit |
| `Preferis.Settings` (`Preferis.Settings.exe` / `Preferis Settings.app`) | プロファイルとルールの編集 | WinUI 3 / AppKit |

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
Directory.Packages.props      NuGet パッケージのバージョンを一元管理
src/
  Preferis.Core/              設定モデル、JSON シリアライズ、条件マッチャー、ルーティングエンジン、
                              プロファイル解決、ピッカー IPC メッセージ、Router の実行時オーケストレーション
                              (RoutingService とフォールバック連鎖)、Platform 層が実装する抽象
                              (Hosting 名前空間)。OS・UI 非依存。
  Preferis.Platform/          Core の抽象の Windows / macOS 実装: ブラウザー検出、プロファイル列挙、
                              プロセス起動、ピッカー起動、OS 登録、URL 受信。
  Preferis.Router/            Router 実行ファイル (AssemblyName=Preferis)。net10.0。
                              macOS の Apple Event 受信 (objc 直接呼び出し) を含む。
                              アプリアイコンもここに置く: Windows 用の Windows/Preferis.ico とその元絵 Preferis.png、
                              macOS 用の macOS/Preferis.icon (Icon Composer。build/AppIcon.targets がコンパイルする)。
  Preferis.Picker.Windows/    WinUI 3。net10.0-windows10.0.xxxxx。Windows でのみビルド。
  Preferis.Picker.MacOS/      AppKit (Microsoft.macOS)。net10.0-macos。macOS でのみビルド。
  Preferis.Settings.Core/     設定 UI の ViewModel (CommunityToolkit.Mvvm) とサービスの抽象 (ISettingsPlatform,
                              IDialogService)、OS 実装の MacSettingsPlatform / WindowsSettingsPlatform。net10.0。UI フレームワーク非依存。
  Preferis.Settings.MacOS/    設定 UI の AppKit 実装 (Microsoft.macOS)。net10.0-macos。macOS でのみビルド。
  Preferis.Settings.Windows/  設定 UI の WinUI 3 実装。net10.0-windows10.0.19041.0。Windows でのみビルド。
test/
  Preferis.Core.Tests/
  Preferis.Platform.Tests/    フィクスチャ (Local State, profiles.ini) を使った解析テスト、macOS 実機 API の小さな結合テスト
  Preferis.Settings.Core.Tests/ ViewModel テスト。実ファイル (一時ディレクトリ) の ConfigLoader とフェイクの OS / ダイアログで検証
tools/
  bench/                      起動時間計測スクリプト
docs/
```

`Preferis.slnx` はすべてのプロジェクトを含み、OS ごとにソリューションフィルター (`Preferis.MacOS.slnf`,
`Preferis.Windows.slnf`) でビルド可能なものだけを対象にします (`dotnet test Preferis.MacOS.slnf`)。
`Preferis.Windows.slnf` は AppKit の 2 つを除き、WinUI 3 のピッカーと設定 UI を含みます (`dotnet test Preferis.Windows.slnf`)。
これにより Guidelines の「常にビルドできる状態を保つ」を OS ごとに満たします。

NuGet パッケージは [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)
を使い、リポジトリルートの `Directory.Packages.props` の `PackageVersion` でバージョンを一元管理します。
各 `.csproj` はバージョンなしの `PackageReference` と、そのプロジェクト固有の参照メタデータを持ちます。
パッケージの追加時は両方に項目を追加し、バージョン更新時は `Directory.Packages.props` を変更します。
SDK やワークロードが暗黙に参照するパッケージは引き続き SDK 側で管理します。

Platform 層は単一プロジェクトとし、`OperatingSystem.IsWindows()` 等による実行時分岐と
`[SupportedOSPlatform]` 注釈で実装します。

### 4.3 起動モード

| 起動 | 動作 |
| --- | --- |
| `Preferis <url> [<url> ...]` | Router。シェルから直接 URL を渡せる |
| `Preferis --single-argument <url>` | Windows で既定ブラウザーとして登録するコマンド (最速経路)。生のコマンドラインの残り全体を 1 つの URL として扱い、URL 中の引用符や空白でオプションを注入されないようにする (Edge / Chrome と同じ方式、§5.4) |
| `Preferis` (引数なし、Launch Services から起動) | macOS。NSApplication を起動し、Apple Event で届く URL を処理して終了する。URL が届かなければ (Finder や Spotlight から `Preferis.app` 自体を開いた場合) 設定 UI を開いて終了する |
| `Preferis` (引数なし、シェルから) | macOS は使い方を表示。Windows は設定 UI を開く (エクスプローラーやスタートメニューのブラウザー項目から開かれるのはこの形で、GUI プログラムなので使い方を表示しても見えない) |
| `Preferis --settings` | 設定 UI (`Preferis Settings.app` / `Preferis.Settings.exe`) を開く。起動中なら前面に出す |
| `Preferis --status` | OS への登録状態、既定ブラウザー、設定ファイルの場所を表示 |
| `Preferis --dry-run <url> [--config <path>]` | 設定を読み、ルーティングの過程 (トレース) と決定、候補プロファイルを標準出力に表示して終了 (起動しない)。デバッグ・テスト用。終了コードは決定できれば 0、失敗なら 2 |
| `Preferis --register` / `--unregister` | OS への登録/解除 |
| `Preferis.Picker` | 標準入力からリクエスト JSON を読み、標準出力に応答 JSON を書く (§9.3) |
| `Preferis.Picker --request <path>` | リクエストをファイルから読む。単体での動作確認用 |
| `Preferis Settings.app` / `Preferis.Settings.exe` | 設定 UI |

### 4.4 ビルドと成果物

ローカルや内部テストのために、**どのマイルストーンの時点でも `.app` / `.exe` の一式を 1 コマンドで
作れる**ようにします。成果物の形は開発中からリリースと同じで、リリース用のパッケージ (ポータブル zip、インストーラー、
dmg) も同じプロジェクトの `Package*` ターゲットで作ります (2026-10-29)。

```
build/
  Preferis.Build.proj     MSBuild オーケストレーションプロジェクト。追加ツールのインストール不要
dotnet-tools.json          ローカルツール: vpk (Velopack CLI)。PackageWindows が `dotnet tool restore` で入れる
.github/workflows/
  build-release.yaml       ビルド・パッケージ・リリースのワークフロー (下記)
artifacts/                 出力先 (.gitignore 対象)
  osx-arm64/Preferis.app
  osx-arm64/release/       Preferis-<version>-osx-arm64.dmg
  win-x64/Preferis/        Preferis.exe, Preferis.Picker.exe (+ Windows App SDK DLL), Preferis.Settings.exe
  win-x64/release/         Preferis-<version>-win-x64.zip, Preferis-win-x64-Setup.exe, *.nupkg, releases.win-x64.json
  win-arm64/...            win-x64 と同じ構成
```

| コマンド | 内容 |
| --- | --- |
| `dotnet build build/Preferis.Build.proj` | 実行している OS の成果物 (下の 2 つのどちらか)。RID はそのマシンのもの (NativeAOT は OS をまたいでビルドできない) |
| `dotnet build build/Preferis.Build.proj -t:PublishMacOS -p:RuntimeIdentifier=osx-arm64` | Router / Picker.MacOS / Settings.MacOS を NativeAOT で publish し、`Preferis.app` (ヘルパーバンドル込み) を組み立て、ad-hoc 署名する |
| `dotnet build build/Preferis.Build.proj -t:PublishWindows -p:RuntimeIdentifier=win-x64` | Router / Picker.Windows / Settings.Windows を NativeAOT で publish し、3 つの実行ファイルと Windows App SDK の自己完結 DLL を `artifacts/win-x64/Preferis/` にまとめる (M4 で 82 MB、約 50 秒) |
| `dotnet build build/Preferis.Build.proj -t:PackageMacOS -p:RuntimeIdentifier=osx-arm64 -p:Version=1.2.3` | `PublishMacOS` に続けて、`Preferis.app` と `/Applications` へのリンクを入れた dmg を `artifacts/osx-arm64/release/` に作る。`-p:SigningIdentity="Developer ID Application: ..."` で ad-hoc 署名を Developer ID に置き換え (hardened runtime とタイムスタンプ付き)、さらに `-p:Notarize=true -p:NotaryKeyPath=<AuthKey.p8> -p:NotaryKeyId=... -p:NotaryIssuerId=...` (App Store Connect API キー) で dmg を公証してチケットをステープルする (Platform.md §3.1) |
| `dotnet build build/Preferis.Build.proj -t:PackageWindows -p:RuntimeIdentifier=win-x64 -p:Version=1.2.3` | `PublishWindows` に続けて、フォルダーを `Preferis/` ごと zip にし (ポータブル版)、Velopack (`dotnet vpk pack`) でインストーラー `Preferis-win-x64-Setup.exe`、フルパッケージ `Preferis-1.2.3-win-x64-full.nupkg`、更新フィード `releases.win-x64.json` を `artifacts/win-x64/release/` に作る (Platform.md §2.1)。`-p:VelopackRepoUrl=https://github.com/Cysharp/Preferis` を渡すと直前のリリースのフルパッケージを取得して差分パッケージも作る (非公開リポジトリは環境変数 `VPK_TOKEN` にトークン。取れなければ警告して差分なしで続ける) |

- オーケストレーションを MSBuild にするのは、Guidelines の「.NET CLI ツールをグローバルにインストールしない」
  に従い、`dotnet` だけで完結させるためです。シェルや PowerShell のスクリプトは用意しません。
- バンドル組み立て (Info.plist とアプリアイコンの配置、`Contents/Helpers/` へのヘルパー配置、`codesign` 呼び出し) は
  `Preferis.Build.proj` のターゲットとして実装し、Router プロジェクト自体は素の `dotnet publish` で
  成果物を出すだけにします。
- 開発中は `artifacts/` 配下の成果物をそのまま使ってテストします。macOS は `open artifacts/osx-arm64/Preferis.app`
  で Launch Services に登録され、Windows は `artifacts\win-x64\Preferis\Preferis.exe --register` で
  レジストリにそのパスが書かれます。再ビルドしても同じパスに出力されるため再登録は不要です。
- macOS では同じ bundle id のバンドルが複数あると Launch Services の URL 配送先が不定になります。
  `/Applications` に古いコピーを置いたまま `artifacts/` の成果物を試さないこと。必要なら
  `lsregister -u <path>` で古い登録を解除します。
- ユニバーサルバイナリは NativeAOT で作れない既知の問題があるため、RID ごとに別の `.app` を出力します。
- バージョンは `Directory.Build.props` の `Version` (開発中は 0.1.0) で、リリースでは `-p:Version=` で上書きします。
  実行ファイルのバージョン情報と `Preferis --version`、Velopack のパッケージ名、Router の `Info.plist`
  (`CFBundleShortVersionString` / `CFBundleVersion` は `1.2.3-beta.1` の数字部分 `1.2.3`) に入ります。
  `Preferis.Build.proj` は SDK 形式ではないので `Directory.Build.props` を明示的に読み込みます。
- Windows のインストーラーに [Velopack](https://velopack.io/) (MIT) を使うのは、管理者権限なしの `%LocalAppData%` への
  インストール、GitHub Release をそのまま更新フィードにできること、差分更新、.NET の NativeAOT と相性がよいこと
  (`vpk` はパッケージングだけで、アプリ側のライブラリも AOT 互換) のためです。`vpk` はローカルツール
  (`dotnet-tools.json`) として入れ、グローバルにはインストールしません。macOS は Velopack を使わず dmg です。

リリースワークフロー `.github/workflows/build-release.yaml` は `workflow_dispatch` で、タグ (例 `1.2.3`) と dry-run を
入力に取ります。Cysharp/Actions の再利用ワークフロー (`validate-release`、`create-release`) とアクション (`checkout`、
`setup-dotnet`、`upload-artifact`) を使い、ライブラリーの NuGet パッケージを成果物に置き換えた形です。

| ジョブ | 内容 |
| --- | --- |
| `validate-release` | タグの構文と、最新リリースより新しいことの検証。`version` (先頭の `v` を除いたもの) を後続に渡す |
| `build-windows` (matrix: `win-x64`, `win-arm64`) | `windows-2025` (x64)。arm64 は NativeAOT のクロスコンパイル (イメージに ARM64 の C++ ツールが入っている)。x64 で `dotnet test Preferis.Windows.slnf` を実行し、`PackageWindows` の `release/` フォルダーを成果物 `Preferis-<rid>` にアップロードする。差分パッケージのための `vpk download github` には `GITHUB_TOKEN` を渡す |
| `build-macos` | `macos-26` (Apple Silicon、Xcode 26 の `actool`)。`dotnet workload install macos`、`dotnet test Preferis.MacOS.slnf`、`PackageMacOS`。シークレット `APPLE_CERTIFICATE_P12` / `APPLE_CERTIFICATE_PASSWORD` (Developer ID Application 証明書の .p12 の base64 とパスワード) があれば一時キーチェーンに入れて署名し、さらに `APPLE_NOTARY_KEY` / `APPLE_NOTARY_KEY_ID` / `APPLE_NOTARY_ISSUER_ID` (App Store Connect API キーの .p8 の base64、キー ID、発行者 ID) があれば公証する。無ければ ad-hoc 署名のまま |
| `create-release` | 成果物をすべて集め、タグを打ってドラフトリリースを作り、`release/` の全ファイルを添付する。dry-run では作ったものを最後に消す。公開 (ドラフトの解除) はリリースノートを確認した人が GitHub 上で行う |

- 実行ファイルの NativeAOT publish は `-p:Version` だけで済むため、`update-packagejson` でバージョンをコミットする
  MagicOnion 方式は採らず、NativeCompressions と同じく実行中のコミットにタグを打ちます。
- Velopack の更新フィードは「最新の (ドラフトでない) リリースの `releases.<channel>.json`」なので、ドラフトを公開した時点で
  利用者に更新が見えます。アプリ内からの更新チェックはまだ組み込んでいません (Platform.md §2.1)。
- Windows の実行ファイルとインストーラーはコード署名していません (SmartScreen の警告が出る)。`VelopackPackArgs`
  プロパティで `vpk pack` に `--azureTrustedSignFile` などを渡せるようにしてあります。

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
- **照合器は評価対象になった条件だけ、安く作る。** Router は URL ごとのプロセスなので、条件ごとの照合器は URL のたびに
  作り直される (プロセス内のキャッシュは効かない)。ワイルドカードは Regex を使わずに照合する (`*` と `?` 以外は文字どおり。
  最後の `*` だけをやり直す貪欲法で、割り当てなし)。正規表現はまずバックトラック版 (構築 4〜8 µs) を 10 ms のタイムアウトで
  試し、超えた時だけ `RegexOptions.NonBacktracking` (構築約 0.8 ms、0.7 MiB。入力に線形の時間) で評価し直す。一致の有無は
  どちらのエンジンでも同じなので、破滅的バックトラックを起こすパターンでも正しい結果が上限のある時間で返る。
  `NonBacktracking` が対応しない構文 (後方参照・先読み) はバックトラック版 + 100 ms タイムアウトのまま (超えたら不一致)。
  以前は全条件で `NonBacktracking` を構築しており、CSV で取り込んだ 1,000 件のワイルドカードのどれにも一致しない URL で
  約 0.6 秒、約 1 GB かかった (Platform.md §5)。
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
| プロセス起動 → ピッカー表示 | 300 ms 以下 (WinUI 3 がこれを超えるなら TaskDialog)。M4 で計測: Router のプロセス起動 → ピッカー表示 約 270 ms、うちピッカーのプロセス起動 → 表示 約 250 ms (ウォーム、Platform.md §5) | 350 ms 以下 (URL 受信 200 ms + ヘルパー起動)。M3 で計測: Router が起動したヘルパーのプロセス起動 → ウィンドウ表示が 120 ms (ウォーム、Platform.md §5) |

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

レジストリの `shell\open\command` に `"Preferis.exe" --single-argument %1` を登録するため、URL はコマンドラインで届き、
URL ごとに新しいプロセスが起動します。単一インスタンス化は不要です。

- `"%1"` と引用符で囲む一般的な登録では、`"` を含む細工した URL が引用符を閉じて `--config` などのオプションを
  足せてしまう。Edge / Chrome と同じく、`--single-argument` の後ろは生のコマンドライン (`GetCommandLineW`) の残り全体を
  1 つの URL として扱い、C ランタイムの引数分割を通さない (`Platform.Windows.CommandLine`)。
- `Preferis.exe` は GUI サブシステム (`WinExe`) のプログラムにする。コンソールプログラムだとリンクをクリックするたびに
  コンソールウィンドウが一瞬開く。コマンド (`--status` など) はターミナルから起動された場合だけ親のコンソールに接続して
  出力する (`AttachConsole`)。GUI プログラムなので cmd / PowerShell は終了を待たず、出力はプロンプトの後に出る。待たせたい
  場合は `Preferis.exe --status | Out-Host` のようにパイプする。

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
7. 開く URL は決定時点の (書き換え後の) URL である。ピッカーへの表示、「記憶する」で作るルールのホスト、
   フォールバック先で開く URL もすべてこれを使う。受け取ったままの URL は履歴にだけ残る。
8. 決定を下したルール (終端アクションを実行したルール) を結果として返す。書き換えだけで素通りしたルールではなく、
   キャッチオールが決めた場合はなし。履歴とテストページはこれを表示する。
9. ルール自身の `rewrite` で URL が変わった後に、ピッカーを出す `open` (候補が 2 つ以上。キャッチオールの暗黙の `open` を
   含む) に達した場合は、ピッカーを出す前に **そのルールより上のルール** (キャッチオールならすべてのルール) を書き換え後の
   URL で評価する。上のルールは「URL が書き換え後の形で来ていたら」決めていたはずなので、先に決める機会を与える。
   - 評価は通常と同じ。最初に決定したルールに従い (結果のルールもそのルール)、フォールスルーは次のルールへ続き、`reroute` は
     ルール先頭からのやり直しになる (ホップ数とループの制限も通常どおり)。ピッカーを出そうとしたルール自身は評価しない。
     上のルールが同じ状況になれば、さらにその上のルールだけを評価する。
   - どれも決定しなければ、そのルールが書き換えた URL でピッカーを出す。上のルールが書き換えだけ行ってフォールスルーしても、
     その書き換えはピッカーの URL に使わない。
   - 「記憶する」のルールはピッカーを出したルールの直前 (キャッチオールなら末尾) に入るため (§9.2)、この評価で書き換え後の
     URL に一致する。これが無いと書き換え前の URL でしか評価されず、記憶しても毎回ピッカーが出る。
   - 次の場合は行わない: 候補が 1 つ以下の `open`。ルール自身の書き換えで URL が変わっていない場合 (前のルールの書き換えで
     フォールスルーしてきた場合を含む。直前に入る記憶のルールは既にその URL で評価されている)。`rewrite` + `reroute`
     (やり直した評価がすべてのルールを書き換え後の URL で評価する)。
   - テストページと `--dry-run` の手順には「再評価」(`Reevaluated`) として、評価を始めたことと、どれも決定しなかったことを出す。

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
| `wildcard` | `*` = 0 文字以上の任意文字列、`?` = 任意の 1 文字。Regex を使わずに照合する (§5.2) | `*.example.com` | `https://example.com/signin/*` |
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
| `open` | ○ | `profiles` に列挙したプロファイルを開く。空ならデフォルトプロファイル。1 件なら即起動、複数ならピッカーを表示 (候補はプロファイル一覧の順に並べ、`profiles` の順序は使わない) |
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
PickerSession        ピッカーの振る舞い (選択、キー操作、記憶、自動選択タイマー)。UI 非依存で各 OS のピッカーが共有 (§9.2)
PickerDisplay        URL のホスト強調用の分割、候補の 2 行目 (ブラウザープロファイル名・アカウント・プライベート)
BrowserProfileMatcher 保存済みの BrowserProfileRef と検出したブラウザープロファイルの照合 (§7.2)。解決・ピッカー・設定 UI で共通
ConfigValidator      設定の静的検査 (設定 UI の警告表示用)。保存は妨げない
PatternErrorCache    パターンの検査結果を検証の間で覚える。設定 UI は編集のたびに検証するため (§9.1)
ConfigStructure      読み込み時の構造検査 (配列の要素・辞書の値の null)。不正なら JSON の構文エラーと同じく読み込みエラー (Configuration.md §4.1)
IdGenerator          ルールの ID (slug) とプロファイルの連番 ID の生成 (Configuration.md §2.10)
RoutingHistory       オプトインのルーティング履歴 (§8)
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

解決可否と `fallback` の走査は Core の `ProfileTargetResolver` に集約する。URL に依存しない
解決済みターゲットまたは型付きの失敗理由を返し、`ProfileResolver` はそこから URL を含む起動計画を作る。
設定画面の `ProfileAvailability` も同じ処理を使い、ブラウザーの検出・プロファイル列挙には起動時の検出結果を
渡す。編集のたびに OS を問い合わせず、画面の警告と Router の解決規則を一致させる。

### 7.4 マシンごとのターゲット

同じ設定を使う複数のマシンで、同じ役割のプロファイルに別のものを開かせたいことがある: Windows では Edge、Mac では Safari。
両方 Edge でも、マシンごとにアカウントも名前も違うブラウザープロファイル。OS ごとの指定 (§7.1) は OS 単位なので同じ OS の
2 台を区別できず、ブラウザープロファイルの照合 (§7.2) は同じアカウントか名前でなければ当たらない。

プロファイルを「役割」(Personal、Work) として扱い、マシンごとにターゲットを置き換えられるようにする (Configuration.md §3.1)。

- `config.json` のプロファイルは今までどおり共有のターゲットを持つ。置き換えはそのマシンの `local.json` の `profileTargets`
  に置き、同期しない。置き換えはターゲット全体 (ブラウザー、ブラウザープロファイル、プライベート、追加引数、コマンド)。
- 解決順は、このマシンの置き換え → OS ごとの指定 → 共通の指定 → `fallback`。Router はプロファイルを解決する前に置き換えを
  当てた設定 (実効設定) を作り、ルーティング、プロファイル解決、ピッカーのリクエストはそれを使う。「記憶する」が書き込む
  `config.json` には置き換えは入らない (保存は読み直した元の設定に対して行う)。
- 別の概念 (エイリアスと実体の 2 階層) にしないのは、置き換えが無ければ今までと全く同じで、1 台だけで使う人には何も
  増えないため。2 台目以降で共有のターゲットが解決できない時に初めて、設定 UI がそのプロファイルに警告
  (「Google Chrome is not installed on this computer; “Microsoft Edge” is used instead.」) を出し、置き換えを作る
  きっかけになる。
- 共有のターゲットを持たない「コンピューターごとに設定」のプロファイルも作れる (`config.json` では `browser` も `command` も
  無いプロファイル)。開くものは各マシンの置き換えだけが決め、置き換えの無いマシンでは解決できない扱いで `fallback` →
  既定のプロファイル → ピッカーに進む。特定のマシンでだけ意味を持つ役割 (社内ネットワーク用の Work など) のためのもので、
  ルールは役割を参照したまま、使うマシンだけが開くものを決める。置き換えの無いマシンで「何もしない」(URL を捨てる)
  にはしない (§1 の「URL を黙って捨てない」)。そのマシンの設定 UI は「このコンピューターでは未設定」と警告し、置き換えを
  作るきっかけになる。
- プロファイル ID は `profile-1`, `profile-2` ... の連番にする (Configuration.md §2.10)。名前から付けると、検出したブラウザーから
  作った最初の名前 (`microsoft-edge`) が名前を変えても残り、マシンごとのターゲットでは別のブラウザーを開くこともあって
  誤解を招くため。ID は編集画面の Advanced で役割の名前 (`work`) に変えられる (Configuration.md §2.11)。他のマシンの置き換えは古い ID の
  キーのままなので、`previousIds` で引き当てる。人が見分けるのは名前なので、名前はプロファイルの間で一意にする (検証の
  エラー)。ログ、Router のトレース、テストページは ID の横に名前も出す (`'Chrome (Work)' (profile-3)`)。

## 8. フォールバックとエラー処理

原則: **URL を黙って捨てない。** 高速経路のどの段階で失敗しても、次の順でリカバリーします。

1. 設定ファイルが無い / 壊れている (JSON として読めない、または値が必要な場所に `null` があるなど構造が不正。
   Configuration.md §4.1) → 検出済みブラウザーすべてを候補としたピッカー (エラー理由を表示)
2. プロファイル解決に失敗 → `fallback` → デフォルトプロファイル → ピッカー
3. 起動 API が失敗 → ピッカー (失敗したプロファイルに印をつける)
4. `system` が自分自身に戻ってくる → デフォルトプロファイル → ピッカー
5. ピッカーが起動できない、異常終了した、または応答が解釈できない → デフォルトプロファイル →
   自分以外の既定ハンドラー → OS 標準のメッセージボックスに URL を表示 (Windows: `MessageBoxW`,
   macOS: `NSAlert`。いずれも UI フレームワーク不要)

ログ:

- 既定はエラーのみ。ログの保存先は OS のローカルログディレクトリ (同期対象には置かない)。
- 「履歴」(直近 N 件の URL とマッチしたルール) は設定 UI でルールを調整するために有用だが、
  URL は機微情報なのでオプトインとし、ローカルにのみ保存する。`local.json` の `logging.history` が `true` の時だけ
  Router が起動処理の後 (起動を遅らせないため) にログディレクトリの `history.jsonl` へ追記し、`historySize` 件に切り詰める。
  設定 UI のテストページが一覧表示し、クリックでその URL を再テストできる。1 つの Router 内の追記は直列化するが、
  複数の Router プロセスが同時に切り詰めるとまれに 1 件落ちることがある。履歴の用途では許容する。

## 9. UI

### 9.1 設定 UI (`Preferis.Settings`)

シングルウィンドウ、左にナビゲーション、右にコンテンツ。画面は OS ごとにネイティブで作り (macOS: AppKit でシステム設定に倣う、
Windows: WinUI 3 (M4))、ViewModel とサービス (`Preferis.Settings.Core`) を共有する。

ページはこの順に並び、起動時は先頭の「一般」を開く (設定アプリの慣習に合わせ、最初に既定のブラウザーの状態が見える)。

| ページ | 内容 |
| --- | --- |
| 一般 | 設定ファイルの場所 (「クラウドストレージへ移動」)、OS への登録状態と「既定のブラウザーに設定」ボタン、ピッカーの動作、ログと履歴の有無、言語 |
| プロファイル | プロファイル一覧 / 追加・編集・削除 / デフォルト指定。一覧の「+」メニューに並ぶ検出したブラウザーから 1 クリックでプロファイル化。ブラウザープロファイルは検出結果のドロップダウンから選択。プライベートモード、追加引数、OS 別上書き、フォールバック |
| ルール | 順序付きリスト (ドラッグ並べ替え、有効/無効トグル、複製)。ルールエディターは条件ツリー (macOS は NSPredicateEditor) とアクション列エディター。末尾にキャッチオールルール (固定)。「…」メニューの「Import from CSV…」で CSV からルールを作る (Configuration.md §2.12) |
| テスト | URL を入力すると、評価の過程 (どのルールがどう判定され、どの URL に書き換わり、どのプロファイルに解決されたか) を表示する。`--dry-run` と同じエンジンを使う |

設定 UI は起動速度の制約を受けないため、全ブラウザーと全ブラウザープロファイルの列挙、
アイコンの読み込みはここで行います。OS への登録は Router の `--register` を呼び出して行います
(登録情報が Router の実行ファイルパスを指すため)。登録状態の表示は Platform 層を直接使います。

M3 での実装の要点:

- **変更はその場で保存する** (Save / Revert ボタンはない。macOS の設定画面の慣習)。編集は `config.json` / `local.json` の
  モデルをその場で書き換え (未知プロパティを保持するため)、編集が 0.6 秒途切れたら書き込む (`ISaveScheduler`。パターンの入力中に
  キーごとにファイルを書いて同期クライアントを起こさないため)。ウィンドウを閉じる時とテストページの「Open」の前には、待っている
  編集をすぐに書き込む。Router が入力途中のパターンを読むことはあり得るが、検証エラーのある設定でも Router は動く
  (`ConfigValidator` は設定 UI の表示用) ので、途中の状態は「マッチしないルール」になるだけ。
- **保存と画面の責務**: `SettingsSession` が編集モデル、保存済みの内容、競合判定、保存・移動・切替を管理し、
  `SettingsViewModel` はページの生成・選択、検証結果、ユーザー操作を扱う。保存には UI スレッド上で作った JSON の
  スナップショットを渡し、ロック待機とファイル書き込みはバックグラウンドで行う。保存中の入力は次の保存対象として残し、
  終了と「Open」は最新の編集まで保存を待つ。保存中に編集を元に戻した場合も、進行中の書き込みが終わるまでは終了しない。
  保存・移動・切替はセッション内で順序を揃え、遅れて完了する保存が別の設定に書き込むのを防ぐ。
  ファイルの読み直し・切替を待つ間に追加入力があれば、その操作を中止して編集を残す。切替先は先に読み込み・検証し、
  画面を新しいモデルへ切り替えた後で `local.json` の保存先指定を保存する。指定を書けなかった場合も保存待ちの編集として残る。
  その間の再読み込みはセッションの切替先を使い、ディスクに残る旧保存先には戻らない。
- **外部の変更を上書きしない**。Router の「記憶する」や同期クライアントが `config.json` を書き換えることがあるため、
  読み込み時の内容と保存直前・ウィンドウのアクティブ化時・ファイル移動前のディスク上の内容を比較する。`config.json` を
  ここで編集していなければ黙って読み直し、書き込み待ちの編集があればその編集を保留して「Use Theirs / Keep Mine」の帯を出す
  (保留中の編集は決めるまで書かない)。`local.json` だけの編集は衝突しないのでそのまま保存する。壊れた (読めない) `config.json`
  は、編集しても「Replace」を選ぶまで書き換えない (手で書いた内容がまだ必要かもしれないため)。値が必要な場所に `null` が
  ある (構造が不正な、Configuration.md §4.1) `config.json` も読めないファイルとして同じに扱う。保存できないまま閉じる時だけ、
  上書きするか編集を捨てるかを尋ねる。保存直前の比較から書き込みまでは Router の「記憶する」と同じロックの中で行い
  (Configuration.md §1.1)、比較の後に記憶されたルールを黙って上書きしない。ロックが取れない時は書き込めなかった時と同じく、
  編集を保存待ちのまま状態欄に理由を出す (`local.json` だけの保存はロックを待たない)。ファイルの移動も、移動先を尋ねた後に
  ロックの中で比較し直してから書き出し、その時点で読めなくなっていれば移動しない。
  移動先と `local.json` の書き込み後は移動完了として新しい保存先に追従する。旧ファイルの削除だけが失敗した場合は警告を表示し、
  旧保存先への編集を続ける状態には戻さない。
- **一覧の更新**: テストのトレース・候補・履歴は、完成した `IReadOnlyList` を一度に公開する。両 OS の画面はその通知で
  一度だけ表示を更新し、1 行追加するたびに全結果を作り直さない。Windows の編集用 `ListPane` は追加・削除・置換・移動を
  差分で反映し、移動した行のコントロールと購読を再利用する。全再構築はコレクションのリセット時だけ行う。
- **検証**: `ConfigValidator` の結果を、該当するプロファイル・ルールの横 (赤い点) と編集画面、サイドバーの件数に表示する。
  検証は編集のたびに設定全体に対して行う (遅らせないので、表示はいつも今の設定のもの)。ただしパターンの検査結果は
  (比較方法, パターン, 大文字小文字の区別) をキーに `PatternErrorCache` に覚えておき、変わったパターンだけを検査する
  (ルール名の変更などではどのパターンも検査しない)。覚えておくのは直前の検証で使ったパターンだけで、入力途中のパターンは
  たまらない。キャッシュは設定 UI のセッション (`SettingsViewModel`) が持ち、ファイルの読み直しをまたいで使う。パターンの
  検査は Router と同じ照合器の誤りを見る (`PatternMatcher.ErrorOf`。編集画面のパターンの誤りの表示も同じ)。照合器は安く
  作られ (§5.2)、非バックトラッキングのオートマトン (1 つ約 0.8 ms、0.7 MiB) は検査では作られない。`url` の条件は Router と
  同じく `ignoreCase` に従って検査する。
  1,000 条件のルールでも編集 1 回は約 1 ms (Platform.md §5)。
- **プロファイル**: 検出したブラウザーとそのブラウザープロファイルから 1 クリックで作成 (「Add All」で未作成のものを一括)。
  Chromium は `directory` / `name` / `email` をすべて、Firefox は `name` を保存する。最初に作ったプロファイルが既定になる。
  削除時は必ず対象の名前を示して確認する。参照 (既定、フォールバック、`open` アクション) があれば一覧も示し、
  「削除」を選んだ場合だけプロファイルと参照、このコンピューターのターゲットを取り除く。キャンセルした場合は変更しない。
  確認中に設定が再読み込みされた場合も削除を中止する (ルールも同様)。
  コマンドで `.app` を選ぶと `/usr/bin/open -a <app> {url}` に変換する。「+」メニューの「空のプロファイル…」は
  「コンピューターごとに設定」のプロファイル (§7.4) を作る (名前は「新しいプロファイル」)。
- **ルール**: 並べ替えはドラッグか「⋯」メニュー。削除時は対象の名前を示して確認し、「削除」を選んだ場合だけ取り除く。
  キャンセルした場合は変更しない。キャッチオールは一覧の下の別枠に固定し、削除できない。条件は Mail のルールと同じ形の
  エディター (macOS は NSPredicateEditor) で、根は常に「All / Any / None of the following are true」の行になる。
  `ConditionPredicates` が条件木とエディターの木を相互変換する: `not` は「None of」(`not (a or b)`) で表し、
  行が 1 つだけの All / Any は保存時にその行に畳む (単純なルールは単純なまま)。`url` の `ignoreCase: false` は
  「URL (case-sensitive)」という別の対象として表す。空にした条件は「条件なし」(決してマッチしない)。条件には ViewModel を
  持たせず、両 OS のエディターは変更のたびに条件全体を `RuleViewModel.SetCondition` で置き換える (編集の経路を 1 つにし、
  テストも画面と同じ経路を通すため)。`open` は全プロファイルのチェックリストで、
  プロファイル一覧の順に並べ、チェックしたものもその順で保存する。ピッカーの並びもプロファイル一覧の順で、プロファイル画面の
  ドラッグで変える。存在しない ID は「(missing)」として外すまで残す。未知の条件・アクションは
  読み取り専用で表示し、そのまま書き戻す。既知の条件・アクションに新しいバージョンが加えたプロパティは、編集で作り直しても
  引き継ぐ (条件エディターでは行を編集前の条件に内容と位置で対応付ける。種類が変わったものは捨てる。Configuration.md §4)。
- **テスト**: 編集中 (書き込み待ちを含む) の設定でルーティングし、各候補がこのマシンで実際に何を実行するか
  (フォールバックした場合はその理由も) を表示する。「Open」はリンクをクリックした時と同じく Router に URL を渡し
  (`Preferis <url>`)、ピッカーや実際の起動を試せる。Router はファイルを読むため、待っている編集を先に書き込む。
  書き込めない (保留中の) 場合だけ、ディスク上の設定で開くかを尋ねる。Router のログ出力と結果を表示し、ピッカーで「記憶する」を
  選んだ場合はその場で読み直す。
- **言語**: 英語と日本語 (§9.4)。一般ページで変えた言語は、設定 UI には次に開いたときから、ピッカーには次のリンクから
  反映される。

M4 (Windows) での実装の要点 (詳細は Platform.md §2.8):

- ViewModel とその振る舞い (自動保存、外部変更の保留、検証、テストページ) は macOS と共通。画面だけを WinUI 3 で
  Windows 11 の設定アプリに倣って組む: タイトルバーまで広げた Mica、左のナビゲーション、ページ見出し、4 px 間隔のカード。
- ViewModel のうち OS で変わる部分は「このマシンの OS」を基準にした: ブラウザーの選択肢はこのマシンの OS のカタログ
  (Windows では Safari を出さない)、OS ごとの指定ではこのマシンの OS 側を検出結果から、もう一方をカタログから選ぶ
  (`MacOSBrowserChoices` / `WindowsBrowserChoices`)、新しいコマンドプロファイルはこのマシンの OS のコマンドから始まる。
- 条件エディターは NSPredicateEditor に相当するものがないため、同じ `ConditionPredicates` の木を Outlook / Mail の
  ルールと同じ形の行 (All / Any / None の行の下に「対象・比較・パターン」の行、入れ子可) で編集する。パターンの誤りは
  行の下に表示する。
- 設定 UI がテストページの「Open」で Router を実行するとき、出力の終端ではなく Router の終了で区切る。Router が起動した
  ブラウザーが出力を引き継ぐと、ブラウザーが終了するまで終端が来ないため (macOS も同じ。Windows の Router は自分の
  標準ハンドルを子に継承させない)。

マシンごとのターゲットとプロファイル ID の変更 (§7.4) の UI (両 OS):

- プロファイルの編集画面の最後に「Advanced」の節を置き、「ID」の行 (入力欄と「Change」) をそこに置く。誤り (使えない文字、
  重複) は行の説明の代わりに表示し、Change か Enter (Return) を押すまで参照は書き換えない (入力途中の ID でルールを
  書き換えないため)。
- 新しいプロファイルの名前は既にあれば番号を付ける (`Safari 2`、複製は `… copy`、`… copy 2`)。名前を変えて他と重なった
  時は検証のエラーとして編集画面に出る。
- 共有のターゲット (Browser / Command) の後に「This Computer」の節を置き、「このコンピューターでは別のものを使う」の
  スイッチを入れると、このマシンのターゲット (開くもの、ブラウザー、ブラウザープロファイル、プライベート、追加引数、
  プログラムと引数) を編集できる。スイッチを入れた時は、共有のターゲットがこのマシンで指すものを初期値にする。切ると
  置き換えを消す。編集は `local.json` に保存され、外部変更の衝突は起きない。
- 「リンクを開く対象」の 3 つ目の選択肢「コンピューターごとに設定」(§7.4) では、共有のターゲットの節を隠し、「This Computer」の
  節にはスイッチを出さずにこのマシンのターゲットの編集欄を出す (各マシンがそれぞれ設定する旨と、設定していないマシンで
  開くものを節の下に書く)。編集欄は置き換えが無くても出し、最初に選んだ時に `local.json` に置き換えを作る (開いただけでは
  書かない)。既存のプロファイルをこれに切り替えると、このマシンは共有のターゲットが指していたものを置き換えとして持ち続ける。
  逆にブラウザーかコマンドへ戻すと、置き換えは残り (スイッチが入った状態)、まだ何も選んでいなければ消える。ブラウザーに
  した時にブラウザーが未選択なら検出したものの先頭を選ぶ (ブラウザーの無い共有のターゲットは「コンピューターごとに設定」と
  区別できないため)。
- このマシンで解決できないプロファイル (ブラウザーが無い、ブラウザープロファイルが見つからない、このマシンの OS の
  指定やプログラムが無い、コンピューターごとに設定でこのマシンは未設定) は、検証の警告と同じ扱いで一覧の印 (警告だけなら黄色の三角、エラーがあれば赤) と編集画面の
  先頭に出す。代わりに開くもの (使える `fallback`、無ければ既定のプロファイルかピッカー) も書く。置き換えがあればそれを、
  無ければ共有のターゲットを調べる。判定には起動時に検出したブラウザーとブラウザープロファイルを使い、編集のたびに
  検出し直さない。検証がすでにエラーにしているもの (どの OS にもプログラムの無いコマンド) は重ねて出さない。
- 一覧の 2 行目、編集画面の見出し、アイコンは、このマシンで実際に使うもの (置き換えがあればそれ) を表す。置き換えが
  ある時は 2 行目に「This computer:」と付ける。テストページの候補の解決も置き換えを当てた設定で行う。
- プロファイルを削除すると置き換えも消し、複製すると置き換えも複製する。設定 UI を開いた時、`previousIds` で見つかった
  置き換えのキーを今の ID に書き換える。

### 9.2 ピッカー UI (`Preferis.Picker`)

Windows の「アプリを選択」ダイアログに近い小さなダイアログ。Windows は WinUI 3、macOS は AppKit で
実装し、OS 標準の外観に合わせます。macOS では Spotlight のようなタイトルバーのない Liquid Glass のパネル
(macOS 26。それより前は半透明のマテリアル) にしています (Platform.md §3.7)。Windows ではタイトルバーのない
角丸のウィンドウを Windows 11 のダイアログ (ContentDialog) と同じ形にしています: 上が内容、下のフッターにボタン。
ボタンは Windows の慣習どおり主アクションが左 (Open、Cancel の順) で、等幅に並べます (Platform.md §2.6)。

- 表示内容: 開こうとしている URL (ホストを強調、長い場合は省略)、候補プロファイルの一覧
  (ブラウザーアイコン + プロファイルアバター + 名前 + ブラウザープロファイル名)。アイコンと
  アバターはリクエストに含まれる識別子からピッカー自身が Platform 層で解決する。
- 操作: ↑↓ で選択、Enter で決定、Esc でキャンセル (= `none`)、`1`〜`9` で直接選択、ダブルクリック。
- 「このホストには今後この選択を使う」チェックボックス。オンにすると応答の `remember` が `true` になり、
  **Router が** `host equals <host>` → `open <選択プロファイル>` のルールを生成して、ピッカーを出した
  ルールの直前に挿入する (末尾に追加するとピッカーを出した元ルールが先にマッチしてしまうため)。キャッチオールが出した
  ピッカーなら末尾に追加する。`<host>` はピッカーに表示した書き換え後の URL のホスト (§6.1 の 7)。元ルール自身が URL を
  書き換えてからピッカーを出した場合 (`rewrite` + `open`、キャッチオールの `rewrite`)、記憶したルールは最初の評価では書き換え前の
  URL を見るため一致しないが、ピッカーの前に上のルールを書き換え後の URL で評価し直すので (§6.1 の 9)、次回はそこで一致して
  ピッカーを省略し、書き換え後の URL を選択したプロファイルで開く。ただし書き換えでホストが変わらない場合 (`http` → `https`
  など) は、記憶したルールが書き換え前の URL に先に一致し、元ルールの書き換えを通らずにその URL を開く (既知の制限)。
  設定ファイルへの書き込みはピッカーでは行わない。ピッカーは数分開いたままのこともあるため、Router は挿入の直前に
  `config.json` を読み直し、その間に保存された変更を残す。読み直せない (壊れた、場所が変わった) 場合は記憶しない。
  選んだプロファイルの ID もその設定で確かめる。その間に ID が変わっていれば `previousIds` から今の ID を引いて書き
  (Configuration.md §2.11)、削除されていた場合やどのプロファイルか決められない場合は記憶しない。記憶しない場合も
  URL は選んだプロファイルで開く。
  読み直しから書き込みまでは設定 UI の保存と同じロックの中で行い (Configuration.md §1.1)、同時に記憶した別の Router
  (macOS では同じ Router の別の URL) や設定 UI の変更を消さない。ロックが 3 秒取れない時は記憶せずにログに残し、URL は開く。
- 「URL をコピー」ボタン。
- 既定で最前面、マウスカーソル位置付近またはスクリーン中央に表示 (設定で選択)。
- 任意: N 秒無操作で選択中の候補 (初期状態は先頭) を自動選択。キー入力やマウス操作のたびにカウントはやり直し、
  残り秒数を「Open (3)」のように表示する。自動選択は「記憶する」がオンでも記憶しない (誰も選んでいないため)。
- ウィンドウを閉じる操作はキャンセル (`none`) と同じ。候補一覧にフォーカスがある時の ⌘C は URL をコピーする。
- 「記憶する」は、ホストのない URL (`file:`) と、設定がなく検出したブラウザーを並べたフォールバック時には出さない
  (リクエストの `options.allowRemember`)。
- 振る舞い (選択、キー、記憶、自動選択) は Core の `PickerSession` にあり、AppKit / WinUI 3 のピッカーは表示と入力の
  受け渡しだけを行う。
- 別プロセスであるため、Router がフォアグラウンド権限を移譲する (Windows: `AllowSetForegroundWindow`、
  macOS: アクティベーションの譲渡)。詳細は Platform.md。

### 9.3 ピッカー IPC プロトコル

Router がピッカーを子プロセスとして起動し、標準入力にリクエスト JSON を 1 件書いて閉じ、
標準出力から応答 JSON を 1 件読みます。シリアライズは Core の JSON ソース生成コンテキストを共有します
(設定ファイルと同じく、値が必要なプロパティ (`url`、`candidates` など) が `null` のメッセージは解釈できないものとして扱う)。

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
  "options": { "position": "cursor", "autoSelectSeconds": 0, "rememberChoiceDefault": false, "allowRemember": true, "language": "ja" },
  "message": null
}
```

- `candidates[].browser` / `browserProfile` はアイコン・アバター解決用。`browserProfile.directory` は
  起動に使うものと同じ値。
- `message` はフォールバック時の説明 (「設定ファイルを読み込めませんでした」など)。通常は `null`。
- `options.allowRemember` が `false` の時、ピッカーは「記憶する」を出さず、応答の `remember` は常に `false`。
- `options.language` は表示する言語 (`en` / `ja`)。Router が `settings.language` を解決して渡し (§9.4)、`message` も同じ言語で
  書く。無い場合 (この項目より古い Router) はピッカーが OS の言語に従う。

応答:

```json
{ "result": "selected", "profileId": "chrome-work", "remember": true }
{ "result": "cancelled" }
```

- プロセスの終了コードは応答を書けた場合 `0`。それ以外、または応答が JSON として解釈できない、
  `profileId` が候補に無い、といった場合は Router が §8 の手順 5 でフォールバックする。
- ピッカーは応答を書いたら標準出力を閉じ、その後で閉じるアニメーションを行ってから終了する。Router は標準出力の
  終端で応答を読み、プロセスの終了を待たずにブラウザーを起動する (アニメーションの分だけ遅れないように)。
  標準出力が閉じられないままタイムアウトした場合はピッカーを終了させてフォールバックする。
- **プロトコルにバージョン番号は持たない。** Router とピッカーは常に同じビルドを同梱して配布するため、
  不整合はフォールバックで吸収すれば十分であり、バージョン管理の手間に見合わない。
- 複数の URL が同時に開かれた場合は Router とピッカーがその数だけ並列に起動する。Windows の
  「アプリを選択」と同じ挙動であり、初期リリースでは直列化しない。

### 9.4 表示言語

UI は英語と日本語です。`settings.language` (Configuration.md §2.9) で選び、`auto` (既定) は OS の優先言語
(Windows: `GetUserPreferredUILanguages`、macOS: `NSLocale.preferredLanguages`) を先頭から見て、最初に Preferis が持つ言語を
使います (無ければ英語)。値が無い場合と、新しいバージョンが追加した知らない値も `auto` と同じです。

- **翻訳表**: 文字列は英語の原文をキーにした翻訳表 (Core の `TextCatalog`) から引く。表はアセンブリごとに 1 つで、
  `CoreText` (Core、Platform、Router、ピッカー) と `SettingsText` (設定 UI の ViewModel と両 OS の画面)。コードは
  `using static` で `L("Open")`、`L("Delete “{0}”?", name)` と書き、原文がそのままコードに残る。訳の無い文字列は英語で出る。
  日本語の表 (`*.ja.cs`) は日本語を表示するときだけ作る。
- **.resx を使わない理由**: ResourceManager はカルチャで言語を選ぶが、InvariantGlobalization ではカルチャがインバリアントに
  固定される。サテライトアセンブリを NativeAOT の各ビルド (macOS のバンドルを含む) に含める手間もかかる。翻訳表は
  ただの C# の辞書で、起動時間にも影響しない。
- **訳の検査**: `CoreTextTests` / `SettingsTextTests` がソースを走査し、`L` に渡した文字列すべてに訳があること、使われない訳が
  無いこと、`{0}` などのプレースホルダーと `{url}` のような語が原文と訳で一致することを確かめる。Windows ではビルドできない
  macOS のプロジェクトもソースとして検査される。このため `L` には必ず文字列リテラル (書式なら引数を続ける) を渡し、
  文字列補間は使わない。語順が言語で変わる文は、部品をつなげず文ごとに書式を用意する (ルールの概要の「host is …」など)。
- **言語を決める時**: 設定 UI とピッカーは起動時にプロセスの既定言語 (`UiLanguages.Current`) を決める。
  ViewModel のドロップダウンの選択肢は一度だけ作るため、一般ページで変えた言語は次に設定 UI を開いたときに反映される。
  Router は URL ごとに読み込んだ設定から決め (読み込み中と、読めない場合は OS の言語)、OS には最初に文字列を作るときに
  尋ねる。URL 処理の言語は `UiLanguages.UseLazily` の実行スコープ (`AsyncLocal`) に保持する。並列処理どうしや呼び出し元の
  既定言語を上書きせず、正常終了・例外時とも元のスコープに戻る。ピッカーには処理の Context に固定した言語を明示的に渡し、
  リクエストの `options.language` に入れる (§9.3)。
- **翻訳する範囲**: 画面に出る文字列。設定 UI、ピッカー、Router がピッカーや最後の手段のダイアログに出す理由、設定 UI に
  表示される Core のメッセージ (設定の検証、ルーティングの手順、プロファイルの解決、CSV のインポート)。CLI (`--dry-run`、
  `--register`、`--status`、使い方) の出力とログだけに出すメッセージは英語のまま。画面とログに共通のメッセージ
  (解決できない理由など) は、ログにも設定した言語で書かれる。
- 設定 UI が新しく作る名前 (「新しいルール」「… のコピー」、コマンドのプロファイル、CSV からインポートしたルール) も
  表示言語で付ける。言語名は選択肢でそれぞれの言語で書く (English / 日本語)。
- 設定 UI の名前は日本語で「Preferis 設定」(英語は Preferis Settings)。ウィンドウのタイトルと、このアプリを指す文言は表示言語に
  従う。macOS の Dock、メニューバー、Finder の名前はバンドルの `ja.lproj/InfoPlist.strings` によるため macOS の言語に従う
  (Platform.md §3.8)。ファイル名 (`Preferis Settings.app`、`Preferis.Settings.exe`) は変えない。
- **OS が描く部分は OS の言語**: Windows のファイル選択ダイアログやテキストボックスのメニュー、macOS の開くパネル。
  macOS のバンドルは `CFBundleLocalizations` に en と ja を宣言し、日本語の macOS では AppKit 自身の文字列も日本語になる
  (Platform.md §3.7, §3.8)。ただし NSPredicateEditor の複合条件の行 (「[Any] of the following are true」) は AppKit に訳が
  無く常に英語のため、日本語のときは Windows と同じ文全体 (「次のいずれかを満たす」) を `formattingDictionary` で与える。WinUI 3 は日本語のときウィンドウのルートの `Language` を
  `ja-JP` にし、英語の Windows でも漢字を日本語のフォントで描く (Platform.md §2.6, §2.8)。
- Windows の条件エディターの複合条件は「次のすべてを満たす」のように文全体を 1 つの選択肢にする (英語も
  「All of the following are true」)。以前の「[All] of the following are true」は選択肢の前後の語が言語で違い、訳せないため。

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
  フォールスルー、rewrite → reroute、ループガード、キャッチオール、書き換え後のピッカーの前の再評価)、設定の往復シリアライズと
  未知プロパティ保持、構造が不正な設定 (値が必要な場所の `null`) の拒否と、それを読んだ Router のフォールバック、
  プロファイル解決の優先順位、ピッカーのリクエスト/応答の往復シリアライズ。
  `config.json` への同時の書き込み (2 つの Router の「記憶する」、保存中の書き込みを待つ記憶、ロックのタイムアウト) は
  一時ディレクトリの実ファイルで、片方を読み込みの直後で待たせて検証する。
  並列 URL 処理の言語混線、終了・例外後の言語スコープの復元、ピッカーへの明示言語も検証する。
- **Platform**: `Local State` / `profiles.ini` のフィクスチャ解析。レジストリと Launch Services は
  薄いインターフェースで包み、フェイクで検出ロジックを検証。`LaunchPlan` 生成 (引数の組み立て) を
  OS ごとに検証。`IPickerLauncher` はフェイクで差し替え、Router のフォールバック順序を検証。
- **Settings**: ViewModel テスト。一時ディレクトリの実ファイルに対する `ConfigLoader` と、OS・ダイアログのフェイクで、
  保存・外部変更・各ページの編集がモデルにどう書かれるかを検証する。Router の「記憶する」(実際の `RoutingService`) と
  同時の保存・移動、ロックが取れない時の保存も同じ実ファイルで検証する。フェイクの OS を Windows にして、このマシンの OS を
  基準にする振る舞い (ブラウザーの選択肢、OS ごとの指定、新しいコマンド) も検証する。
  保存中の追加編集・編集取消、部分的な書き込み失敗、画面と Router の解決可否の一致、1,000 ルールのテスト結果が
  一括通知されることも回帰テストで確認する。
- **Picker**: `--request <path>` で起動する手動確認を基本とし、ロジック (キー操作による選択、
  自動選択タイマー) は UI から切り離した状態機械 (`PickerSession`) としてテストする。
- **画面の確認**: 画面収録の権限やディスプレイがなくても見た目を確認できるよう、ピッカーは `PREFERIS_PICKER_SNAPSHOT=<png>`、
  設定 UI は `PREFERIS_SETTINGS_SNAPSHOT=<フォルダー>` で描画結果を PNG に書き出す (開発用。両 OS とも同じ環境変数。Platform.md §2.6, §2.8, §3.7, §3.8)。
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
   ヘルパーバンドル同梱時の再確認は M3 で行った: ヘルパー 2 つを `Contents/Helpers/` に置いた `Preferis.app` だけが
   https / public.html のハンドラーに列挙され、ヘルパーは列挙されない。
5. **Microsoft.macOS ピッカーの NativeAOT ビルドとバンドル化** — ヘルパーバンドルとして
   `Contents/Helpers/` に配置し、内側から署名して動作すること。
   **結果 (2026-10-01, 部分)**: `net10.0-macos` + `PublishAot=true` でビルドでき、実行ファイル 3.6 MB、バンドル 4.9 MB、
   ad-hoc 署名は自動。ウォームでプロセス起動 → Main 40〜60 ms、→ ウィンドウ表示 190〜235 ms。
   Microsoft.macOS 固有の注意: `.app` は `$(PublishDir)` ではなく `$(OutDir)` にできる、既定で `.pkg` も作る
   (`CreatePackage=false` で抑止)、バンドル名は `ApplicationTitle` に従う。`Contents/Helpers/` へのネスト配置と
   内側からの署名は M3 で確認した (`codesign --verify --strict --deep` が通り、Router の子プロセスとして動作する)。

Windows:

Windows のスパイクは使い捨てのコードではなく、M4 の製品コード (`artifacts/win-x64/Preferis/`) で行った。

6. **子プロセスのピッカーへのフォーカス移譲** — `AllowSetForegroundWindow` によりピッカーが前面に来るか。
   **結果 (2026-10-02, 一部)**: Router はピッカーの起動直後にそのプロセス ID で `AllowSetForegroundWindow` を呼び、ピッカーは
   表示時に `SetForegroundWindow` を呼ぶ。ピッカーは前面に出たかどうかを trace に書く (`shown` / `shown (not in front)`)。
   バックグラウンドのターミナルから Router を起動した計測では、Router 自身が前面化の権利を持たないため "not in front" だった
   (想定どおり。ピッカーは最前面ウィンドウなので見えてはいる)。前面のアプリでリンクをクリックした場合 (Router がシェルから
   権利を受け取る場合) の確認は、Preferis を既定のブラウザーにする必要があるため開発者の目視確認が必要。
7. **WinUI 3 ピッカーの表示時間** — NativeAOT + 自己完結モードでプロセス起動から表示までを計測し、
   目標 300 ms を超えるなら Win32 TaskDialog (コマンドリンク + 検証チェックボックス) に切り替える。
   **結果 (2026-10-02)**: 目標内のため WinUI 3 のまま。空のウィンドウなら 90 ms、製品のピッカーで約 250 ms (ウォーム。
   初回 330 ms)、Router のプロセス起動からでも約 270 ms。内訳は WinUI の起動 35 ms、ウィンドウ作成 25 ms、タイトルバーを
   消すフレーム変更 65 ms、コントロール (ListView など) の初回生成 60 ms、読み込みと配置 40 ms。アイコンとプロファイル
   画像の読み取り (60 ms) は WinUI の起動と並行してバックグラウンドで行う (Platform.md §2.6)。
8. **HKCU 登録** — 管理者権限なしで「既定のアプリ」一覧に表示されること。
   **結果 (2026-10-02, 未確認)**: 書き込む内容 (Platform.md §2.2) はユニットテストで確認した。実際の登録は利用者の既定の
   アプリ一覧を変えるため、開発者が `artifacts\win-x64\Preferis\Preferis.exe --register` で確認する (解除は `--unregister`)。
9. **起動中ブラウザーへのプロファイル指定受け渡し (Windows)** — 3 と同じ確認を Windows で行う。
   **結果 (2026-10-02, 未確認)**: 利用中のブラウザーにウィンドウを開くため実施していない。手元には Edge と Edge Dev がある。
   Chromium のプロセスシングルトンは Windows でも同じ方式 (引数を起動中のプロセスに渡して終了) なので、macOS と同じ結果を
   見込んでいる。

ユーザー判断が必要な事項:

- 配布形態の残り: winget と Homebrew cask への登録、Apple Developer ID と Windows のコード署名証明書の取得
  (zip + Velopack インストーラーと dmg は決定済み、§4.4。署名と公証はシークレットを置けば動く)
- Windows で `.html` 等のファイル関連付けも登録するか (既定は URL のみ)
- 送信元アプリケーション (どのアプリが URL を開いたか) を条件に加えるか (将来拡張)

## 13. 決定事項の記録

| 日付 | 決定 | 理由 |
| --- | --- | --- |
| 2026-10-01 | Router と UI (ピッカー、設定) を別実行ファイルに分ける | Router から UI フレームワークを完全に排除する。OS 固有 UI のビルド制約を閉じ込める |
| 2026-10-01 | ピッカーは WinUI 3 / AppKit のネイティブ UI | OS 標準の外観。使用頻度が低いため初期化コストは許容 |
| 2026-10-01 | ピッカー IPC は stdin/stdout の JSON、バージョン番号なし | 同梱配布のため不整合はフォールバックで吸収する。バージョン管理は使用・実装を煩雑にする |
| 2026-10-01 | ブラウザー起動と設定ファイル書き込みは Router に集約 | 副作用を 1 か所にまとめる。ピッカーは純粋な UI |
| 2026-10-01 | `.app` / `.exe` 一式を作るビルドは各 OS の最初のマイルストーン (M2 / M4) に含める。M5 に残すのは Developer ID 署名、公証、インストーラー、ドキュメント整備のみ | ローカル・内部テストで常に最終形の成果物を使えるようにする。配布形態だけがリリース段階の関心事 |
| 2026-10-01 | macOS で先に機能を完成させ、Windows 対応はその後に行う | 開発環境が macOS であり、Apple Event 受信やヘルパーバンドルなど macOS 固有のリスクを早期に潰す。Core と設定 UI は OS 非依存なので Windows 対応は Platform と ピッカーの追加で済む |
| 2026-10-01 | Platform 層が実装する抽象 (ブラウザー検出、起動、ピッカー、通知、登録) は Core の `Hosting` 名前空間に置き、Router のオーケストレーション (`RoutingService`) も Core に置く | フォールバック連鎖をフェイクでテストできるようにする。Platform は実装だけを持つ |
| 2026-10-01 | ブラウザー ID はカタログの既知 ID のみ。チャネル (Stable / Beta / Dev / Canary) は別 ID とし、同系統の別チャネルで開くフォールバックは設けない | チャネルごとにプロファイルが別物であり、暗黙に別チャネルで開くと意図から外れる。マシン差は OS ごとの指定と `fallback` で明示する |
| 2026-10-01 | ピッカーの振る舞いは Core の `PickerSession` に置き、AppKit / WinUI 3 は描画と入力の受け渡しだけにする | 両 OS で同じ規則にし、UI なしでテストできるようにする |
| 2026-10-01 | 自動選択は「N 秒無操作で選択中の候補」。操作のたびにカウントをやり直し、記憶はしない | 先頭固定だと、別の候補に移動して待った利用者の意図と違うものが開く。無人のタイムアウトから恒久的なルールを作らない |
| 2026-10-01 | 書き換え後の URL を開く (ピッカー、記憶、フォールバックも同様)。M1/M2 では受け取った URL を開いていた不具合を M3 で修正 | Safe Links の展開のように、書き換えの目的は開く URL を変えること |
| 2026-10-01 | ピッカー IPC の `options` に `allowRemember` を追加 (バージョン番号は引き続き持たない) | ホストのない URL や検出ブラウザーの一覧では「記憶する」が意味を持たない。古いピッカーは未知プロパティを無視するので互換 |
| 2026-10-01 | 設定 UI は明示的な保存とし、外部での変更を検出して上書き前に知らせる (2026-10-02 に自動保存へ変更) | Router の「記憶する」と同期クライアントが同じファイルを書く。入力途中の設定を Router に読ませない |
| 2026-10-01 | 設定 UI の `not` は各条件ノードの「Not」フラグとして表示する (AppKit 版では NSPredicateEditor の「None of」に変更) | 否定のための 1 階層が減り、読みやすい。`not not x` は `x` と同じ意味なので畳んでも意味は変わらない |
| 2026-10-01 | M3 の UI (ピッカー、設定) は英語のみ。`settings.language` は保存だけ行う | 対応言語はユーザー判断事項 (§12) で未決。文字列を後から差し替えられるよう UI 文字列は View と ViewModel に集める |
| 2026-10-01 | ルーティング履歴は JSON Lines でローカルのログディレクトリに置く | URL を含むため同期対象に置かない。追記と切り詰めが単純 |
| 2026-10-02 | ピッカーの候補はプロファイル一覧の順に並べる。`open` の `profiles` はどれを出すかだけを表し、その順序は使わない (以前は列挙した順) | 設定 UI はチェックした順に保存していたが、その順を見せも並べ替えもできず、プロファイル画面の並び (ドラッグで変えられる) とピッカーの並びが食い違って見えた。キャッチオールやフォールバックのピッカーはもともとプロファイル一覧の順で、並びを決める場所を 1 つにする |
| 2026-10-02 | 設定 UI は変更をその場で保存する (編集が 0.6 秒途切れたら書き込む)。Save / Revert をなくす。外部の変更と衝突した編集、読めない `config.json` への編集は保留して尋ねる | macOS の設定画面は即時反映が慣習で、保存ボタンは不自然 (利用者の判断)。Router は検証エラーのある設定でも動くため、入力途中の状態を読んでもマッチしないルールになるだけ。上書きの安全性は保留で保つ |
| 2026-10-01 | 設定 UI は OS ごとにネイティブで作る (macOS: AppKit、Windows: WinUI 3)。ViewModel とサービスは `Preferis.Settings.Core` に分けて共通にし、Avalonia はやめる | Avalonia 版は機能は揃ったが、macOS のアプリとしては見た目が不自然だった。ViewModel は当初から UI 非依存で、画面だけを差し替えられる。AppKit 版はバンドルも小さくなる (Avalonia 版 26 MB) |
| 2026-10-02 | Windows のピッカーは WinUI 3 のまま (TaskDialog にしない)。タイトルバーのない角丸ウィンドウ | 表示まで約 250〜300 ms で目標 300 ms 内 (§12 スパイク 7)。タイトルバーを消すと 65 ms 余計にかかるが、Windows 11 の「アプリを選択」と同じ外観を優先した |
| 2026-10-02 | Windows のピッカーは ContentDialog の形と色にし、フッターのボタンは Open、Cancel の順で等幅にする。アクリルはやめる (2026-10-03 に背景を Mica に変更) | Windows のダイアログらしく見せる (利用者の判断)。Windows ではボタンの主アクションが左。ダイアログの配色は不透明なので背景のマテリアルは見えなくなる |
| 2026-10-02 | Windows App SDK はメタパッケージではなく WinUI コンポーネント (`Microsoft.WindowsAppSDK.WinUI`) を参照する | メタパッケージは AI / ML / Search のランタイムまで自己完結の出力に入れる。必要なのは WinUI とその依存だけ |
| 2026-10-02 | WinUI 3 の画面もコードで組み、`{Binding}` / x:Bind を使わない。XAML は `App.xaml` (コントロールのスタイル) だけ | NativeAOT ではリフレクションのバインディングが動かず、コードで組めば AppKit 版と同じ構造で読める。WinRT から返るオブジェクトの型がトリミングで失われる問題 (Platform.md §2.6) を、扱う型を自分で生成するものに限ることで避ける |
| 2026-10-02 | Windows の登録コマンドは `"Preferis.exe" --single-argument %1` | `"%1"` では `"` を含む URL がオプションを注入できる。Edge / Chrome と同じ対策 (§5.4) |
| 2026-10-02 | Windows の Router は GUI サブシステムにし、コマンドはターミナルのコンソールに接続して出力する | リンクを開くたびにコンソールウィンドウが一瞬出るのを避ける。開発用コマンドの出力がプロンプトの後に出るのは許容する |
| 2026-10-02 | 設定 UI は Router の出力を終了で区切り、Windows の Router は自分の標準ハンドルを子に継承させない | 起動したブラウザーが出力のパイプを持ち続け、テストページの「Open」がブラウザーの終了まで終わらなかった (実測 8 秒 → 0.1 秒)。macOS も同じ読み方をしていたため共通の `RouterProcess` に直した |
| 2026-10-02 | プロファイルは役割として扱い、マシンごとにターゲット全体を `local.json` の `profileTargets` で置き換えられるようにする。OS ごとのブラウザー指定は残す。エイリアスと実体の 2 階層は作らない | 同じ OS の 2 台、アカウントの違うブラウザープロファイルを OS ごとの指定では表せない。置き換えが無ければ今までどおりで、必要になったマシンでだけ見える (利用者と検討して決定)。OS ごとの指定は「Mac は全部 Safari」を同期で配るのに便利 |
| 2026-10-02 | プロファイル ID は名前から付けるが、設定 UI で変更でき、参照も書き換える。元の ID は `previousIds` に残す | 名前から付けた ID はブラウザー名を含み、役割の名前にならない。他のマシンの置き換えは古い ID のキーのままなので引き当てに使う。ランダムな ID は JSON を読む時に分かりにくい |
| 2026-10-02 | (前の行を改める) プロファイル ID は `profile-<番号>` の連番で付け、ID の変更は編集画面の Advanced に置く。番号は今ある ID、`previousIds`、このマシンの置き換えのキーの最大 + 1 で、空きは埋めない。名前はプロファイルの間で一意にし、ログには ID と名前を出す | 名前から付けると、最初の名前 (ブラウザー名) が、すぐ名前を変えても ID に残る (利用者の指摘)。マシンごとのターゲットでは別のブラウザーを開くこともあり誤解を招く。番号を使い回すと他のマシンに残った置き換えが無関係なプロファイルに当たる。名前をキーにする案は、名前の変更が参照の書き換えになり、他のマシンとのつながりも切れやすいため採らない (利用者と検討して決定) |
| 2026-10-02 | UI を英語と日本語にする (§9.4)。`settings.language` の `auto` は OS の優先言語に従う。文字列は英語の原文をキーにした C# の翻訳表から引き、.resx (ResourceManager、サテライトアセンブリ) は使わない | 利用者の要望 (§12 の判断事項)。ResourceManager はカルチャで言語を選ぶが InvariantGlobalization ではインバリアントに固定され、サテライトアセンブリを各 NativeAOT ビルドに含める手間もかかる。原文がコードに残って読みやすく、訳の漏れはソースを走査するテストで検出できる |
| 2026-10-02 | 設定 UI の言語の変更は次に設定 UI を開いたときに反映する。ピッカー IPC の `options` に `language` を追加し、Router が解決した言語を渡す (バージョン番号は引き続き持たない) | ViewModel は選択肢を一度だけ作り、画面も作り直さない構成のまま保つ。ピッカーは URL ごとに起動するので次のリンクから反映される。古いピッカーは未知のプロパティを無視し、`language` の無いリクエストではピッカーが OS の言語に従う |
| 2026-10-03 | 共有のターゲットを持たないプロファイル (「コンピューターごとに設定」) を認める。`config.json` では `browser` も `command` も無いプロファイルで、検証のエラーにしない。置き換えの無いマシンでは解決できない扱いで `fallback` → 既定 → ピッカーに進む。設定 UI では「+」メニューの「空のプロファイル…」と「リンクを開く対象」の選択肢で作る | 特定のマシンでだけ開くものを決めたい役割のため (利用者の提案)。「何もしない」ターゲットも検討したが、置き換えていないマシンで URL を黙って捨てることになり (§1)、ルールを見ても分からないため採らなかった (利用者と検討して決定)。新しい属性を足さないので、古いバージョンも同じく `fallback` 以降に進む |
| 2026-10-03 | ワイルドカードは Regex を使わずに照合する。正規表現はバックトラック版を 10 ms のタイムアウトで先に試し、超えた時だけ `NonBacktracking` で評価し直す (`NonBacktracking` が対応しない構文は従来どおりバックトラック版 + 100 ms) | Router は URL ごとのプロセスで、条件ごとの照合器を毎回作る。`NonBacktracking` の構築は 1 つ約 0.8 ms・0.7 MiB で、CSV で取り込んだ 1,000 件のワイルドカードのどれにも一致しない URL で約 0.6 秒・約 1 GB かかっていた (#15)。バックトラック版だけにすると破滅的バックトラックのパターンがタイムアウトで誤って不一致になるため、線形のエンジンは遅い場合の答えとして残す |
| 2026-10-02 | ルールの CSV インポート (URL かドメイン, プロファイル名) を設定 UI に置く。スキームの有無で `url` / `host`、どちらもワイルドカード。パスの無い URL には `/*` を補う。プロファイルごとに 1 つのルールにまとめ、既存のルールの後に加える | 他のツールからの移行用 (利用者の要望)。URL 条件は正規化した URL 全体と照合するので、`https://*.slack.com` はそのままでは何にも当たらない。既存のルールにまとめないのは、手で直したルールを書き換えないため |
| 2026-10-05 | CSV インポートは `host:port`、先頭 `/` を印とする URL 正規表現、2 列目以降の複数プロファイル名を受け入れる (Configuration.md §2.12)。`host:port` はホストのワイルドカードと実際の authority ポートの正規表現を `and` で結び、正規化で省かれる既定ポートにも対応する。正規表現は先頭 `/` と、エスケープされていない末尾 `/` があればその 1 つを除き、空・不正なものを拒否する。末尾 `/` の直前に連続する `\` が奇数個ならエスケープ済みとして残し、偶数個なら区切りとして除く。閉じる区切りの省略は認め、フラグの指定には対応しない。プロファイルの組み合わせごとに 1 つのルールと複数候補の `open` を作り、列順と同じ名前の繰り返しは組み合わせを変えない。空欄・未知名があれば行全体を拒否する | 他のツールからの移行でポート・正規表現・複数候補を失わないため (#17、#18、#24)。ホスト条件を残すことで国際化ドメイン名とワイルドカードの照合を維持し、URL のユーザー情報やパス内のポートらしい文字列を誤認しない。末尾 `/` を区切りとして扱って入力時の曖昧さを減らし、照合に含めたい場合は `\/` で残す。重複判定は `\d` と `\D` を区別する。複数候補をプロファイル別のルールに分けると最初のルールだけが適用される。名前と候補は設定のプロファイル一覧順、ルールは組み合わせの初出順とし、既存ルールへの追加位置と見出し行の扱いは変えない |
| 2026-10-03 | 既知の `type` の条件・アクションも未知のプロパティを保持する (基底クラスの `Condition` / `RoutingAction` に `JsonExtensionData`)。設定 UI は作り直す条件・アクションに引き継ぎ、条件エディターでは編集後の行を編集前の条件に内容と位置で対応付ける (`ConditionPredicates.FromEditor` に編集前の条件を渡す)。種類が変わった条件・アクションのものは捨てる | 互換性方針 (Configuration.md §4) どおり、新しいバージョンが書いた設定を無関係な編集で壊さない。エディターの木は NSPredicate を経由して作り直されるので行に識別子を持たせられない。エディターは変更を 1 つずつ知らせるため、内容と位置の対応付けで足りる。古い種類に向けて書かれたプロパティを別の種類に付けると、新しいバージョンが別の意味に読むおそれがある |
| 2026-10-03 | ルール自身の `rewrite` で URL を変えた後のピッカーを出す `open` (キャッチオールを含む) は、ピッカーの前に上のルール (キャッチオールならすべてのルール) を書き換え後の URL で通常どおり評価し、決定したルールに従う (§6.1 の 9)。「記憶する」のルールの形式と挿入位置は変えない。上のルールが書き換え後の URL に一致すれば、以前はピッカーが出ていたところでそのルールが決める (`none` や `system` も含む) | 記憶したルールの条件は書き換え後のホストだが、挿入位置 (元ルールの直前) では書き換え前の URL で評価されるため、`rewrite` + `open` のルールでは記憶しても毎回ピッカーが出ていた (#5)。上のルールは URL が書き換え後の形で来ていれば決めていたはずのもので、`rewrite` + `reroute` と同じ考え方で先に機会を与える。採らなかった案: 記憶したルールを元ルールの後ろに置く (元ルールが先に終端するため届かない)。前に置いたまま条件を工夫する (書き換え前の URL から書き換え後のホストは判定できない)。条件を書き換え前のホストにして元ルールの書き換えを複製する (記憶の単位がラッパーのホストになり、別の宛先にも同じ選択が使われる)。元ルールを `rewrite` だけのルールと `open` のルールに分ける (書き換え後の URL が下のルールにも渡り、`open` 側には書き換え後の URL に合う条件が要るなど、利用者のルールの意味が変わる)。下のルールに続けて評価する (ピッカーが出るかどうかで下のルールの扱いが変わり、ルールの順序の意味と食い違う)。ホストを変えない書き換えでは記憶したルールが書き換え前の URL に先に一致する問題 (§9.2) は残る |
| 2026-10-03 | ピッカーの「記憶する」は、ピッカーを開いている間に変わったプロファイル ID を `previousIds` で今の ID に直して書く。その ID を今持つプロファイルを優先する。削除されたプロファイルと、複数のプロファイルの `previousIds` にあってどれか決められない ID は記憶せず、URL を開くだけにする | ピッカーの返す ID をそのまま書くと、存在しない ID を指すルールになり、次から選んだプロファイルで開かずに既定のプロファイルなどへ流れる (検証の警告が出るだけで気付きにくい)。推測で別のプロファイルを記憶するより、次にもう一度選んでもらう方が害が小さい。今持つプロファイルを優先するのはマシンごとのターゲットの引き当て (§7.4) と同じ規則 |
| 2026-10-03 | 設定 UI の検証は編集のたびに行ったまま、パターンの検査結果を (比較方法, パターン, 大文字小文字の区別) をキーに覚え、変わったパターンだけを検査する。覚えるのは直前の検証で使ったものだけ。パターンの検査は正規表現の解析だけにし、非バックトラッキングのオートマトンは作らない | 1,000 条件のルールでは名前を 1 文字変えるたびに正規表現を 1,000 個作り直していた (1 回約 0.7〜1 秒、986 MiB の割り当て。#3)。覚えておけば編集 1 回は約 1 ms で、検証を入力が止まるまで遅らせる必要はない (遅らせると、閉じる時の保存やエラー表示が古い結果を使わないよう順序を考えることになる)。構文の誤りはどちらのエンジンでも同じ解析器が報告し、非バックトラッキングのエンジンが加えるのは未対応の構文の `NotSupportedException` だけ (その時 Router はバックトラッキング版で照合する) なので、構築の安いバックトラッキング版で検査結果は変わらない。その後 #15 で照合器そのものが安くなり (§5.2)、`ErrorOf` は照合器の誤りを返すだけになった |
| 2026-10-03 | 値が必要な場所の `null` (`"rules": null`、`"actions": [null]`、`null` の `id` など) は構造の誤りとし、JSON の構文エラーと同じくファイル全体の読み込みエラーにする (Configuration.md §4.1)。プロパティは System.Text.Json の `RespectNullableAnnotations` (ソース生成コンテキストに指定するので NativeAOT のまま) でモデルの null 許容注釈どおりに拒否し、それが見ない配列の要素と辞書の値は読み込み直後の `ConfigStructure` で拒否する。省略は従来どおり既定値で読む | JSON として正しい `null` が読み込みを通り、ルーティング中の例外で Router が失敗終了して URL を捨てていた (§8 の原則に反する。issue #2)。読み込みの境界で拒否すれば、Router (§8 の 1)、設定 UI (読めないファイルを上書きしない)、「Use Existing File…」が読めないファイルの既存の扱いをそのまま使える。`null` を既定値に読み替える案は手で書いた誤りを黙って直し、設定 UI の保存でそれが確定してしまう。誤りのあるルールだけを読み飛ばす案は、そのルールが止めるはずだった URL を別のブラウザーで開きうる (未知のアクションを失敗とするのと同じ理由、§6.1)。`ConfigValidator` は保存を妨げない表示用で、Router はその前に例外で落ちる。注釈に従わせるのは、null 非許容のプロパティが増えても検査が追従するため。同じコンテキストを使うピッカー IPC のメッセージもプロパティについては同じ規則で読む (同梱の Router とピッカーの間なので要素の検査はしない) |
| 2026-10-03 | `config.json` を読んで書き戻す処理 (Router の「記憶する」、設定 UI の保存と移動) は、`local.json` と同じフォルダーの `config.lock` を `FileShare.None` で開いてから行う。3 秒取れなければ諦め、Router は記憶せずに URL を開き、設定 UI は編集を保存待ちのまま理由を出す。同期クライアントや別のマシンとの競合はこのロックでは扱わない (Configuration.md §1.1) | 2 つの Router が同時に記憶すると後の書き込みが先のルールを消し、設定 UI も外部の変更を確かめてから書き込むまでの間に書かれた変更を見逃していた (#1)。アトミック書き込みは途中の状態を見せないだけで、読み込みから書き込みまでの競合は防がない。ファイルなら .NET の同じコードで両 OS とも排他になり (Windows は共有違反、macOS は `flock`)、プロセスが終われば OS が解放するので異常終了で残らない。`config.json` の隣はクラウドストレージのことがあり、ロックファイルが同期されても他のマシンは止められないため、同期しないフォルダーに置く |
| 2026-10-03 | Windows のピッカーの背景を Mica にする。内容は ContentDialog の不透明な色の代わりに `LayerFillColorDefaultBrush` に置き、フッターは Mica のまま | 利用者の要望。設定 UI と同じく Windows 11 の WinUI のウィンドウらしく見せる。ContentDialog の色は不透明で Mica を隠すため、Mica のウィンドウの標準の半透明のレイヤーで「上が明るく下が素材」の階層を保つ |
| 2026-10-03 | Windows のピッカーの見出しによる移動は、見出しを `InputNonClientPointerSource` でタイトルバーの領域にして OS に任せる。コピーボタンと URL は Passthrough | `PointerPressed` から `WM_NCLBUTTONDOWN` を送る方法では、ボタンを離したことが移動のループに届かず、クリックしただけでウィンドウがポインターに付いて動いた (利用者の報告)。タイトルバーの領域なら、アクティブ化、ドラッグの判定、離した時の終了がすべて普通のタイトルバーと同じになる |
| 2026-10-29 | 配布は Windows がポータブル zip と Velopack のインストーラー (x64 / arm64 を別チャネル)、macOS が Apple Silicon の dmg。リリースは `build-release.yaml` (workflow_dispatch、dry-run あり) で Cysharp/Actions の `validate-release` / `create-release` を使い、パッケージ作成は `Preferis.Build.proj` の `PackageWindows` / `PackageMacOS` に置く。バージョンは `-p:Version` で渡し、コミットしない | Velopack は管理者権限なしのインストール、GitHub Release をそのまま更新フィードにできること、差分更新、NativeAOT との相性で選んだ。パッケージ作成を MSBuild に置くのは §4.4 の方針 (`dotnet` だけで完結、ローカルでも同じコマンド) に合わせるため。org のワークフローはライブラリー向けなので、NuGet の部分を成果物のアップロードに置き換えた。arm64 は公開リポジトリ限定の `windows-11-arm` ランナーが使えないため x64 でクロスコンパイルする |

## 14. マイルストーンと現在の状態

macOS で一通りの機能を完成させてから Windows 対応に進みます。Core と設定 UI のロジックは OS 非依存に作るため、
Windows 対応で追加するのは Platform の Windows 実装、Router の Windows 経路、WinUI 3 のピッカーと設定 UI の画面です。
`.app` / `.exe` 一式を生成するビルド (§4.4) は各 OS の最初のマイルストーンに含め、以降のマイルストーンでは
常にその成果物でテストします。M5 はリリース段階の作業 (署名・公証・インストーラー・ドキュメント) だけです。

| M | 内容 | 完了条件 | 状態 |
| --- | --- | --- | --- |
| M0 | スパイク (§12 の macOS 項目 1〜5) と計測方法の確立 | macOS 起動経路とピッカー構成の方式が確定している | 完了 (2026-10-01)。残件はヘルパーのネスト配置時の再確認で、M2 で行う。`tools/bench` は測る対象の Router ができる M2 で作る |
| M1 | Core: 設定モデル、JSON、条件、ルーティング、プロファイル解決、IPC メッセージ、`--dry-run` | ユニットテストで §6〜§7 の仕様を網羅。`--dry-run` で設定ファイルの評価結果を確認できる | 完了 (2026-10-01)。`Preferis.Core` + 111 テスト、`Preferis.Router` は `--dry-run` のみ。NativeAOT publish 確認済み |
| M2 | macOS Platform + Router 高速経路: Launch Services 検出、プロファイル列挙、LaunchPlan 実行、既定ブラウザー登録、Apple Event 受信。**`PublishMacOS` ターゲットで ad-hoc 署名済みの `Preferis.app` 一式を生成** | `artifacts/` の `Preferis.app` を既定ブラウザーにし、単一候補のルールで日常利用できる | 完了 (2026-10-01)。`open -a` 経由と argv 経由で Chrome / Edge Dev への振り分けを確認。既定ブラウザー化は `--register` をユーザーが実行して確認する。ピッカー不在時は既定プロファイルへフォールバック。Platform.md §5 に計測値 |
| M3 | macOS ピッカー (AppKit, ヘルパーバンドル) + 設定 UI (AppKit)。両者を `Preferis.app` に同梱 | macOS で全機能が揃い、設定ファイルを手で編集せずに運用できる | 実装完了、目視確認待ち (2026-10-02)。`Preferis Picker.app` (6.8 MB) と `Preferis Settings.app` (8.7 MB。Avalonia 版 26 MB から AppKit に置き換え) はどちらも AppKit + NativeAOT で、`Contents/Helpers/` に同梱し内側から署名 (`Preferis.app` 全体で 20 MB)。Router 経由でピッカー表示まで 120 ms、「記憶する」、オプトイン履歴、`Preferis.app` を直接開くと設定 UI。テスト 367 件 (Core 221, Platform 35, Settings.Core 111)。描画はウィンドウのキャプチャで確認済みだが、実際のリンククリックからのピッカーの前面表示と設定 UI の手操作は開発者の目視確認が必要。アプリアイコンは 2026-10-23 に追加 (Platform.md §3.1) |
| M4 | Windows: スパイク (§12 の 6〜9)、Platform の Windows 実装、Router の Windows 経路、WinUI 3 ピッカー、WinUI 3 の設定 UI (`Preferis.Settings.Core` の ViewModel を使う)。**`PublishWindows` ターゲットで exe 一式のフォルダーを生成** | `artifacts/` の `Preferis.exe` を既定ブラウザーにし、M3 と同等の機能が揃う。同一の設定ファイルを両 OS で共有できる | 実装完了、目視確認待ち (2026-10-02)。`dotnet build build/Preferis.Build.proj` で `artifacts/win-x64/Preferis/` (82 MB: Router 4.9 MB、ピッカー 7.6 MB、設定 10 MB と Windows App SDK) を生成。3 つとも NativeAOT。レジストリからのブラウザー検出、HKCU への登録、`--single-argument`、ピッカー (Router 起動から表示まで約 270 ms)、設定 UI (Windows 11 の設定アプリ風、条件は行のエディター)、テストページからの Router 実行を確認。テスト 424 件 (Core 221, Platform 80, Settings.Core 123)。macOS 側の変更 (ViewModel、`RouterProcess`) は Windows ではビルドとテストのみで、AppKit の 2 プロジェクトは macOS での確認が必要。未確認: 既定ブラウザーにしてのリンククリック (前面表示、スパイク 6)、既定のアプリ一覧への表示 (スパイク 8)、起動中ブラウザーへの受け渡し (スパイク 9)、Firefox。アプリアイコンは 2026-10-22 に追加 (Platform.md §2.7) |
| M5 | リリース: Developer ID 署名と公証、インストーラー (macOS: dmg / Homebrew cask、Windows: zip / winget 等)、ベンチ結果の記録、利用者向けドキュメント | 配布物を第三者が導入できる | 進行中 (2026-10-29)。ビルド・パッケージ・リリースのワークフロー `build-release.yaml` と `PackageWindows` / `PackageMacOS` ターゲット (§4.4) を追加: Windows x64 / arm64 のポータブル zip と Velopack インストーラー、macOS (Apple Silicon) の dmg を作ってドラフトリリースに添付する。Developer ID 署名と公証はシークレットがあれば行う (証明書は未取得)。ローカルで確認したのは Windows x64 の `PackageWindows` (zip、Setup.exe、nupkg、フィード) と 881 件のテストで、arm64 のクロスコンパイルと macOS の dmg / 署名 / 公証はワークフローの初回実行 (dry-run) で確認する。残り: アプリ側の Velopack 統合 (`VelopackApp.Build().Run()`、更新チェック)、Windows のコード署名、winget / Homebrew cask、利用者向けドキュメント、ベンチ結果 |

M3・M4 の後、UI を英語と日本語の 2 言語にした (§9.4、2026-10-02)。Windows では設定 UI とピッカーを NativeAOT で発行し、
日本語の表示をスナップショットで確認した。AppKit の 2 プロジェクト (メニューバー、`CFBundleLocalizations`、NSPredicateEditor の
表示を含む) は macOS でのビルドと確認が必要。

本ドキュメントは設計段階のものです。実装と齟齬が出た場合は実装側の判断を本書に反映してください。
