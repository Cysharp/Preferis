# 設定ファイル仕様

Preferis の設定は 2 つの JSON ファイルに分かれます。

| ファイル | 役割 | 同期 |
| --- | --- | --- |
| `config.json` | プロファイル、ルール、UI 設定。マシン非依存 | クラウドストレージで同期してよい |
| `local.json` | `config.json` の場所、マシン固有の上書き、ログ設定 | 同期しない (常にローカル) |

## 1. 配置場所

| OS | `local.json` の場所 (固定) | `config.json` の既定の場所 |
| --- | --- | --- |
| Windows | `%LOCALAPPDATA%\Preferis\local.json` | `%APPDATA%\Preferis\config.json` |
| macOS | `~/Library/Application Support/Preferis/local.json` | `~/Library/Application Support/Preferis/config.json` |

`local.json` の `configPath` が設定されていればそちらを `config.json` として読みます
(例: OneDrive や Google Drive 配下)。シンボリックリンクはクラウドストレージとの相性が悪いため、
ポインター方式を採用します。設定 UI の「Move To…」(クラウドストレージへの移動) は、編集中の設定を新しい場所に
書き出して `configPath` を書き換えます。元のファイルを消すのはそれがこのマシンの既定の場所だった場合だけで、
それ以外 (クラウドストレージ上など) は他のマシンが使っている可能性があるため残します。移動先に既に `config.json` が
あれば、それを使う (別のマシンで同期済みの設定を採用する) か置き換えるかを選ばせます。「Use Existing File…」は
既存のファイルを変更せずに `configPath` だけを向けます。

書き込みは一時ファイルに書いてから置き換える (アトミック書き込み) こととし、同期ツールが
中途半端な状態のファイルを拾わないようにします。読み込み時に JSON が壊れていた場合は
[Design.md §8](Design.md) のフォールバックに従います。

## 2. `config.json`

### 2.1 例

```json
{
  "version": 1,
  "defaultProfile": "edge",
  "profiles": [
    { "id": "edge", "name": "Microsoft Edge", "browser": "edge" },
    {
      "id": "chrome-default",
      "name": "Chrome (Default)",
      "browser": "chrome",
      "browserProfile": { "directory": "Default", "name": "Mayuki" }
    },
    {
      "id": "chrome-work",
      "name": "Chrome (Work)",
      "browser": "chrome",
      "browserProfile": { "directory": "Profile 1", "name": "Work", "email": "me@work.example.com" },
      "fallback": "edge"
    },
    { "id": "edge-private", "name": "Edge (InPrivate)", "browser": "edge", "private": true },
    { "id": "system-browser", "name": "OS 標準ブラウザー", "browser": { "windows": "edge", "macos": "safari" } },
    {
      "id": "custom-tool",
      "name": "社内ツール",
      "command": {
        "windows": { "path": "%LOCALAPPDATA%\\Programs\\Foo\\foo.exe", "args": ["--open", "{url}"] },
        "macos":   { "path": "/Applications/Foo.app/Contents/MacOS/Foo", "args": ["{url}"] }
      }
    }
  ],
  "rules": [
    {
      "id": "unwrap-safelinks",
      "name": "Outlook Safe Links を展開して再ルーティング",
      "enabled": true,
      "condition": { "type": "host", "match": "wildcard", "pattern": "*.safelinks.protection.outlook.com" },
      "actions": [
        { "type": "rewrite", "mode": "queryParameter", "parameter": "url" },
        { "type": "reroute" }
      ]
    },
    {
      "id": "google-admin",
      "name": "Google Admin は Chrome (Work)",
      "enabled": true,
      "condition": { "type": "url", "match": "startsWith", "pattern": "https://admin.google.com/" },
      "actions": [ { "type": "open", "profiles": ["chrome-work"] } ]
    },
    {
      "id": "app-split",
      "name": "app1 / app2 でプロファイルを分ける",
      "enabled": true,
      "condition": {
        "type": "or",
        "conditions": [
          { "type": "host", "match": "equals", "pattern": "app1.example.com" },
          { "type": "host", "match": "equals", "pattern": "app2.example.com" }
        ]
      },
      "actions": [ { "type": "open", "profiles": ["chrome-default"] } ]
    },
    {
      "id": "ms-login",
      "name": "Microsoft ログインは選択する",
      "enabled": true,
      "condition": { "type": "host", "match": "regex", "pattern": "^login\\.microsoftonline\\.com$" },
      "actions": [ { "type": "open", "profiles": ["edge", "chrome-work", "chrome-default"] } ]
    }
  ],
  "catchAll": {
    "actions": [ { "type": "open" } ]
  },
  "settings": {
    "picker": { "position": "cursor", "autoSelectSeconds": 0, "rememberChoiceDefault": false },
    "language": "auto"
  }
}
```

