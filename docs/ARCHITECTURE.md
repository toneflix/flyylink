# Architecture

Three apps, one protocol. The Windows app (`windows/`) mirrors the Mac app: `FlyyLink.Core` is the C# port of FlyyLinkKit, and a WinUI 3 tray app takes the place of the menu bar app.

```
Android phone                                  Mac
┌──────────────────────────┐                  ┌──────────────────────────┐
│ app (Compose UI)          │                  │ FlyyLink (SwiftUI menu bar) │
│ LinkService (foreground)  │                  │ AppModel                  │
│ feature plugins           │                  │ feature plugins           │
├──────────────────────────┤   Wi-Fi / LAN    ├──────────────────────────┤
│ protocol (Kotlin)         │ ◀──────────────▶ │ FlyyLinkKit (Swift)         │
│ packets, pairing, LanLink │   TLS over TCP   │ packets, pairing, LanLink │
└──────────────────────────┘                  └──────────────────────────┘
```

The protocol code exists three times, in Kotlin, Swift and C#, with the same names and the same tests. When you change one, change the others.

## Why KDE Connect's protocol

It already solves discovery, pairing, encryption and file transfer, and it is documented and widely used. Speaking it means FlyyLink works with KDE Connect on Linux, Windows and Android from day one. We add our own packet types (prefixed `flyylink.`) for features KDE Connect lacks. A peer only gets those if it lists them in its capabilities, so plain KDE Connect clients are unaffected.

We do not copy KDE Connect's code, which is GPL. We implement the protocol ourselves, so the code is all ours to license.

## Protocol summary (version 8)

**Packets.** Every message is one line of JSON ending in `\n`:

```json
{"id": 1700000000000, "type": "kdeconnect.ping", "body": {}}
```

A packet that carries a file adds `payloadSize` and `payloadTransferInfo: {"port": N}`. The receiver opens a separate TLS connection to that port to download the bytes.

**Discovery.**

1. Each device broadcasts a `kdeconnect.identity` packet over UDP to port 1716. It holds the device id, name, type, protocol version, capabilities and the TCP port it listens on (from 1716 to 1764).
2. A device that hears a broadcast opens a TCP connection to that port and sends its own identity in plain text. It skips devices it already has an open link to.
3. If both devices connect at the same moment, both keep the link opened by the device with the larger id and close the other. Any later link replaces the old one, because a peer only reconnects when its old link died.

**Encryption.** Each device has a self-signed certificate whose common name is its device id. After the plain-text identity, both sides start TLS on the same socket. The device that *opened* the TCP connection acts as the TLS *server*. Once TLS is up, version 8 has both sides send their identity again over the encrypted link. A paired device's certificate is saved and must match on every later connection.

**Heartbeat (`flyylink.heartbeat`).** Every 5 seconds each side sends `{"pong": false}` and the other answers at once with `{"pong": true}`. A link that receives nothing for 30 seconds is closed, so discovery can reconnect. A sleeping phone wakes to answer even when its own timer is paused. Only peers that list the packet in their capabilities take part.

**Several devices at once.** Each side keeps one link per device id, so a Mac stays connected to every paired phone and a phone to every paired Mac at the same time. Broadcast features (notifications, clipboard, battery, calls, media) reach all of them. Features about one device (the main screen, Messages, Photos, call audio, the Share menu) follow `DeviceFocus`: the device the user picked, or else the first paired one by name. Call audio stays with the phone it is linked to over Bluetooth while that phone is connected.

