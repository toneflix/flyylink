import Foundation

/// Finds TVs on the network that play media sent to them ("Play on TV"). Mirrors DlnaNetwork.kt.
public enum TvFinder {
    private static let targets = [Ssdp.renderer, AvTransport.service]

    /// Searches for `timeout` seconds and returns each TV once, sorted by name.
    public static func search(timeout: TimeInterval = 3) async -> [TvRenderer] {
        let locations = await Task.detached { collectLocations(timeout: timeout) }.value

        let found = await withTaskGroup(of: TvRenderer?.self) { group in
            for location in locations {
                group.addTask { await describe(location) }
            }

            var found: [TvRenderer] = []
            for await tv in group {
                if let tv, !found.contains(where: { $0.id == tv.id }) { found.append(tv) }
            }
            return found
        }

        return found.sorted { $0.name.lowercased() < $1.name.lowercased() }
    }

    /// Sends the searches and gathers the description location of everything that answers. Blocks.
    private static func collectLocations(timeout: TimeInterval) -> [String] {
        let fd = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP)
        guard fd >= 0 else { return [] }
        defer { close(fd) }

        var group = sockaddr_in()
        group.sin_family = sa_family_t(AF_INET)
        group.sin_port = Ssdp.port.bigEndian
        inet_pton(AF_INET, Ssdp.address, &group.sin_addr)

        // Sent twice, since multicast over Wi-Fi drops packets.
        for _ in 0..<2 {
            for target in targets {
                let message = Array(Ssdp.searchMessage(target: target).utf8)
                _ = withUnsafePointer(to: &group) { ptr in
                    ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                        sendto(fd, message, message.count, 0, $0, socklen_t(MemoryLayout<sockaddr_in>.size))
                    }
                }
            }
        }

        var locations: [String] = []
        var buffer = [UInt8](repeating: 0, count: 4096)
        let deadline = Date().addingTimeInterval(timeout)

        while true {
            let left = deadline.timeIntervalSinceNow
            if left <= 0 { break }

            var wait = timeval(tv_sec: Int(left), tv_usec: Int32((left - floor(left)) * 1_000_000))
            setsockopt(fd, SOL_SOCKET, SO_RCVTIMEO, &wait, socklen_t(MemoryLayout<timeval>.size))

            let count = recv(fd, &buffer, buffer.count, 0)
            if count <= 0 { break }

            if let reply = Ssdp.parse(String(decoding: buffer[0..<count], as: UTF8.self)), !locations.contains(reply.location) {
                locations.append(reply.location)
            }
        }

        return locations
    }

    private static func describe(_ location: String) async -> TvRenderer? {
        guard let url = URL(string: location) else { return nil }

        var request = URLRequest(url: url, timeoutInterval: 3)
        request.httpMethod = "GET"

        guard let (data, _) = try? await URLSession.shared.data(for: request) else { return nil }

        return TvRenderer.parse(String(decoding: data, as: UTF8.self), location: location)
    }
}

/// Plays media on one TV and controls it.
public struct TvPlayer: Sendable {
    public let tv: TvRenderer

    public init(tv: TvRenderer) {
        self.tv = tv
    }

    public func play(url: String, title: String, mime: String, size: Int64? = nil) async throws {
        try await send(tv.avTransportUrl, AvTransport.setUri(url, title: title, mime: mime, size: size))
        try await send(tv.avTransportUrl, AvTransport.play())
    }

    public func resume() async throws {
        try await send(tv.avTransportUrl, AvTransport.play())
    }

    public func pause() async throws {
        try await send(tv.avTransportUrl, AvTransport.pause())
    }

    public func stop() async throws {
        try await send(tv.avTransportUrl, AvTransport.stop())
    }

    public func seek(_ positionMs: Int64) async throws {
        try await send(tv.avTransportUrl, AvTransport.seek(positionMs))
    }

    public func position() async throws -> CastPosition? {
        AvTransport.position(try await send(tv.avTransportUrl, AvTransport.positionInfo()))
    }

    public func state() async throws -> CastState? {
        AvTransport.state(try await send(tv.avTransportUrl, AvTransport.transportInfo()))
    }

    public func volume() async throws -> Int? {
        guard let url = tv.renderingControlUrl else { return nil }

        return RenderingControl.volume(try await send(url, RenderingControl.getVolume()))
    }

    public func setVolume(_ volume: Int) async throws {
        guard let url = tv.renderingControlUrl else { return }

        try await send(url, RenderingControl.setVolume(volume))
    }

    @discardableResult
    private func send(_ url: String, _ call: SoapCall) async throws -> String {
        guard let endpoint = URL(string: url) else { throw ProtocolError("bad control URL \(url)") }

        var request = URLRequest(url: endpoint, timeoutInterval: 5)
        request.httpMethod = "POST"
        request.setValue("text/xml; charset=\"utf-8\"", forHTTPHeaderField: "Content-Type")
        request.setValue(call.soapAction, forHTTPHeaderField: "SOAPACTION")
        request.httpBody = Data(call.envelope().utf8)

        let (data, response) = try await URLSession.shared.data(for: request)

        let code = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard (200..<300).contains(code) else { throw ProtocolError("\(call.action) failed with HTTP \(code)") }

        return String(decoding: data, as: UTF8.self)
    }
}