### 2.2 ルート

| プロパティ | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `version` | number | ○ | スキーマバージョン。現在 `1`。読み込み側は自分より新しいバージョンを拒否せず、未知プロパティを保持したまま扱う |
| `defaultProfile` | string | — | デフォルトプロファイルの `id`。未設定なら `open` でプロファイル未指定時にピッカーを表示 |
| `profiles` | Profile[] | ○ | プロファイル定義 |
| `rules` | Rule[] | ○ | ルーティングルール (評価順) |
| `catchAll` | { actions } | ○ | キャッチオールルール。条件は持たない |
| `settings` | object | — | UI 設定 (§2.9) |

### 2.3 Profile

| プロパティ | 型 | 説明 |
| --- | --- | --- |
| `id` | string | 一意な識別子。ルールから参照する。設定 UI は `profile-1`, `profile-2` ... と連番で付ける (§2.10)。名前を変えても変わらない。設定 UI の Advanced で変更でき、参照も書き換わる (§2.11) |
| `previousIds` | string[] | 以前の `id` (新しい順)。設定 UI が `id` を変えた時に書く。他のマシンの `local.json` の `profileTargets` (§3.1) が古い `id` のままでも見つけるため |
| `name` | string | 表示名。ピッカーや設定 UI でプロファイルを見分けるのは名前なので、プロファイルの間で重複してはいけない (前後の空白を除き、大文字と小文字を区別せずに比べる。重複は検証のエラー) |
| `browser` | string \| { windows?, macos? } | ブラウザー ID (§2.4)。`command` と排他 |
| `browserProfile` | BrowserProfileRef | ブラウザープロファイル参照 (§2.5)。`browser` 指定時のみ |
| `private` | bool | プライベート/シークレットモードで開く。ブラウザーごとに適切なフラグに変換 |
| `extraArgs` | string[] | 追加のコマンドライン引数 (例: `--new-window`) |
| `command` | { windows?, macos? } | 任意の実行ファイル。各 OS に `{ path, args[] }`。`args` 内の `{url}` が置換される。`browser` と排他 |
| `fallback` | string | このマシンで解決できない時に代わりに使うプロファイル `id` |

`browser` / `browserProfile` / `private` / `extraArgs` / `command` をまとめて **ターゲット** と呼びます。`config.json` の
ターゲットはすべてのマシンで共有されます。マシンによって違うものを開きたい場合は、そのマシンの `local.json` で
ターゲットを置き換えます (§3.1)。`browser` も `command` も無いプロファイルは「コンピューターごとに設定」で、共有の
ターゲットを持たず、各マシンの置き換えだけが開くものを決めます (置き換えの無いマシンでは `fallback` に進みます)。プロファイルは「Personal」「Work」のような役割の名前を持ち、ルールはその役割を参照し、
各マシンが実際に何を開くかを決める、という使い方になります。

### 2.4 ブラウザー ID

ブラウザー ID は **カタログに定義された既知の ID** であり、任意の文字列は指定できません。
Edge / Chrome / Firefox のようにリリースチャネルが複数あるブラウザーは、**チャネルごとに別の ID**
として扱います。チャネルごとにインストール先・bundle id・ユーザーデータディレクトリが異なり、
Chromium 系ではブラウザープロファイルの集合も共有されないため、同一視できないからです
(Firefox は `profiles.ini` を全チャネルで共有しますが、インストール先は別です)。

