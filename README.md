<p align="center">
  <img src="branding/app-icon.svg" width="112" alt="FlyyLink">
</p>

<h1 align="center">FlyyLink</h1>

<p align="center"><strong>Your Android phone and your computer, finally on speaking terms.</strong></p>

<p align="center">For Android, Mac, Windows and TV. Free on Android and TV. Try it free on your Mac or PC.</p>

---

![The FlyyLink panel on a Mac with two phones paired, a call coming in, and the Messages window](web/public/screens/mac-overview.png)

## What it does

- **Notifications.** See your phone's notifications on your computer and reply right from your keyboard. Your computer's show up on your phone too.
- **Texts and calls.** Read and reply to texts, and answer calls without reaching for your phone.
- **Files.** Send files of any size both ways, straight from any app.
- **FlyyDrop.** Share with people nearby, even if you've never paired with them.
- **Screen mirroring.** See your phone's screen on your computer and use it with your mouse and keyboard.
- **Photos.** See the latest photos from your phone on your computer.
- **Phone storage.** Browse your phone's folders in Finder or File Explorer, like any other drive.
- **Clipboard.** Copy something on your phone and paste it on your computer, or the other way round.
- **Find your phone.** Make it ring, even when it's on silent.
- **Music, battery and signal.** Control what's playing on your phone and keep an eye on its battery and signal.
- **Computer remote.** Use your phone as a touchpad, a keyboard or a clicker for your slides.
- **TV remote.** Control your TV from your phone or computer, and type with a real keyboard.
- **Play on TV.** Show your photos, videos and music on your smart TV.
- **Browser add-on.** Sign-in codes from your phone fill themselves in, phone numbers on pages call from your phone, and the videos playing in your tabs show up in your phone's media controls.
- **Several devices.** Connect as many phones and computers as you like.
- **No Wi-Fi? No problem.** FlyyLink works over your phone's hotspot too.

## Privacy

Your devices talk to each other directly, and everything between them is encrypted. Nothing goes through our servers, and there are no ads or tracking.

## Get it

Get the phone app on [Google Play](https://play.google.com/store/apps/details?id=com.toneflix.flyylink) (Fire TV and Fire tablets: [Amazon Appstore](https://www.amazon.com/gp/mas/dl/android?p=com.toneflix.flyylink)) and the computer apps from [flyylink.toneflix.net](https://flyylink.toneflix.net/#download). [Buy a monthly, annual or lifetime plan](https://flyylink.toneflix.net/buy) for your computers.

|             | Needs                      | How it installs              |
| ----------- | -------------------------- | ---------------------------- |
| **Android** | Android 8.0 or later       | Google Play                  |
| **TV**      | Google TV or Fire TV       | Google Play, Amazon Appstore |
| **Mac**     | macOS 13 Ventura or later  | Signed app, updates itself   |
| **Windows** | Windows 10 (1809) or later | Installer, updates itself    |

### Setting up

1. Put your devices on the same Wi-Fi, or connect your computer to your phone's hotspot.
2. Open FlyyLink on each of them.
3. Tap **Pair** and check the code matches on both screens.
4. On your phone, allow notification access and turn on any other features you want.

If the connection drops while your phone is idle (this happens a lot on Tecno, Infinix, itel and Xiaomi phones), let FlyyLink run in the background in your battery settings.

If you use KDE Connect, close it while you set up FlyyLink, because they both use the same network port.

## For developers

FlyyLink is three native apps sharing one protocol.

| Folder                      | What's inside                                                                                                                                                                                                                          |
| --------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `android/protocol`          | The protocol in plain Kotlin: packets, identity, pairing, discovery.                                                                                                                                                                   |
| `android/app`               | The Android app (Kotlin, Jetpack Compose).                                                                                                                                                                                             |
| `macos/Sources/FlyyLinkKit` | The protocol in Swift.                                                                                                                                                                                                                 |
| `macos/Sources/FlyyLink`    | The Mac menu bar app (SwiftUI).                                                                                                                                                                                                        |
| `windows/src/FlyyLink.Core` | The protocol in C#.                                                                                                                                                                                                                    |
| `windows/src/FlyyLink`      | The Windows tray app (WinUI 3).                                                                                                                                                                                                        |
| `browser`                   | The browser add-on for Chrome, Edge and Firefox. See [docs/BROWSER.md](docs/BROWSER.md).                                                                                                                                               |
| `web`                       | The website (Next.js), its worker, and the `flyylink-cli` CLI in `web/cli`.                                                                                                                                                            |
| `branding`                  | The logo and the scripts that generate every app icon.                                                                                                                                                                                 |
| `docs`                      | [Architecture](docs/ARCHITECTURE.md), [roadmap](docs/ROADMAP.md), [releasing](docs/RELEASING.md), [licences](docs/LICENCES.md), [admin CLI](docs/ADMIN.md), [website hosting](docs/WEBSITE.md), the [Windows app](docs/WINDOWS.md) and the [browser add-on](docs/BROWSER.md). |

**Android** (JDK 17 and the Android SDK):

```sh
cd android
./gradlew :protocol:test        # protocol tests, no Android SDK needed
./gradlew :app:installDebug     # build and install on a connected phone
```

**Mac** (Xcode 15 or later):

```sh
cd macos
swift test
./scripts/bundle.sh             # builds macos/build/FlyyLink.app
open build/FlyyLink.app
```

Run it as a bundle, or notifications and the Share menu won't work.

**Windows** (.NET 8 SDK, and Windows for the app itself):

```sh
cd windows
dotnet test tests/FlyyLink.Core.Tests    # runs on any OS
```

See [windows/README.md](windows/README.md) for building the app and the installer, and [docs/WINDOWS.md](docs/WINDOWS.md) for what it does.

**Browser add-on** (Node 20 or later):

```sh
cd browser
npm test
npm run build                   # load browser/dist/chrome or browser/dist/firefox unpacked
```

**Website** (Node 20 or later):

```sh
cd web
npm install
npm run dev
```

### Releasing

Push a tag like `1.2.3` to release all three apps, or `android-1.2.3`, `macos-1.2.3`, `windows-1.2.3` for one. See [docs/RELEASING.md](docs/RELEASING.md) for the secrets and where the files go.

## License

Proprietary. © Toneflix Technologies Limited. See [LICENSE](LICENSE).