/// A file to serve to a TV.
public protocol MediaSource: Sendable {
    var name: String { get }
    var mime: String { get }
    var size: Int64 { get }

    /// Opens the file for reading. Called for every request, since TVs read a file several times.
    func open() throws -> FileHandle
}

/// A file on disk, served by `MediaServer`.
public struct MediaFile: MediaSource {
    public let url: URL
    public let name: String
    public let mime: String
    public let size: Int64

    public init(url: URL, mime: String) {
        self.url = url
        self.mime = mime
        name = url.lastPathComponent
        size = Int64((try? url.resourceValues(forKeys: [.fileSizeKey]))?.fileSize ?? 0)
    }

    public func open() throws -> FileHandle { try FileHandle(forReadingFrom: url) }
}

/// A small HTTP server that hands files to TVs, with Range support so they can seek.
public final class MediaServer: @unchecked Sendable {
    private let fd: Int32
    public let port: UInt16
    private var sources: [String: MediaSource] = [:]
    private let lock = NSLock()
    private var closed = false

    public init() throws {
        let listener = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP)
        guard listener >= 0 else { throw ProtocolError("no socket for the media server") }

        var yes: Int32 = 1
        setsockopt(listener, SOL_SOCKET, SO_REUSEADDR, &yes, socklen_t(MemoryLayout<Int32>.size))

        var address = sockaddr_in()
        address.sin_family = sa_family_t(AF_INET)
        address.sin_port = 0
        address.sin_addr.s_addr = 0

        let bound = withUnsafePointer(to: &address) { ptr in
            ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { bind(listener, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) }
        }
        guard bound == 0, listen(listener, 16) == 0 else {
            Darwin.close(listener)
            throw ProtocolError("the media server couldn't open a port")
        }

        var local = sockaddr_in()
        var length = socklen_t(MemoryLayout<sockaddr_in>.size)
        _ = withUnsafeMutablePointer(to: &local) { ptr in
            ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { getsockname(listener, $0, &length) }
        }

        fd = listener
        port = UInt16(bigEndian: local.sin_port)