| ID | ブラウザー | Windows | macOS (bundle id) | プロファイル方式 |
| --- | --- | --- | --- | --- |
| `edge` | Microsoft Edge (Stable) | StartMenuInternet `Microsoft Edge` | `com.microsoft.edgemac` | chromium |
| `edge-beta` | Microsoft Edge Beta | `Microsoft Edge Beta` | `com.microsoft.edgemac.Beta` | chromium |
| `edge-dev` | Microsoft Edge Dev | `Microsoft Edge Dev` | `com.microsoft.edgemac.Dev` | chromium |
| `edge-canary` | Microsoft Edge Canary | `Microsoft Edge Canary` | `com.microsoft.edgemac.Canary` | chromium |
| `chrome` | Google Chrome (Stable) | `Google Chrome` | `com.google.Chrome` | chromium |
| `chrome-beta` | Google Chrome Beta | `Google Chrome Beta` | `com.google.Chrome.beta` | chromium |
| `chrome-dev` | Google Chrome Dev | `Google Chrome Dev` | `com.google.Chrome.dev` | chromium |
| `chrome-canary` | Google Chrome Canary | 既定ブラウザーに登録されないため既知パスで検出 | `com.google.Chrome.canary` | chromium |
| `brave` | Brave | `Brave` | `com.brave.Browser` | chromium |
| `vivaldi` | Vivaldi | `Vivaldi` | `com.vivaldi.Vivaldi` | chromium |
| `opera` | Opera | `OperaStable` | `com.operasoftware.Opera` | chromium (単一) |
| `arc` | Arc | `Arc` | `company.thebrowser.Browser` | chromium (要調査) |
| `firefox` | Mozilla Firefox (Release / ESR) | `FIREFOX.EXE` / `Firefox-<hash>` | `org.mozilla.firefox` | firefox |
| `firefox-developer` | Firefox Developer Edition | `Firefox Developer Edition-<hash>` | `org.mozilla.firefoxdeveloperedition` | firefox |
| `firefox-nightly` | Firefox Nightly | `Firefox Nightly-<hash>` | `org.mozilla.nightly` | firefox |
| `safari` | Safari | — | `com.apple.Safari` | なし |
| `win:<key>` | カタログ外 (Windows) | StartMenuInternet `<key>` | — | なし |
| `mac:<bundle id>` | カタログ外 (macOS) | — | `<bundle id>` | なし |

解決の規則:

- ID はカタログエントリと **完全一致** で解決する。`edge` が無いマシンで `edge-dev` を代わりに使う、
  といったチャネル間の暗黙のフォールバックは行わない。別チャネルのプロファイルを黙って開くと
  ユーザーの意図と異なる結果になるため。
- マシンによって使うチャネルが違う場合は、OS ごとの指定 (`"browser": { "windows": "edge", "macos": "edge-dev" }`)
  またはプロファイルの `fallback` で明示する。
- `win:` / `mac:` プレフィックス付き ID だけが例外的に自由形式で、検出結果に含まれるキー名・bundle id を
  そのまま使う。本質的にマシン依存なので OS ごとの指定と組み合わせる。
- ユーザーが ID を手で入力することは想定しない。設定 UI は検出結果 (チャネル名を含む表示名) から選ばせ、
  対応する ID を保存する。

カタログは Core に埋め込みの定数データとして持ち、エントリごとに表示名、OS ごとの検出ヒント
(StartMenuInternet キー名、実行ファイル名、既知のインストールパス、bundle id)、ユーザーデータ
ディレクトリ、プロファイル方式、プライベートモードのフラグを持ちます。キー名・bundle id の正確な値は
実装時に実機で確認し、[Platform.md](Platform.md) に記録します。

### 2.5 BrowserProfileRef

```json
{ "directory": "Profile 1", "name": "Work", "email": "me@work.example.com" }
```

| プロパティ | 対応 | 説明 |
| --- | --- | --- |
| `email` | chromium | `Local State` の `user_name`。マシンをまたいで最も安定 |
| `name` | chromium / firefox | 表示名 (`name`、Firefox は `profiles.ini` の `Name`) |
| `directory` | chromium | ディレクトリ名。最後の手段であり、プロファイルデータが読めない環境ではこれをそのまま渡す |

解決順序は `email` → `name` → `directory`。設定 UI は検出結果から選択した時に取得できる値をすべて保存します。
Firefox のプロファイルは `-P <name>` で選ぶため `name` だけを保存します (`directory` はマシン固有のパスになるため)。

### 2.6 Rule

| プロパティ | 型 | 説明 |
| --- | --- | --- |
| `id` | string | 一意な識別子 |
| `name` | string | 表示名 |
| `enabled` | bool | 無効なルールは評価をスキップ |
| `condition` | Condition | 条件木 |
| `actions` | Action[] | アクション列。空は不可 (UI で禁止) |

### 2.7 Condition

```json
{ "type": "and" | "or", "conditions": [ Condition, ... ] }
{ "type": "not", "condition": Condition }
{ "type": "host" | "url", "match": "regex" | "wildcard" | "startsWith" | "endsWith" | "contains" | "equals",
  "pattern": "...", "ignoreCase": true }
```

