import Darwin
import Foundation

/// The host's work: passes the browser's framed messages to the app's local port as lines, and the app's lines back
/// as frames. While the app isn't running it tells the browser so, and keeps trying; `launch` opens it. The last
/// `hello` and `media` are sent again on every connection, so an app that starts later still knows the browser.
/// Mirrors BrowserHostRelay.cs.
public final class BrowserHostRelay: @unchecked Sendable {
    private static let replayed = ["hello", "media"]

    private let input: FileHandle
    private let output: FileHandle
    private let endpoint: () -> BrowserEndpoint?
    private let lock = NSLock()
    private let writing = NSLock()
    private var socket: Int32 = -1
    private var latest: [String: String] = [:]
    private var stopped = false

    public var retryDelay: TimeInterval = 2

    /// Opens FlyyLink when the add-on asks with `launch`, such as from its "Open FlyyLink" button.
    public var launch: (() -> Void)?

    public init(input: FileHandle, output: FileHandle, endpoint: @escaping () -> BrowserEndpoint?) {
        self.input = input
        self.output = output
        self.endpoint = endpoint
    }

    /// Runs until the browser closes the host's input.
    public func run() {
        let appThread = Thread { [self] in keepConnected() }
        appThread.start()

        readBrowser()

        lock.withLock {
            stopped = true
            if socket >= 0 { shutdown(socket, Int32(SHUT_RDWR)) }
        }
    }

    private func readBrowser() {
        var reader = NativeMessageReader()

        while true {
            let data = input.availableData
            if data.isEmpty { return }

            reader.append(data)
            while let message = reader.next() { forward(message) }
            if reader.isBroken { return }
        }
    }

    private func forward(_ message: String) {
        guard let root = BrowserEndpoint.object(message) else { return }

        let line = BrowserEvents.encode(root)
        let type = root["type"]?.stringValue
        if type == "launch" {
            launch?()
            return
        }

        let target: Int32 = lock.withLock {
            if let type, Self.replayed.contains(type) { latest[type] = line }
            return socket
        }
        if target >= 0 { _ = Self.send(line + "\n", to: target) }
    }

    private func keepConnected() {
        var toldClosed = false

        while !lock.withLock({ stopped }) {
            if let connected = connect() {
                toldClosed = false
                relayApp(connected)
            }

            if !toldClosed {
                writeBrowser(BrowserEvents.closed())
                toldClosed = true
            }

            Thread.sleep(forTimeInterval: retryDelay)
        }
    }

    private func connect() -> Int32? {
        guard let target = endpoint() else { return nil }

        let fd = Darwin.socket(AF_INET, SOCK_STREAM, 0)
        guard fd >= 0 else { return nil }

        var noSignal: Int32 = 1
        setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &noSignal, socklen_t(MemoryLayout<Int32>.size))

        var address = sockaddr_in()
        address.sin_family = sa_family_t(AF_INET)
        address.sin_port = target.port.bigEndian
        address.sin_addr.s_addr = inet_addr("127.0.0.1")
        let connected = withUnsafePointer(to: &address) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { Darwin.connect(fd, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) }
        }
        guard connected == 0, Self.send(target.helloLine + "\n", to: fd) else {
            close(fd)
            return nil
        }

        let replay = lock.withLock { Self.replayed.compactMap { latest[$0] } }
        for line in replay where !Self.send(line + "\n", to: fd) {
            close(fd)
            return nil
        }

        lock.withLock { socket = fd }
        return fd
    }

    private func relayApp(_ fd: Int32) {
        var pending = Data()
        var chunk = [UInt8](repeating: 0, count: 64 * 1024)

        defer {
            lock.withLock { socket = -1 }
            close(fd)
        }

        while true {
            let read = recv(fd, &chunk, chunk.count, 0)
            if read <= 0 { return }

            pending.append(contentsOf: chunk[0..<read])
            while let newline = pending.firstIndex(of: 0x0A) {
                let line = pending[pending.startIndex..<newline]
                pending = Data(pending[(newline + 1)...])
                if line.count <= Browser.maxMessage { writeBrowser(String(decoding: line, as: UTF8.self)) }
            }
            if pending.count > 4 * Browser.maxMessage { return }
        }
    }

    private func writeBrowser(_ json: String) {
        writing.withLock {
            try? output.write(contentsOf: NativeMessaging.frame(json))
        }
    }

    private static func send(_ text: String, to fd: Int32) -> Bool {
        let bytes = Array(text.utf8)
        var sent = 0

        while sent < bytes.count {
            let result = bytes[sent...].withUnsafeBytes { Darwin.send(fd, $0.baseAddress, $0.count, 0) }
            if result <= 0 { return false }
            sent += result
        }

        return true
    }
}
