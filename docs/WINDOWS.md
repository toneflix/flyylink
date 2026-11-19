# FlyyLink for Windows

What the Windows app does and how to use it. To build it, see [windows/README.md](../windows/README.md).

## Setting up

On first launch Windows Firewall asks whether FlyyLink may use the network. Allow it on private networks, or the phone can't find this PC.

Phone notifications need Windows notifications turned on for FlyyLink.

The panel asks you to sign in with the email from your FlyyLink plan, or to start a free trial on the website. See [LICENCES.md](LICENCES.md).

## What works

Pairing with the verification code, phone notifications with replies, shared clipboard (text, pictures and files), sending and receiving files with resume, links and text, battery level, and ringing the phone with its location on a map.

Texts in the Messages window, calls with a banner to answer, mute and hang up, media controls for what the phone plays, the phone's photos in the Photos window, and several paired phones with a card carousel.

Mirror and control the phone's screen from the panel's Mirror tile. When the phone is off your Wi-Fi but in Bluetooth range, FlyyLink finds it over Bluetooth and can ask it to turn on its hotspot and join it. Windows only shares the Wi-Fi name with apps when location is allowed for apps, so turn that on if FlyyLink doesn't remember the hotspot.

A paired TV gets a Remote tile with arrows, typing and a touchpad that moves the TV's pointer. While a TV is on your network, Play on TV in the panel sends it photos, videos and music.

Your phone's 2,000 most recent photos show in Explorer under `Phone Photos` in your user folder. They take no space until opened, when FlyyLink fetches the original from the phone. Turn it off in Settings.

Win+Shift+A opens the panel from anywhere, and Ctrl+Alt+C, F, M and R send the clipboard, send files, mirror and ring the phone (turn them off under Settings, Keyboard shortcuts). Pin FlyyLink to the taskbar for jump list shortcuts to Messages, Photos, mirroring, ringing your phone and sending the clipboard or files.

Scripts, Task Scheduler and Power Automate can drive the shown phone through `FlyyLink.exe`:

| Command | Does |
| --- | --- |
| `--ring` | Rings the phone |
| `--mirror` | Mirrors its screen |
| `--send-clipboard` | Sends what's copied |
| `--send-text "text"` | Opens a link on the phone, or puts other text on its clipboard |
| `--send <files>` | Sends files |
| `--battery` | Prints the battery level, 0 to 100 (in PowerShell, `FlyyLink.exe --battery \| Out-String` waits for it) |

The connected phones' battery is also kept in `%LOCALAPPDATA%\FlyyLink\status.json`.

Right-click files in Explorer and pick "Send to phone" (on Windows 11 it's under "Show more options"). To call `tel:` links from your phone, pick FlyyLink for TEL in Settings > Apps > Default apps.