`ignoreCase` は `url` のみ有効 (既定 `true`)。`host` は常に大文字小文字を区別しません。
意味論は [Design.md §6.2](Design.md) を参照。

### 2.8 Action

```json
{ "type": "open", "profiles": ["chrome-work"] }         // profiles 省略または空 = デフォルトプロファイル。複数ならピッカーに出す (並びはトップレベルの profiles の順で、ここでの順序は問わない)
{ "type": "system" }
{ "type": "none" }
{ "type": "rewrite", "mode": "regex", "pattern": "^http://", "replacement": "https://" }
{ "type": "rewrite", "mode": "queryParameter", "parameter": "url" }
{ "type": "reroute" }
```

`rewrite` の結果が有効な絶対 URL でない場合はルーティング失敗として扱います。`queryParameter` モードは
値をパーセントデコードしますが `+` を空白には変換しません (値に埋め込まれた URL の `+` を壊さないため)。
`regex` モードは正規化済みの絶対 URL (小文字のスキームとホスト、既定ポート除去) に対して置換します。
同じルールの `rewrite` で URL が変わった後の `open` で候補が複数になる (ピッカーを出す) 場合は、その前に、そのルールより上の
ルール (キャッチオールならすべてのルール) を書き換え後の URL で評価し、決定したルールがあればそれに従います
([Design.md §6.1](Design.md) の 9)。

### 2.9 settings

| プロパティ | 既定 | 説明 |
| --- | --- | --- |
| `picker.position` | `cursor` | `cursor` / `center` |
| `picker.autoSelectSeconds` | `0` | 0 で無効。N 秒無操作で選択中の候補 (初期状態は先頭) を選択。キーやマウスの操作でカウントはやり直し。自動選択では「記憶する」は働かない |
| `picker.rememberChoiceDefault` | `false` | 「記憶する」チェックの初期状態 |
| `language` | `auto` | `auto` / `en` / `ja`。UI の言語 (Design.md §9.4)。`auto` は OS の優先言語のうち Preferis が持つ最初のもの (無ければ英語)。知らない値は `auto` と同じ。設定 UI には次に開いたときから、ピッカーには次のリンクから反映される |

### 2.10 ID の生成

設定 UI がプロファイル・ルールを作る時の `id` は次の規則で付けます。手で書く場合はこの形に従う必要はなく、一意であれば
何でも構いません。

- プロファイルは `profile-<番号>` (`profile-1`, `profile-2` ...)。番号は、今あるプロファイルの `id` と `previousIds`、
  このマシンの `local.json` の `profileTargets` のキーのうち `profile-<番号>` の形をしたものの最大の番号 + 1 (無ければ 1)。
  途中の空いた番号は埋めない。削除したプロファイルの番号を使い回すと、他のマシンの `local.json` に残った置き換え (§3.1) が
  無関係なプロファイルに当たってしまうため。最大の番号のプロファイルを削除した直後に限りその番号がまた使われるが、
  作ってすぐ消したプロファイルに他のマシンが置き換えを持っていることはまず無いので許容する
- プロファイルの `id` を名前から作らないのは、検出したブラウザーから作るプロファイルの最初の名前がブラウザー名で、
  後で名前を変えても `id` に残るため。マシンごとのターゲットがあると、`microsoft-edge` が Safari を開くこともある
- ルールは名前から作る。ASCII の英数字は小文字にして残し、それ以外の文字の並びは `-` 1 つにする。先頭と末尾の `-` は
  除く。最大 48 文字 (`Google Admin` → `google-admin`)。何も残らなければ (日本語だけの名前など) `rule`。既に使われて
  いれば `-2`, `-3` ... を付ける
- ピッカーの「記憶する」が作るルールは `remember-<host>-<ランダム 8 桁>`

### 2.11 プロファイル ID の変更

連番の `id` はルールの JSON を読むと何を指すのか分かりにくい。役割の名前 (`work`) にしたい時のために、設定 UI は
プロファイル編集画面の Advanced でプロファイルの `id` を変更できます。

- 新しい `id` は 1〜48 文字の ASCII 英数字と `-` `_` `.`。他のプロファイルの `id` と重なってはいけない。`:` は
  ピッカーが検出ブラウザーを表すのに使う (`browser:<id>`) ため使えない
- 他のプロファイルの `previousIds` にある `id` も使えない。他のマシンがまだその `id` で置き換えを持っているかもしれず、
  別のプロファイルに当たってしまうため。設定 UI が付ける連番も同じ理由でそれらを避ける (§2.10。複製は `previousIds` を
  引き継がない)
