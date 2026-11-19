import Foundation

/// Audio or video playing in a browser tab. Times are in milliseconds. Mirrors BrowserMedia.cs.
public struct BrowserPlayer: Equatable, Sendable {
    public var id: String
    public var site: String
    public var title = ""
    public var artist = ""
    public var album = ""
    /// The page's artwork, an http or https address.
    public var artwork: String?
    public var isPlaying = false
    public var position: Int64 = 0
    public var length: Int64 = 0
    public var canSeek = false
    public var canGoNext = false
    public var canGoPrevious = false

    public init(id: String, site: String) {
        self.id = id
        self.site = site
    }

    init?(_ root: [String: JSONValue]) {
        guard let id = root["id"]?.stringValue, (1...100).contains(id.count),
              let site = root["site"]?.stringValue, !site.isEmpty
        else { return nil }

        let millis = { (key: String) -> Int64 in
            guard let value = root[key]?.doubleValue, value.isFinite, value > 0 else { return 0 }
            return Int64(value)
        }

        self.init(id: id, site: BrowserRequest.limit(site, 60) ?? site)
        title = BrowserRequest.limit(root["title"]?.stringValue, 300) ?? ""
        artist = BrowserRequest.limit(root["artist"]?.stringValue, 300) ?? ""
        album = BrowserRequest.limit(root["album"]?.stringValue, 300) ?? ""
        artwork = BrowserRequest.webUrl(root["artwork"]?.stringValue)
        isPlaying = root["playing"]?.boolValue ?? false
        position = millis("position")
        length = millis("length")
        canSeek = root["canSeek"]?.boolValue ?? false
        canGoNext = root["canNext"]?.boolValue ?? false
        canGoPrevious = root["canPrevious"]?.boolValue ?? false
    }
}

/// The players of every connected browser, named for paired devices as "YouTube in Chrome". A name stays with its
/// player while the player lasts, so a phone keeps controlling the right tab.
public final class BrowserMedia: @unchecked Sendable {
    private struct Entry {
        let browser: String
        let player: BrowserPlayer
        let name: String
    }

    private let lock = NSLock()
    private var entries: [Entry] = []

    public init() {}

    /// Replaces what one browser is playing. `browser` tells connected browsers apart.
    public func update(browser: String, browserName: String, players: [BrowserPlayer]) {
        lock.withLock {
            let others = entries.filter { $0.browser != browser }
            let previous = Dictionary(entries.filter { $0.browser == browser }.map { ($0.player.id, $0.name) }, uniquingKeysWith: { first, _ in first })
            var taken = Set(others.map(\.name))

            var seen = Set<String>()
            let current = players.filter { seen.insert($0.id).inserted }
            let kept = Set(previous.filter { pair in current.contains { $0.id == pair.key } }.values)

            var updated: [Entry] = []
            for player in current {
                let name: String
                if let own = previous[player.id], !taken.contains(own) {
                    name = own
                } else {
                    name = Self.freeName("\(player.site) in \(browserName)", avoiding: taken.union(kept))
                }
                taken.insert(name)
                updated.append(Entry(browser: browser, player: player, name: name))
            }

            entries = others + updated
        }
    }

    public func forget(browser: String) {
        update(browser: browser, browserName: "", players: [])
    }

    public var players: [MediaPlayer] {
        lock.withLock {
            entries.map { entry in
                var player = MediaPlayer(name: entry.name)
                player.title = entry.player.title
                player.artist = entry.player.artist
                player.album = entry.player.album
                player.isPlaying = entry.player.isPlaying
                player.position = entry.player.position
                player.length = entry.player.length
                player.canSeek = entry.player.canSeek && entry.player.length > 0
                player.canGoNext = entry.player.canGoNext
                player.canGoPrevious = entry.player.canGoPrevious
                player.albumArtUrl = entry.player.artwork
                return player
            }
        }
    }

    /// Which browser and which of its players a name stands for.
    public func find(_ name: String) -> (browser: String, playerId: String)? {
        lock.withLock {
            entries.first { $0.name == name }.map { ($0.browser, $0.player.id) }
        }
    }

    /// Whether `url` is the artwork of one of the browser's players, so it is safe to download for a device.
    public func hasArtwork(_ url: String) -> Bool {
        lock.withLock { entries.contains { $0.player.artwork == url } }
    }

    private static func freeName(_ wanted: String, avoiding avoid: Set<String>) -> String {
        guard avoid.contains(wanted) else { return wanted }

        var number = 2
        while avoid.contains("\(wanted) \(number)") { number += 1 }

        return "\(wanted) \(number)"
    }
}