**Computers with each other.** A Mac or PC pairs with another Mac or PC the same way it pairs with a phone, so Mac↔PC, Mac↔Mac and PC↔PC all work. Phone-only features (Messages, Photos, calls, call audio, the hotspot) follow the focused *phone* and skip computers. Between computers: clipboard, files and links work as with a phone; each computer answers `kdeconnect.battery.request` with its own battery; `kdeconnect.findmyphone.request` plays a looping sound until someone answers it; `kdeconnect.mpris.request` controls what the computer plays (Music and Spotify over AppleScript on a Mac, the Windows media flyout's sessions on a PC). A PC also sends its own notifications to paired computers, but Windows only lets apps with package identity read them, and macOS doesn't let any app read them, so a Mac's notifications never go to a PC.

**Shared keyboard and mouse (`flyylink.input`).** Each computer stores which of its edges a paired computer sits beyond. When the pointer leaves through that edge, it sends `enter` with the edge on the other side and how far along it, then swallows local input and sends `move` (relative), `button`, `scroll` and `key` packets. Keys travel as W3C `code` names ("KeyA", "ShiftLeft") and each app maps them to its own key codes; between a Mac and a PC, Control and Meta swap so Ctrl+C becomes Command-C. The controlled computer posts the input and, when its pointer goes back out through the edge it came in by, sends `leave`, which hands control back. `release` lets go of anything held. A dropped link or three presses of Escape always returns control. A computer that is entered through an edge learns the other one sits there. The Mac uses an event tap and needs Accessibility access; Windows uses low-level hooks and `SendInput`.

**Another computer's screen.** The viewer sends `flyylink.screen.request` like it does to a phone. A computer answers with `flyylink.screen` carrying `codec: "jpeg"` and its `platform`, and a live payload where every `ScreenFrame` is one whole JPEG picture flagged as a key frame. The sender writes pictures to a `PayloadStream` (Swift) or `LiveStream` (C#) and drops them while the viewer is still busy, so a slow link skips pictures instead of lagging. The Mac records with ScreenCaptureKit and needs Screen Recording access; Windows copies the primary screen with GDI and draws the pointer in. The viewer works the other computer with `flyylink.input`: `moveTo` with fractions of the picture, plus `button`, `scroll` and `key`, swapping Control and Meta when the platforms differ. Closing the window sends `release` and `stop`. Showing the screen follows the same "Let my computers control this Mac" (or "this PC") setting as the keyboard and mouse.

**Phones through another computer (`flyylink.relay`).** A computer passes its phone's notifications and call updates to its paired computers, wrapped with the phone's id and name. A computer that is connected to that phone itself drops them; one that isn't shows them as "Phone via Computer" and remembers which computer the phone is reached through. Replies, notification buttons, dismissals and call buttons go back the same way, wrapped again, and the first computer hands them to the phone. Only those packet types are relayed, and payloads such as notification icons are not. A setting on each computer turns relaying off.

**FlyyDrop (`flyylink.flyydrop.*`).** AirDrop-style sharing with any nearby FlyyLink device, paired or not. Discovery already links every FlyyLink device on the network over TLS, so FlyyDrop needs no new transport. The sender sends `flyylink.flyydrop.request` with an `id`, the `files` (name and size of each) or a `text` (a link or some text). The receiver shows who is asking and what, and answers `flyylink.flyydrop.response` with `accept` true or false and a `reason` (`declined`, `unavailable` when it isn't receiving from unpaired devices, `busy` while it is already asking about another request from that device). Once accepted, the items travel as ordinary `kdeconnect.share.request` packets tagged `flyylink.flyydrop: id`, so progress and saving work as they do for paired devices. Either side can send `flyylink.flyydrop.cancel`. `Device` asks its `GuestAccess` before delivering or sending any packet of an unpaired device, and `FlyyDrop` is that authority: it lets in requests and cancels, the answer to our own open request, and each accepted file once, with the declared name and exact size. Nothing else from an unpaired device reaches a plugin, so a FlyyDrop sender gets no notifications, clipboard, battery or control. Each device chooses who may ask: paired devices only, everyone for 10 minutes, or everyone. Unanswered requests expire after a minute; an accepted request expires after an hour without a new item.

**TVs.** The Android app also runs on Android TV, Google TV and Fire TV, from the same build. It announces itself as `deviceType: "tv"` with `Capabilities.tv`, which leaves out texts, calls, clipboard, battery, location, photos and screen sharing. Phones and computers pair with a TV like with any device. What a TV receives (`kdeconnect.share.request`) opens full screen: photos, videos and music in its own viewer, links in the app that handles them. Phone notifications show as a small card in the corner. A phone works the TV as a remote with `flyylink.remote` (`TvRemote`): `key` with `up`, `down`, `left`, `right`, `select`, `back`, `home`, `menu`, `playPause`, `next`, `previous`, `volumeUp`, `volumeDown`, `mute`, `delete` or `enter`, and `text` for typing. Volume and media keys work on their own; moving focus, Back, Home and typing go through the "FlyyLink remote" accessibility service the user turns on on the TV. The phone shows what the TV plays from `kdeconnect.mpris`, and computers control it like a phone's player. Without notification access a TV offers one player, "TV", worked with media keys. Opening things and pop-ups over other apps need "Display over other apps" on the TV.

**Pairing.** A sends `kdeconnect.pair` with `pair: true` and `timestamp` (Unix seconds). B shows a prompt and a short verification code derived from both certificates and the timestamp, so the user can check the same code shows on both screens. B answers `pair: true` to accept or `pair: false` to reject. Either side sends `pair: false` later to unpair. Requests time out after 30 seconds. `PairingStateMachine` implements this on both sides.

**Play on TV (DLNA).** Phones and computers play a photo, video or song on smart TVs that take media over DLNA, such as Hisense VIDAA, Samsung and LG, with no app on the TV. This is plain UPnP, not our protocol. `TvFinder` sends an SSDP `M-SEARCH` for `MediaRenderer` and `AVTransport` to 239.255.255.250:1900 and reads each answer's device description into a `TvRenderer`. `MediaServer` serves the file over HTTP on a random port with Range support, at a random path, from the local address that faces the TV. `TvPlayer` sends `AvTransport` SOAP actions (`SetAVTransportURI` with DIDL-Lite metadata, `Play`, `Pause`, `Stop`, `Seek`) and polls `GetPositionInfo` and `GetTransportInfo` about once a second. Each app keeps one session (`PlayOnTv`, `PlayOnTvModel`, `PlayOnTvState`) and ends it when the TV stops on its own. The apps search every 30 seconds and show Play on TV only while a TV is around; `TvList` drops a TV once it misses two searches in a row.

## Licence

The Android app is free. The Mac and Windows apps need a lifetime licence after a 14-day trial; the licence belongs to an email account and covers up to 5 computers. The website's worker (`web/worker/licence`, setup in [LICENCES.md](LICENCES.md)) sells it through Paddle, signs people in with emailed codes, and hands each computer an ECDSA P-256 token naming its scrambled hardware ID (`MachineId`). The apps check the token offline with a public key built in at release (`LICENCE_PUBLIC_KEY`), renew it every few hours, and keep working 37 days without the internet. `LicenceRules` (Swift and C#, same tests and fixtures) decides between unlimited (no key built in), trial, licensed and locked. While locked, the app doesn't start `LanLink`, so it neither finds nor connects to devices. When they check in, the apps also fetch notices the team sends with the [admin CLI](ADMIN.md) and show them as system notifications.

## What is done and what is next

Done:

- Packet format, identity, name rules and the pairing state machine, with tests, in Kotlin and Swift.
- LAN discovery, plain-text identity exchange, and the TLS upgrade with v8 identity re-exchange (`LanLink`).
- Self-signed RSA 2048 certificates per device (`DeviceCertificate`), stored on first launch.
- Trusted devices (`TrustStore`): a paired device whose certificate changes is refused.
- `Device`: wraps a link with pairing, and drops packets from unpaired devices.
- Pairing UI with the verification code on both apps.
- The Mac side runs on SwiftNIO and swift-nio-ssl, because Network.framework cannot start TLS on a connection that is already open.

- `Plugin` and `PluginRouter`: each feature declares the packet types it accepts, and the router hands packets from paired devices to it.
- Notifications plugin: Android sends `kdeconnect.notification` from a `NotificationListenerService`; the Mac shows them in Notification Center with a Reply field, and sends back `kdeconnect.notification.reply` or `kdeconnect.notification.request` (`cancel`) on dismiss. On pairing the Mac asks for all current notifications (`request: true`).

Next, in order:

1. Check against a real KDE Connect install: the verification code derivation and certificate fields.
2. Notification app icons (sent as payloads), then ping, battery, clipboard and share (files and links).

## Platform limits to design around

- **RCS:** Android gives other apps no RCS access. RCS chats can only be mirrored through their notifications. SMS and MMS work through the SMS provider.
- **Google Play and SMS or call log permissions:** these are restricted to default SMS or phone apps and a few exceptions, including companion apps for connected devices. Expect a review and a written justification.
- **Call audio (experimental):** Android does not let apps capture call audio, so the Mac acts as a Bluetooth hands-free device for the phone, as Phone Link does on Windows. `HandsFreeAudio` uses `IOBluetoothHandsFreeDevice`: once the phone and Mac are paired over Bluetooth, the Mac connects as a headset and Android sends call audio to it by itself. `flyylink.callaudio.request` (`status` or `pair`, with the Mac's Bluetooth address) lets the Mac ask how the phone's side stands or have the phone start pairing; the phone answers and reports changes with `flyylink.callaudio`. Answering from the Mac goes over Bluetooth so the audio comes to the Mac; the call banner moves the audio between the Mac and the phone and mutes the microphone in use (`mute`/`unmute` in `flyylink.call.request` for the phone's). Audio is phone-call quality, and `IOBluetoothHandsFreeDevice` is an old API that some macOS versions handle unreliably.
- **Background limits:** the Android side runs as a `connectedDevice` foreground service. Later we should use `CompanionDeviceManager` so the system keeps the service alive and grants extra background rights.
- **macOS local network privacy:** a bundled app needs `NSLocalNetworkUsageDescription` and Bonjour entries in its Info.plist. When we move from `swift run` to a signed `.app`, add these.