- 変更すると、`defaultProfile`、各プロファイルの `fallback`、ルールとキャッチオールの `open` の `profiles`、
  このマシンの `local.json` の `profileTargets` のキーを新しい `id` に書き換える。新しい版の Preferis が書いた未知の
  アクションの中は書き換えない
- 元の `id` を `previousIds` の先頭に加える (戻した場合は `previousIds` から除く)。他のマシンの `local.json` は
  書き換えられないため、そのマシンの Router は `previousIds` を手がかりに置き換えを見つけ、そのマシンで設定 UI を
  開いた時にキーを新しい `id` に書き換える
- `previousIds` はピッカーを開いている間の変更にも使う。ピッカーは開いた時の `id` を返すため、Router の「記憶する」は
  `previousIds` から今の `id` を引いてルールを書く (Design.md §9.2)。その `id` を今持つプロファイルがあれば、他の
  プロファイルの `previousIds` にあっても今持つ方とする。どのプロファイルも持たず `previousIds` にも無い (削除された)
  場合と、複数のプロファイルの `previousIds` にある (手で書いた設定でだけ起きる) 場合は記憶しない

### 2.12 ルールの CSV インポート

設定 UI のルール画面は、URL かドメインとプロファイル名の組を並べた CSV (UTF-8) からルールを作れます。他のツールの
設定を移す時のためのもので、取り込んだ結果は通常のルールです。

```csv
マッチURLまたはドメイン,プロファイル名
https://www.example.com,Personal
https://*.slack.com,Work
*.microsoftonline.com,Work
https://github.com/dotnet/*,Work
https://github.com/mayuki/*,Personal
```

- 1 列目がパターン、2 列目がプロファイル名。3 列目以降は無視する。RFC 4180 の引用 (`"a,b"`、`""`) を解釈し、BOM の
  有無は問わない。前後の空白は除く。空の行は無視する
- パターンがスキーム (`https://` のように `<英字で始まる英数字と + - .>://`) で始まれば `url` 条件、そうでなければ
  `host` 条件にする。どちらも `wildcard` で照合する (§2.7。`*` は任意の文字列、`?` は 1 文字)
  - `url` 条件は正規化した URL 全体と照合するため、パスの無いパターン (`https://*.slack.com`) はそのままでは
    何にも当たらない。パスが無ければ `/*` を補い、そのサイト全体に当てる (`https://*.slack.com/*`)。`/` で終わる
    パターンはそのまま (そのページだけ)
  - `host` 条件のパターンに `/`、`:`、空白があるものは取り込まない (ホスト名にはそれらが無く、当たらないため)
- プロファイル名は名前で照合する (前後の空白と大文字小文字は問わない。§2.3)。無いプロファイル名の行は取り込まない
- 1 行目が取り込めない場合は見出し行とみなし、取り込まなかった行に数えない
- 同じプロファイルの行は 1 つのルールにまとめる。名前は `Imported Rule for <プロファイル名>`、`id` は名前から作る
  (§2.10)。条件は行の順の `or` (1 行だけならその条件)、アクションはそのプロファイルの `open`。同じルールに同じ
  パターンが重なる行は取り込まない
- ルールは CSV に現れたプロファイルの順で、既存のルールの後 (キャッチオールの前) に有効な状態で加える。既存の
  ルールとはまとめない。同じ名前のルールがあれば名前に番号を付ける (`Imported Rule for Work 2`)
- 終わると、取り込んだ行の数、作ったルール、取り込まなかった行とその理由を表示する

## 3. `local.json`

```json
{
  "configPath": "~/Library/CloudStorage/OneDrive-Example/Apps/Preferis/config.json",
  "browserOverrides": {
    "chrome": { "path": "/Applications/Google Chrome Beta.app" }
  },
  "profileTargets": {
    "personal": { "browser": "safari" },
    "work": { "browser": "edge", "browserProfile": { "directory": "Profile 2", "name": "Contoso" } }
  },
  "logging": { "level": "error", "history": false, "historySize": 50 }
}
```

