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
| `id` | string | 一意な識別子。ルールから参照する。UI が名前から生成する (人間可読な slug、§2.10)。名前を変えても変わらない |
| `name` | string | 表示名 |
| `browser` | string \| { windows?, macos? } | ブラウザー ID (§2.4)。`command` と排他 |
| `browserProfile` | BrowserProfileRef | ブラウザープロファイル参照 (§2.5)。`browser` 指定時のみ |
| `private` | bool | プライベート/シークレットモードで開く。ブラウザーごとに適切なフラグに変換 |
| `extraArgs` | string[] | 追加のコマンドライン引数 (例: `--new-window`) |
| `command` | { windows?, macos? } | 任意の実行ファイル。各 OS に `{ path, args[] }`。`args` 内の `{url}` が置換される。`browser` と排他 |
| `fallback` | string | このマシンで解決できない時に代わりに使うプロファイル `id` |

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
{ "type": "open", "profiles": ["chrome-work"] }         // profiles 省略または空 = デフォルトプロファイル。複数ならこの順でピッカーに並ぶ
{ "type": "system" }
{ "type": "none" }
{ "type": "rewrite", "mode": "regex", "pattern": "^http://", "replacement": "https://" }
{ "type": "rewrite", "mode": "queryParameter", "parameter": "url" }
{ "type": "reroute" }
```

`rewrite` の結果が有効な絶対 URL でない場合はルーティング失敗として扱います。`queryParameter` モードは
値をパーセントデコードしますが `+` を空白には変換しません (値に埋め込まれた URL の `+` を壊さないため)。
`regex` モードは正規化済みの絶対 URL (小文字のスキームとホスト、既定ポート除去) に対して置換します。

### 2.9 settings

| プロパティ | 既定 | 説明 |
| --- | --- | --- |
| `picker.position` | `cursor` | `cursor` / `center` |
| `picker.autoSelectSeconds` | `0` | 0 で無効。N 秒無操作で選択中の候補 (初期状態は先頭) を選択。キーやマウスの操作でカウントはやり直し。自動選択では「記憶する」は働かない |
| `picker.rememberChoiceDefault` | `false` | 「記憶する」チェックの初期状態 |
| `language` | `auto` | `auto` / `en` / `ja`。M3 時点の UI は英語のみで、値は保存されるだけ |

### 2.10 ID の生成

設定 UI がプロファイル・ルールを作る時の `id` は、名前から次の規則で作ります。手で書く場合はこの形に従う必要はなく、
一意であれば何でも構いません。

- ASCII の英数字は小文字にして残し、それ以外の文字の並びは `-` 1 つにする。先頭と末尾の `-` は除く。最大 48 文字
  (`Chrome (Work)` → `chrome-work`)
- 何も残らなければ (日本語だけの名前など) `profile` / `rule` / `command`
- 既に使われていれば `-2`, `-3` ... を付ける
- ピッカーの「記憶する」が作るルールは `remember-<host>-<ランダム 8 桁>`

## 3. `local.json`

```json
{
  "configPath": "~/Library/CloudStorage/OneDrive-Example/Apps/Preferis/config.json",
  "browserOverrides": {
    "chrome": { "path": "/Applications/Google Chrome Beta.app" }
  },
  "logging": { "level": "error", "history": false, "historySize": 50 }
}
```

| プロパティ | 説明 |
| --- | --- |
| `configPath` | `config.json` の場所。環境変数と `~` を展開。未設定なら既定の場所 |
| `browserOverrides` | ブラウザー ID → `{ path, userDataDir? }`。検出より優先する。ポータブル版や特殊なインストール先向け |
| `logging.level` | `off` / `error` / `info` / `trace` |
| `logging.history` | ルーティング履歴 (URL を含む) をローカルに保存するか。オプトイン。保存先はログディレクトリの `history.jsonl` (macOS: `~/Library/Logs/Preferis/`、Windows: `%LOCALAPPDATA%\Preferis\Logs\`) |
| `logging.historySize` | 履歴に残す件数 (既定 50)。古いものから消す |

## 4. 互換性方針

- `version` を上げるのは互換性のない変更時のみ。読み込み側は未知のプロパティを `JsonExtensionData` で
  保持し、保存時にそのまま書き戻す (新しいバージョンのアプリが書いた設定を古いアプリが壊さない)。
- 未知の `type` を持つ条件・アクションは内容をそのまま保持して書き戻す (`UnknownCondition` / `UnknownAction`)。
  ルーティング時、未知の条件は決してマッチせず、未知のアクションに到達した場合はルーティング失敗として
  フォールバックに進む (Design.md §6.1)。設定 UI では警告表示する。
- `type` プロパティはオブジェクト内のどの位置にあってもよい。
- 旧バージョンの設定を読む場合は読み込み時にマイグレーションを適用し、保存時に新形式で書き出す。
