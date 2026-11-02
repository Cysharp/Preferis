---
title: Reopening URLs with Preferis
sidebar_position: 8
---

To route the page you are viewing in a browser through Preferis again and reopen it in the right browser, you can use a URL with the `preferis` scheme.

:::tip
Once the scheme is registered, `preferis` URLs work even when Preferis is not your default browser.
:::

## Formats of `preferis` URLs
The `preferis` scheme accepts two URL formats.

| Format | Example |
| --- | --- |
| `preferis:<URL>` | `preferis:https://example.com/?q=1` |
| `preferis://open?url=<encoded URL>` | `preferis://open?url=https%3A%2F%2Fexample.com%2F%3Fq%3D1` |

### `preferis:<URL>`
Put `preferis:` in front of the URL to route. The URL is used as it is.

### `preferis://open?url=<encoded URL>`
Percent-encode the URL, for example with JavaScript's `encodeURIComponent`. If the URL is not encoded, Preferis shows an error instead of opening the URL cut off at its first `&` or `#`.

### Note
The only URL schemes you can pass with this scheme are `http` and `https`. For security reasons, other kinds of URLs, such as files, result in an error.

## Testing
Enter a `preferis:` URL on the **Test** page of the settings to see how the URL inside it is routed.

## Reopening the current page with a bookmarklet
Add a bookmark with the following text as its URL. Clicking the bookmark passes the page you are viewing to Preferis and reopens it.

```
javascript:location.href='preferis://open?url='+encodeURIComponent(location.href)
```

The first time, the browser asks whether to open Preferis.

:::tip
The app that opened a `preferis:` URL is usually the browser you clicked in. With a condition on the app that opened the URL, you can change where pages open for each browser, for example opening pages passed from Chrome in Firefox. See [Routing by the App That Opened the URL](./rule/condition-opened-by.md).
:::

## Registering the `preferis` scheme
### Windows
Clicking **Make Default** under **Default Browser** on the **General** page registers `preferis:` as well.

You do not have to choose Preferis in the Windows Settings page that opens. If you registered Preferis with an earlier version, the **General** page shows that Preferis is not registered; click **Make Default** again.

### macOS
The scheme is available once macOS knows about Preferis.app, for example after you open it for the first time.