| プロパティ | 説明 |
| --- | --- |
| `configPath` | `config.json` の場所。環境変数と `~` を展開。未設定なら既定の場所 |
| `browserOverrides` | ブラウザー ID → `{ path, userDataDir? }`。検出より優先する。ポータブル版や特殊なインストール先向け。`path` は macOS では `.app`、Windows では実行ファイル (`"D:\\Portable\\Chrome\\chrome.exe"`)。Windows ではキーの無いポータブル版も検出結果に加わる |
| `profileTargets` | プロファイル `id` → このマシンで使うターゲット (§3.1) |
| `logging.level` | `off` / `error` / `info` / `trace` |
| `logging.history` | ルーティング履歴 (URL を含む) をローカルに保存するか。オプトイン。保存先はログディレクトリの `history.jsonl` (macOS: `~/Library/Logs/Preferis/`、Windows: `%LOCALAPPDATA%\Preferis\Logs\`) |
| `logging.historySize` | 履歴に残す件数 (既定 50)。古いものから消す |

### 3.1 `profileTargets` (このマシンのターゲット)

`config.json` のプロファイルのターゲット (§2.3) を、このマシンでだけ置き換えます。同じ OS の 2 台で使うブラウザーが違う、
同じ Edge でもマシンごとにアカウントや名前の違うブラウザープロファイルを使う、といった場合のためのものです。

| プロパティ | 型 | 説明 |
| --- | --- | --- |
| `browser` | string | このマシンのブラウザー ID (§2.4)。OS ごとの指定は無い (マシンの OS は決まっているため)。`command` と排他 |
| `browserProfile` | BrowserProfileRef | ブラウザープロファイル参照 (§2.5) |
| `private` | bool | プライベートモード |
| `extraArgs` | string[] | 追加のコマンドライン引数 |
| `command` | { path, args[] } | このマシンで実行するプログラム。`args` の `{url}` が置換される。`browser` と排他 |

- 置き換えは **ターゲット全体** で、共有のターゲットとは混ぜない (`config.json` に `private: true` があっても、ここに
  書かなければプライベートにならない)。`name` と `fallback` は `config.json` のものを使う
- プロファイルを解決する順は、このマシンの `profileTargets` → `config.json` の OS ごとの指定 → `config.json` の共通の
  指定 → `fallback`。`browser` も `command` も無い置き換えは「このマシンでは解決できない」扱いになり `fallback` に進む。
  共有のターゲットの無いプロファイル (§2.3、コンピューターごとに設定) も、置き換えの無いマシンでは同じく `fallback` に進む
- キーはプロファイルの `id`。見つからなければプロファイルの `previousIds` (新しい順) で探す (§2.11)。存在しない
  プロファイルの `id` のキーは無視する (設定 UI は残す。同期された `config.json` が後から追い付くことがあるため)
- 置き換えは同期されず `config.json` を見ても分からないため、設定 UI はプロファイルの一覧と編集画面、テストページに
  このコンピューターで実際に使うものを表示する

## 4. 互換性方針

- `version` を上げるのは互換性のない変更時のみ。読み込み側は未知のプロパティを `JsonExtensionData` で
  保持し、保存時にそのまま書き戻す (新しいバージョンのアプリが書いた設定を古いアプリが壊さない)。既知の `type` の
  条件・アクションに加わった未知のプロパティも同じく保持する (ルーティングには使わない)。
- 設定 UI は条件・アクションを編集のたびに作り直すが、未知のプロパティは作り直したものに引き継ぐ。アクションの追加・
  削除・並べ替え・設定の変更、条件の行の追加・削除・編集、ルールの並べ替えなどで、残った条件・アクションのものは失われない。
  条件エディターは行に識別子を持たないため、編集後の行を編集前の条件に、同じ位置で同じ内容 → 別の位置で同じ内容 →
  残りを順に、の順で対応付ける (内容の同じ行どうしは入れ替わることがある)。ただし次の場合は捨てる。
  - 種類が変わったもの (対象を Host から URL に変えた行、All から Any に変えたグループなど)。古い種類に向けて書かれた
    プロパティが別の種類で何を意味するかは分からないため。グループの種類が変わっても、その下の行のものは残る
  - 削除したもの、エディターが畳んで無くなったもの (1 行だけになった根の All / Any、1 行だけになった None of の `or`)
- 未知の `type` を持つ条件・アクションは内容をそのまま保持して書き戻す (`UnknownCondition` / `UnknownAction`)。
  ルーティング時、未知の条件は決してマッチせず、未知のアクションに到達した場合はルーティング失敗として
  フォールバックに進む (Design.md §6.1)。設定 UI では警告表示する。
- `type` プロパティはオブジェクト内のどの位置にあってもよい。
- 旧バージョンの設定を読む場合は読み込み時にマイグレーションを適用し、保存時に新形式で書き出す。
