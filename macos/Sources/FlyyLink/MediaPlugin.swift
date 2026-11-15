import Foundation
import FlyyLinkKit

/// What a phone is playing, with when we heard about it so the position can keep moving.
struct NowPlaying: Equatable {
    let player: MediaPlayer
    let receivedAt: Date
    let artwork: URL?

    /// Where playback should be now, in milliseconds.
    func position(at now: Date = Date()) -> Int64 {
        guard player.isPlaying else { return player.position }

        let moved = Int64(now.timeIntervalSince(receivedAt) * 1000)
        return player.length > 0 ? min(player.position + moved, player.length) : player.position + moved
    }
}

/// Shows and controls the phone's media players ("kdeconnect.mpris").
final class MediaPlugin: Plugin {
    let incoming: Set<String> = [PacketType.mpris]

    /// Called on any thread when something changed.
    var onChange: (() -> Void)?

    private var players: [String: [String: NowPlaying]] = [:]
    private let lock = NSLock()
    private let artFolder: URL
    private let files = FileManager.default

    init() {
        let caches = files.urls(for: .cachesDirectory, in: .userDomainMask).first ?? files.temporaryDirectory
        artFolder = caches.appendingPathComponent("FlyyLink/AlbumArt", isDirectory: true)
        try? files.createDirectory(at: artFolder, withIntermediateDirectories: true)
    }

    func onPaired(_ device: Device) {
        device.send(MediaRequest.playerList().toPacket())
    }

    func onFeaturesChanged(_ device: Device) {
        onPaired(device)
    }

    func onPacket(_ packet: NetworkPacket, from device: Device) {
        if let names = Media.playerList(packet) {
            updateList(names, device: device)
        } else if let url = Media.albumArtUrl(packet) {
            receiveArt(packet, url: url, device: device)
        } else if let player = MediaPlayer(packet: packet) {
            update(player, device: device)
        }
    }

    /// The player to show: one that is playing, otherwise the last one with a song.
    func nowPlaying(for deviceId: String) -> NowPlaying? {
        let all = lock.withLock { Array(players[deviceId, default: [:]].values) }
        let candidates = all.filter { !$0.player.title.isEmpty }

        return candidates.first { $0.player.isPlaying } ?? candidates.max { $0.receivedAt < $1.receivedAt }
    }

    func forget(_ deviceId: String) {
        lock.withLock { _ = players.removeValue(forKey: deviceId) }
    }

    private func updateList(_ names: [String], device: Device) {
        let known = lock.withLock { () -> Set<String> in
            players[device.id] = players[device.id, default: [:]].filter { names.contains($0.key) }
            return Set(players[device.id, default: [:]].keys)
        }

        names.filter { !known.contains($0) }.forEach { device.send(MediaRequest.nowPlaying($0).toPacket()) }
        onChange?()
    }

    private func update(_ player: MediaPlayer, device: Device) {
        let artwork = player.albumArtUrl.flatMap(artFile)
        let cached = artwork.map { files.fileExists(atPath: $0.path) } ?? false

        lock.withLock {
            players[device.id, default: [:]][player.name] = NowPlaying(player: player, receivedAt: Date(), artwork: cached ? artwork : nil)
        }

        if let url = player.albumArtUrl, artwork != nil, !cached {
            device.send(MediaRequest.albumArt(player.name, url: url).toPacket())
        }
        onChange?()
    }

    private func receiveArt(_ packet: NetworkPacket, url: String, device: Device) {
        guard let target = artFile(url) else { return }

        let partial = target.appendingPathExtension("part")
        guard files.createFile(atPath: partial.path, contents: nil),
              let sink = try? FileHandle(forWritingTo: partial),
              let download = try? device.receivePayload(packet, into: sink)
        else {
            try? files.removeItem(at: partial)
            return
        }

        download.whenComplete { [self] result in
            try? sink.close()
            if case .success = result {
                try? files.removeItem(at: target)
                try? files.moveItem(at: partial, to: target)
            } else {
                try? files.removeItem(at: partial)
                return
            }

            lock.withLock {
                for (name, entry) in players[device.id, default: [:]] where entry.player.albumArtUrl == url {
                    players[device.id]?[name] = NowPlaying(player: entry.player, receivedAt: entry.receivedAt, artwork: target)
                }
            }
            onChange?()
        }
    }

    /// A safe local file name for an art url: our own urls carry a hash, others get hashed.
    private func artFile(_ url: String) -> URL? {
        let prefix = "flyylink://art/"
        let suffix = url.hasPrefix(prefix) ? String(url.dropFirst(prefix.count)) : ""
        let name = suffix.count == 32 && suffix.allSatisfy(\.isHexDigit) ? suffix : PhoneNotification.iconHash(Array(url.utf8))

        return artFolder.appendingPathComponent("\(name).jpg")
    }
}
