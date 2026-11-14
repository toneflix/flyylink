import AppKit
import FlyyLinkKit
import UniformTypeIdentifiers

/// What is playing on a TV right now. Times are in milliseconds; a duration of 0 means unknown.
struct TvSession: Equatable {
    var tv: TvRenderer
    var file: MediaFile
    var kind: CastKind
    var state: CastState = .loading
    var position: Int64 = 0
    var duration: Int64 = 0

    static func == (lhs: TvSession, rhs: TvSession) -> Bool {
        lhs.tv == rhs.tv && lhs.file.url == rhs.file.url && lhs.state == rhs.state && lhs.position == rhs.position && lhs.duration == rhs.duration
    }
}

/// "Play on TV": finds DLNA TVs, serves the chosen file to one and controls what it plays. Mirrors PlayOnTv.kt.
@MainActor
final class PlayOnTvModel: ObservableObject {
    @Published private(set) var tvs: [TvRenderer] = []
    @Published private(set) var searching = false
    @Published private(set) var session: TvSession?
    /// A file picked to play, waiting for a TV to be chosen.
    @Published var picked: MediaFile?
    @Published private(set) var error: String?

    private let seen = TvList()
    private var server: MediaServer?
    private var player: TvPlayer?
    private var watcher: Task<Void, Never>?
    private var looking: Task<Void, Never>?

    init() {
        keepSearching()
    }

    func search() {
        guard !searching else { return }

        searching = true
        Task {
            tvs = seen.update(await TvFinder.search())
            searching = false
        }
    }

    /// Searches again every 30 seconds, so the list follows TVs as they come and go.
    private func keepSearching() {
        looking = Task { [weak self] in
            while !Task.isCancelled {
                self?.search()
                try? await Task.sleep(nanoseconds: 30_000_000_000)
            }
        }
    }

    /// Asks for a photo, video or song, then lists TVs to play it on.
    func choose() {
        NSApplication.shared.activate(ignoringOtherApps: true)

        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        panel.allowedContentTypes = [.image, .movie, .audio]
        panel.prompt = "Choose"
        panel.message = "Choose a photo, video or song to play on a TV"
        panel.begin { [weak self] response in
            guard response == .OK, let url = panel.url else { return }

            Task { @MainActor in self?.pick(url) }
        }
    }

    func pick(_ url: URL) {
        guard let mime = UTType(filenameExtension: url.pathExtension)?.preferredMIMEType, CastKind.of(mime) != nil else { return }

        error = nil
        picked = MediaFile(url: url, mime: mime)
        search()
    }

    func play(on tv: TvRenderer) {
        guard let file = picked, let kind = CastKind.of(file.mime) else { return }

        picked = nil
        error = nil
        watcher?.cancel()

        let previous = player
        let tvPlayer = TvPlayer(tv: tv)
        player = tvPlayer
        session = TvSession(tv: tv, file: file, kind: kind)

        Task {
            try? await previous?.stop()

            do {
                let media = try mediaServer()
                media.clear()
                let url = try media.share(file, tvHost: tv.host)

                try await tvPlayer.play(url: url, title: file.name, mime: file.mime, size: file.size)
                watch(tvPlayer)
            } catch {
                end()
                self.error = "Couldn't play on \(tv.name)"
            }
        }
    }

    func togglePause() {
        guard var current = session, let player else { return }

        let paused = current.state == .paused
        current.state = paused ? .playing : .paused
        session = current

        Task {
            if paused {
                try? await player.resume()
            } else {
                try? await player.pause()
            }
        }
    }

    func seek(to positionMs: Int64) {
        guard let player else { return }

        session?.position = positionMs
        Task { try? await player.seek(positionMs) }
    }

    func stop() {
        let tvPlayer = player

        end()
        Task { try? await tvPlayer?.stop() }
    }

    func cancelPick() {
        picked = nil
    }

    /// Follows the TV about once a second, and ends the session once it stops on its own.
    private func watch(_ tvPlayer: TvPlayer) {
        watcher = Task { [weak self] in
            var started = false

            while !Task.isCancelled {
                let state = try? await tvPlayer.state()
                let position = try? await tvPlayer.position()

                guard let self, self.player?.tv == tvPlayer.tv, var current = self.session else { return }

                if state == .playing || state == .paused { started = true }

                if started, state == .stopped, current.kind != .photo {
                    self.end()
                    return
                }

                current.state = state ?? current.state
                current.position = position?.position ?? current.position
                if let duration = position?.duration, duration > 0 { current.duration = duration }
                self.session = current

                try? await Task.sleep(nanoseconds: 1_000_000_000)
            }
        }
    }

    private func end() {
        watcher?.cancel()
        watcher = nil
        player = nil
        server?.clear()
        session = nil
    }

    private func mediaServer() throws -> MediaServer {
        if let server { return server }

        let created = try MediaServer()
        server = created
        return created
    }
}
