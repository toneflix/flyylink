import Foundation
import FlyyLinkKit

/// Lets paired devices see and control what this Mac is playing ("kdeconnect.mpris.request"): Music and Spotify,
/// and the browser tabs the add-on reports.
final class MediaHostPlugin: Plugin {
    let incoming: Set<String> = [PacketType.mprisRequest]

    /// Bigger artwork than this is not sent to devices.
    private static let maxArtwork = 4 * 1024 * 1024

    private let devices: DeviceRegistry
    private let browsers: BrowserCompanion
    private let media = LocalMedia()
    private let queue = DispatchQueue(label: "com.toneflix.flyylink.media-host")
    private var lastSent: [MediaPlayer] = []

    init(devices: DeviceRegistry, browsers: BrowserCompanion) {
        self.devices = devices
        self.browsers = browsers
        media.onChange = { [weak self] in self?.queue.async { self?.broadcast() } }
        browsers.mediaChanged = { [weak self] in self?.queue.async { self?.broadcast() } }
        media.start()
    }

    func onPacket(_ packet: NetworkPacket, from device: Device) {
        guard let request = MediaRequest(packet: packet) else { return }

        if let art = request.albumArtUrl, let name = request.player {
            sendArtwork(art, of: name, to: device)
            return
        }

        queue.async { [self] in
            let changes = request.action != nil || request.seekTo != nil || request.volume != nil
            if changes, !browsers.control(request) { media.perform(request) }

            let players = allPlayers()
            if request.wantsPlayerList {
                device.send(Media.playerListPacket(players.map(\.name)))
            }
            if request.wantsNowPlaying || changes, let player = players.first(where: { $0.name == request.player }) {
                device.send(player.toPacket())
            }
        }
    }

    private func allPlayers() -> [MediaPlayer] {
        media.players() + browsers.media.players
    }

    /// Sends a browser player's artwork, which only this Mac can fetch for the device.
    private func sendArtwork(_ address: String, of player: String, to device: Device) {
        guard browsers.media.hasArtwork(address), let url = URL(string: address) else { return }

        URLSession.shared.dataTask(with: url) { data, _, _ in
            guard let data, !data.isEmpty, data.count <= Self.maxArtwork else { return }

            let file = FileManager.default.temporaryDirectory.appendingPathComponent("flyylink-art-\(UUID().uuidString)")
            guard (try? data.write(to: file)) != nil, let source = try? FileHandle(forReadingFrom: file) else { return }

            let cleanUp = {
                try? source.close()
                try? FileManager.default.removeItem(at: file)
            }
            if !device.sendWithPayload(Media.albumArtPacket(player: player, url: address), size: Int64(data.count), source: source, completion: { _ in cleanUp() }) {
                cleanUp()
            }
        }.resume()
    }

    /// Tells every paired device about a change, once per change.
    private func broadcast() {
        let players = allPlayers()
        guard players != lastSent else { return }

        let namesChanged = players.map(\.name) != lastSent.map(\.name)
        lastSent = players

        for device in devices.all where device.isPaired && device.link.identity.incomingCapabilities.contains(PacketType.mpris) {
            if namesChanged { device.send(Media.playerListPacket(players.map(\.name))) }
            players.forEach { device.send($0.toPacket()) }
        }
    }
}