        let thread = Thread { [weak self] in self?.acceptLoop() }
        thread.name = "media-server"
        thread.start()
    }

    deinit {
        close()
    }

    /// Serves `source` and returns its URL as `tvHost` can reach it.
    public func share(_ source: MediaSource, tvHost: String) throws -> String {
        let token = UUID().uuidString
        lock.withLock { sources[token] = source }

        let name = source.name.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed.subtracting(CharacterSet(charactersIn: "/?#"))) ?? "media"
        guard let address = Self.localAddressFacing(tvHost) else { throw ProtocolError("no network leads to \(tvHost)") }

        return "http://\(address):\(port)/\(token)/\(name)"
    }

    /// Stops serving everything shared so far.
    public func clear() {
        lock.withLock { sources.removeAll() }
    }

    public func close() {
        let wasOpen = lock.withLock { () -> Bool in
            defer { closed = true }
            sources.removeAll()
            return !closed
        }

        if wasOpen {
            shutdown(fd, Int32(SHUT_RDWR))
            Darwin.close(fd)
        }
    }

    private func acceptLoop() {
        while !lock.withLock({ closed }) {
            let client = accept(fd, nil, nil)
            if client < 0 {
                if lock.withLock({ closed }) { return }
                continue
            }

            var yes: Int32 = 1
            setsockopt(client, SOL_SOCKET, SO_NOSIGPIPE, &yes, socklen_t(MemoryLayout<Int32>.size))

            Thread.detachNewThread { [weak self] in
                defer { Darwin.close(client) }
                self?.handle(client)
            }
        }
    }

    private func handle(_ client: Int32) {
        var wait = timeval(tv_sec: 15, tv_usec: 0)
        setsockopt(client, SOL_SOCKET, SO_RCVTIMEO, &wait, socklen_t(MemoryLayout<timeval>.size))

        guard let head = readHead(client), let request = HttpRequestHead.parse(head) else { return }

        let token = request.path.drop(while: { $0 == "/" }).split(separator: "/").first.map(String.init) ?? ""
        let source = lock.withLock { sources[token] }

        if request.method != "GET", request.method != "HEAD" {
            respond(client, "405 Method Not Allowed")
        } else if let source {
            serve(client, request, source)
        } else {
            respond(client, "404 Not Found")
        }
    }

    private func serve(_ client: Int32, _ request: HttpRequestHead, _ source: MediaSource) {
        let range = ByteRange.parse(request.headers["range"], size: source.size)

        if let range, !range.isSatisfiable {
            respond(client, "416 Range Not Satisfiable", [("Content-Range", "bytes */\(source.size)")])
            return
        }

        let start = range?.start ?? 0
        let length = range?.length ?? source.size
        var headers = [
            ("Content-Type", source.mime),
            ("Content-Length", String(length)),
            ("Accept-Ranges", "bytes"),
            ("transferMode.dlna.org", CastKind.of(source.mime) == .photo ? "Interactive" : "Streaming"),
            ("contentFeatures.dlna.org", "DLNA.ORG_OP=01;DLNA.ORG_CI=0;DLNA.ORG_FLAGS=01700000000000000000000000000000"),
        ]
        if let range { headers.append(("Content-Range", "bytes \(range.start)-\(range.end)/\(source.size)")) }

        respond(client, range != nil ? "206 Partial Content" : "200 OK", headers)
        if request.method == "HEAD" { return }

        guard let file = try? source.open() else { return }
        defer { try? file.close() }

        do {
            try file.seek(toOffset: UInt64(start))

            var left = length
            while left > 0 {
                guard let chunk = try file.read(upToCount: Int(min(left, 64 * 1024))), !chunk.isEmpty else { break }
                guard write(client, chunk) else { break }
                left -= Int64(chunk.count)
            }
        } catch {
            return
        }
    }

    private func respond(_ client: Int32, _ status: String, _ headers: [(String, String)] = []) {
        var text = "HTTP/1.1 \(status)\r\n"
        headers.forEach { text += "\($0.0): \($0.1)\r\n" }
        if !headers.contains(where: { $0.0 == "Content-Length" }) { text += "Content-Length: 0\r\n" }
        text += "Connection: close\r\n\r\n"

        _ = write(client, Data(text.utf8))
    }

    private func write(_ client: Int32, _ data: Data) -> Bool {
        data.withUnsafeBytes { buffer in
            var offset = 0
            while offset < buffer.count {
                let sent = Darwin.send(client, buffer.baseAddress! + offset, buffer.count - offset, 0)
                if sent <= 0 { return false }
                offset += sent
            }
            return true
        }
    }

    private func readHead(_ client: Int32) -> String? {
        var bytes: [UInt8] = []
        var byte: UInt8 = 0

        while bytes.count < 16 * 1024 {
            guard recv(client, &byte, 1, 0) == 1 else { return nil }

            bytes.append(byte)
            if bytes.count >= 4, bytes.suffix(4) == [13, 10, 13, 10] { return String(decoding: bytes, as: UTF8.self) }
        }

        return nil
    }

    /// The local address on the network that leads to `host`. No packet is sent.
    public static func localAddressFacing(_ host: String) -> String? {
        let fd = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP)
        guard fd >= 0 else { return nil }
        defer { Darwin.close(fd) }

        var remote = sockaddr_in()
        remote.sin_family = sa_family_t(AF_INET)
        remote.sin_port = Ssdp.port.bigEndian
        guard inet_pton(AF_INET, host, &remote.sin_addr) == 1 else { return nil }

        let connected = withUnsafePointer(to: &remote) { ptr in
            ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { connect(fd, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) }
        }
        guard connected == 0 else { return nil }

        var local = sockaddr_in()
        var length = socklen_t(MemoryLayout<sockaddr_in>.size)
        _ = withUnsafeMutablePointer(to: &local) { ptr in
            ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { getsockname(fd, $0, &length) }
        }

        var buffer = [CChar](repeating: 0, count: Int(INET_ADDRSTRLEN))
        guard inet_ntop(AF_INET, &local.sin_addr, &buffer, socklen_t(INET_ADDRSTRLEN)) != nil else { return nil }

        return String(cString: buffer)
    }
}

/// The TVs seen lately. A TV stays listed until it misses `misses` searches in a row, since multicast answers get lost.
public final class TvList: @unchecked Sendable {
    private let misses: Int
    private var known: [String: TvRenderer] = [:]
    private var missed: [String: Int] = [:]
    private let lock = NSLock()

    public init(misses: Int = 2) {
        self.misses = misses
    }

    public var tvs: [TvRenderer] {
        lock.withLock { sorted() }
    }

    /// Takes one search's results and returns the TVs to show.
    @discardableResult
    public func update(_ found: [TvRenderer]) -> [TvRenderer] {
        lock.withLock {
            let ids = Set(found.map(\.id))

            for id in known.keys where !ids.contains(id) {
                let count = (missed[id] ?? 0) + 1
                if count >= misses {
                    known[id] = nil
                    missed[id] = nil
                } else {
                    missed[id] = count
                }
            }

            for tv in found {
                known[tv.id] = tv
                missed[tv.id] = nil
            }

            return sorted()
        }
    }

    private func sorted() -> [TvRenderer] {
        known.values.sorted { $0.name.lowercased() < $1.name.lowercased() }
    }
}
