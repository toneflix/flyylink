# Browser add-on

The add-on does what the desktop app can't do on its own, because only the browser can see inside a page:

- **Codes.** When a sign-in code arrives on the phone, it goes straight into the code box on the page, or waits by the box with a Fill button if the user prefers to be asked.
- **Calls and texts.** Clicking a `tel:` or `sms:` link, or right-clicking a selected number, calls or texts it from the phone.
- **Send to phone.** Right-click a picture, video, link or selection to send it to a phone, tablet or TV. Pictures and videos are downloaded by the computer and sent as files.
- **Open on phone.** The popup opens the current tab on the phone, at the same spot: the same second of a video on YouTube, Vimeo and Dailymotion, or the same paragraph elsewhere through a text fragment (`#:~:text=`).
- **Downloads.** Finished downloads can go to the phone, after asking or every time.
- **Play on TV.** A page's video plays on a network TV straight from its address. Sites that stream in pieces (`blob:` addresses) can't be played that way, so the page opens on a FlyyLink TV instead.
- **Tabs in the phone's media controls.** Tabs that play show up on the phone like any other player, with play, pause, skip, seek and artwork, and pause during calls.

It runs in Chrome, Edge, Brave, Firefox and other browsers built on Chrome, on Mac and Windows.

## How it fits together

```
Browser                         Computer                                     Phone
┌──────────────────┐  native    ┌──────────────────┐  loopback  ┌─────────┐  KDE Connect  ┌────────┐
│ add-on            │ messaging │ FlyyLinkBrowserHost│  TCP port  │ FlyyLink │  protocol    │ FlyyLink│
│ background worker │ ◀───────▶ │ (started by the    │ ◀────────▶ │ app      │ ◀──────────▶ │ app     │
│ content scripts   │  stdio    │  browser)          │  + token   │          │              │         │
└──────────────────┘           └──────────────────┘            └─────────┘              └────────┘
```

