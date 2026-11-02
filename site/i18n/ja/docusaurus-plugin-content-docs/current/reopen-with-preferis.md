---
title: Preferis で URL を開き直す
---

ブラウザーで表示しているページを Preferis でもう一度振り分けて、適切なブラウザーで開き直したいといった場合は `preferis` スキームの URL を使用できます。

:::tip
`preferis` スキームの URL は登録済みであれば Preferis を既定のブラウザーに設定していなくても使用できます。
:::

## `preferis` スキームの URL 形式
`preferis` スキームでは次の2種類の URL 形式を受け付けます。

| 形式 | 例 |
| --- | --- |
| `preferis:<URL>` | `preferis:https://example.com/?q=1` |
| `preferis://open?url=<エンコードした URL>` | `preferis://open?url=https%3A%2F%2Fexample.com%2F%3Fq%3D1` |

### `preferis:<URL>`
振り分ける URL の前に `preferis:` を付けます。URL はそのまま使用されます

### `preferis://open?url=<エンコードした URL>`
URL を JavaScript の `encodeURIComponent` などでパーセントエンコードします。エンコードされていない場合、URL が最初の `&` や `#` で切れたまま開かないよう、Preferis はエラーを表示します

### 注意
このスキームで渡すことのできる URL のスキームは `http` と `https` だけです。セキュリティー上の制限からファイルなどほかの種類の URL はエラーとなります。

## テスト
設定の「テスト」ページに `preferis:` の URL を入力すると、中の URL がどのように振り分けられるかを確認できます。

## ブックマークレットで表示中のページを開き直す
次の文字列を URL としてブックマークに追加します。ブックマークをクリックすると表示中のページを Preferis に渡して開きなおせます。

```
javascript:location.href='preferis://open?url='+encodeURIComponent(location.href)
```

初回はブラウザーが Preferis を開くかどうかを確認します。

:::tip
`preferis:` の URL を開いたアプリは、通常はクリックしたブラウザーになります。URL を開いたアプリの条件を使うと、例えば Chrome から渡したページを Firefox で開くといったようにブラウザーごとに開き先を変えられます。詳しくは [URL を開いたアプリによって振り分ける](./rule/condition-opened-by.md) を参照してください。
:::

## `preferis` スキームの登録
### Windows
「一般」ページの「既定のブラウザー」で「既定にする」をクリックすると `preferis:` も登録されます。

開いた Windows の設定画面で Preferis を選択する必要はありません。以前のバージョンで登録していた場合は「一般」ページに Preferis が未登録と表示されるので、再度「既定にする」をクリックしてください。

### macOS
初めて Preferis.app を開いた後など、macOS が Preferis.app を認識した時点以降で使用可能となります。
