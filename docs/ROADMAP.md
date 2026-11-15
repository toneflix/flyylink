# Roadmap

The goal is an app better than both KDE Connect and Microsoft Phone Link. Each stage ends with something usable.

## 1. Connected and trusted
- TLS with saved device certificates
- Pairing with a verification code on both screens
- Reconnect on its own when both devices are on the same network

## 2. MVP: the everyday basics
- Notifications from the phone on the Mac, with reply and dismiss
- Send files and links both ways, from the Mac share menu and the Android share sheet
- Shared clipboard
- Battery level and low-battery alerts in the menu bar
- Media controls for whatever plays on the phone
- Ring my phone

## 3. Messages and calls
- SMS and MMS: read threads, send, get new messages live
- Calls: see who's calling, answer, reject, dial from the Mac
- Call audio on the Mac, with the Mac as a Bluetooth hands-free device (experimental, needs testing on real Macs)

## 4. Where we beat KDE Connect and Phone Link
These are the reasons someone would pick FlyyLink. Packets for them use the `flyylink.` prefix.

- **A native Mac feel.** Notification Center integration, Share menu, Finder sidebar, Handoff-style "continue on Mac" for links and documents. KDE Connect's Mac port is rough and Phone Link has no Mac version.
- **Works without Wi-Fi.** Bluetooth Low Energy to wake the link and the phone's hotspot as a fallback, so it works on trains and in cafés with captive portals.
- **Fast large transfers.** Parallel streams and resumable transfers for photos and videos.
- **Phone screen and apps on the Mac.** Screen mirroring with keyboard, mouse and drag-and-drop, built on the approach scrcpy uses.
- **Photos.** Recent photos and screenshots show up on the Mac right away.
- **Use the phone's camera as a Mac webcam.**
- **Universal clipboard with images and files,** not just text.
- **Auto-unlock:** unlock the Mac when the paired phone is nearby and unlocked.

## TVs
- FlyyLink on Google TV, Android TV and Fire TV: notifications, photos, videos and music from your devices
- The phone or computer as the TV remote, with arrows, typing and a touchpad pointer
- Play on TV: photos, videos and music on any DLNA or Google Cast TV, no TV app needed

## Later
- Instant hotspot: turn on the phone's hotspot from the Mac menu bar
- Find my phone with the last known location
- Multiple phones per Mac