- The add-on never talks to the phone. It talks to the FlyyLink app on the same computer, which already has the pairing, the licence and the connection, so there is nothing to set up in the browser.
- The browser starts **the host**, a small helper inside the app: `FlyyLink.app/Contents/MacOS/FlyyLinkBrowserHost` on Mac and `FlyyLink.BrowserHost.exe` next to `FlyyLink.exe` on Windows. Native messaging frames each message with a 4-byte little-endian length. The host turns each message into one line of JSON on a loopback TCP connection to the app, and back.
- The app listens on a random loopback port and writes it, with a random token, to `endpoint.json`, readable only by the user: `~/Library/Application Support/FlyyLink/Browser/` on Mac and `%LOCALAPPDATA%\FlyyLink\Browser\` on Windows. The host's first line must carry the token, or the app drops the connection.
- While the app isn't running, the host tells the add-on `{"type":"state","status":"closed"}` and keeps trying. The popup's **Open FlyyLink** button sends `{"type":"launch"}`, and the host opens the app.
- The app writes the host manifests (`com.toneflix.flyylink.json`) on every launch, so they follow the app if it moves: into each installed browser's `NativeMessagingHosts` folder on Mac, and into `%LOCALAPPDATA%\FlyyLink\Browser\` plus the `HKCU\Software\<browser>\NativeMessagingHosts` keys on Windows. The uninstaller removes the keys.
- Only the add-on ids in `Browser.chromiumIds` / `Browser.ChromiumIds` and `Browser.firefoxId` may start the host.

### Messages

Add-on to app:

| Type       | Fields                                                                                    | What happens                                                    |
| ---------- | ----------------------------------------------------------------------------------------- | --------------------------------------------------------------- |
| `hello`    | `browser`                                                                                 | Names the browser, for player names like "YouTube in Chrome".   |
| `share`    | `id`, `device`, one of `link`, `text`, `file` (a path) or `fetch` (an address), `name`    | Sends it like the app's own Share. `fetch` is downloaded first. |
| `call`     | `id`, `device` (optional), `number`                                                       | Calls from the phone.                                           |
| `text`     | `id`, `device` (optional), `number`, `body`                                               | Texts from the phone.                                           |
| `playOnTv` | `id`, `tv`, `url`, `title`, `mime`                                                        | Plays the address on a network TV.                              |
| `media`    | `players: [{id, site, title, artist, album, artwork, playing, position, length, canSeek, canNext, canPrevious}]` | Every tab that plays. Times are in milliseconds. |

App to add-on:

| Type      | Fields                                                                                                    |
| --------- | --------------------------------------------------------------------------------------------------------- |
| `state`   | `status` (`ready`, `locked` or `closed`), `computer`, `platform`, `devices: [{id, name, type, canCall, canText}]`, `tvs: [{id, name}]` |
| `code`    | `code`, `app`, `device`: a one-time code just arrived in a phone notification.                            |
| `call`    | `active`: a call started or ended on a phone.                                                             |
| `control` | `player`, `action` (`PlayPause`, `Play`, `Pause`, `Next`, `Previous`, `Stop`), `seekTo` (milliseconds).   |
| `result`  | `id`, `ok`, `error`: the answer to a request with an `id`.                                                |

### Codes

`OneTimeCode` (in all three protocol libraries, with the same tests) finds the code in a phone notification. It needs a word like "code", "OTP" or "verification", skips amounts, dates, times and percentages, and picks the candidate closest to that word. The add-on finds the code box by `autocomplete="one-time-code"`, then by its name, label and shape, and handles a row of single-digit boxes. Codes stay on offer for three minutes.

### Media

The add-on wraps `navigator.mediaSession.setActionHandler` in the page (`src/page/media-session.js`, in the page's own world) so that it can press the site's own next and previous buttons, and reads titles and artwork from `navigator.mediaSession.metadata`. The app adds the tabs to the players it already shares (`MediaHostPlugin`), named like "YouTube in Chrome", and fetches artwork for the phone, since the phone can't reach the page. On Windows, the browser's own system media session for the same tab is left out, so it isn't listed twice.

On the phone, `ComputerMedia` shows whatever a computer plays in the phone's own media controls, through a `MediaSession` and a media notification. The phone's `MediaPlugin` leaves FlyyLink's own session out, so it never sends a computer's media back to it.

## Building and trying it

```sh
cd browser
npm test                      # the shared code's tests
npm run build                 # dist/chrome and dist/firefox
```

- **Chrome, Edge and other browsers built on Chrome:** open `chrome://extensions`, turn on Developer mode, choose **Load unpacked** and pick `browser/dist/chrome` (or `browser/src`). The `key` in the manifest gives it the id `dnlkpekmfbjelcenommpiibkcdbooacj`, which the apps allow.
- **Firefox:** open `about:debugging#/runtime/this-firefox`, choose **Load Temporary Add-on** and pick `browser/dist/firefox/manifest.json`. Firefox asks for permission to read pages before codes and numbers work; the popup has a button for it.

The FlyyLink app must be installed and started once, so the host manifests exist. On Mac, run it as a bundle (`./scripts/bundle.sh`), since the host lives inside it.

## Publishing

`npm run store` builds `dist/flyylink-chrome-<version>.zip` and `dist/flyylink-firefox-<version>.zip`, without the development key. CI builds them on every pull request too. Bump `version` in `browser/src/manifest.json` for each upload.

1. **Chrome Web Store** ($5 once, at <https://chrome.google.com/webstore/devconsole>): upload the Chrome zip. Use the privacy policy at <https://flyylink.toneflix.net/privacy>. Explain each permission: native messaging talks to the FlyyLink app; all sites, to fill codes and find phone numbers; downloads, to send finished downloads; context menus, notifications and storage for the menus, prompts and settings. Single purpose: "Use your Android phone from the browser through the FlyyLink app."
2. **Microsoft Edge Add-ons** (free, at <https://partner.microsoft.com/dashboard/microsoftedge>): upload the same Chrome zip.
3. **Firefox Add-ons** (free, at <https://addons.mozilla.org/developers>): upload the Firefox zip. AMO may ask for the source; send the `browser` folder, which has no build step beyond copying.
4. Once Chrome and Edge give their ids, add both to `Browser.chromiumIds` in `macos/Sources/FlyyLinkKit/Browser.swift` and `Browser.ChromiumIds` in `windows/src/FlyyLink.Core/Browser.cs`, update the tests, and release the apps. Until then only the unpacked add-on can reach the app. Firefox keeps `browser@flyylink.toneflix.net` everywhere, so it works from the first upload.
5. Link the store pages from the website and the apps.
