<p align="center">
  <img src="branding/app-icon.svg" width="112" alt="FlyyLink">
</p>

<h1 align="center">FlyyLink</h1>

<p align="center"><strong>Your Android phone and your computer, finally on speaking terms.</strong></p>

<p align="center">For Android, Mac, Windows and TV. Free on Android and TV. Mac and PC: 14 days free, then pay once.</p>

---

![The FlyyLink panel on a Mac with two phones paired, a call coming in, and the Messages window](web/public/screens/mac-overview.png)

## What it does

- **Notifications.** Reply from your keyboard.
- **Texts and calls.** Read, reply, answer.
- **Files.** Any size, from any app.
- **FlyyDrop.** Send to anyone nearby.
- **Screen mirroring.** Use your phone from your computer.
- **Photos.** Your latest shots, on your computer.
- **Clipboard.** Copy on one, paste on the other.
- **Find your phone.** Ring it, even on silent.
- **Media and battery.** Play, pause, skip, and check the battery.
- **Computer remote.** Your phone is a touchpad, keyboard and slide clicker.
- **TV remote.** Your phone or computer is the remote. Type and point, too.
- **Play on TV.** Photos, videos and music on your smart TV.
- **Several devices.** Pair as many phones and computers as you like.
- **No shared Wi-Fi?** Connects through your phone's hotspot.

## Privacy

Nothing goes through us, and no tracking. Your devices talk to each other directly, encrypted.

## Get it

Get the phone app on [Google Play](https://play.google.com/store/apps/details?id=com.toneflix.flyylink) and the computer apps from [flyylink.toneflix.net](https://flyylink.toneflix.net/#download). [Buy a lifetime licence](https://flyylink.toneflix.net/buy) for your computers.

|             | Needs                      | How it installs              |
| ----------- | -------------------------- | ---------------------------- |
| **Android** | Android 8.0 or later       | Google Play                  |
| **TV**      | Google TV or Fire TV       | Google Play                  |
| **Mac**     | macOS 13 Ventura or later  | Signed app, updates itself   |
| **Windows** | Windows 10 (1809) or later | Installer, updates itself    |

### Setting up

1. Put your devices on the same Wi-Fi, or join your phone's hotspot.
2. Open FlyyLink on each.
3. Tap **Pair** and check the code matches.
4. On the phone, allow notification access and any extras you want.

If the connection drops when the phone is idle (common on Tecno, Infinix, itel and Xiaomi), let FlyyLink run in the background in your battery settings.

Close KDE Connect while setting up; both use the same network port.

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
| `web`                       | The website (Next.js), its worker, and the `flyylink-cli` CLI in `web/cli`.                                                                                                                                                            |
| `branding`                  | The logo and the scripts that generate every app icon.                                                                                                                                                                                 |
| `docs`                      | [Architecture](docs/ARCHITECTURE.md), [roadmap](docs/ROADMAP.md), [releasing](docs/RELEASING.md), [licences](docs/LICENCES.md), [admin CLI](docs/ADMIN.md), [website hosting](docs/WEBSITE.md) and the [Windows app](docs/WINDOWS.md). |

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
