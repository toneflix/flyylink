import Foundation
import FlyyLinkKit
import Network
import OSLog

/// One browser running the FlyyLink add-on, connected through the host.
final class BrowserConnection: @unchecked Sendable {
    /// Tells connected browsers apart in `BrowserMedia`.
    let key = UUID().uuidString

    private let connection: NWConnection
    private let lock = NSLock()
    private var browserName = "Browser"

    init(_ connection: NWConnection) {
        self.connection = connection
    }

    /// The browser's name from its hello, such as "Chrome".
    var name: String {
        get { lock.withLock { browserName } }
        set { lock.withLock { browserName = newValue } }
    }

    func send(_ line: String) {
        connection.send(content: Data((line + "\n").utf8), completion: .contentProcessed { _ in })
    }

    func close() {
        connection.cancel()
    }
}

/// Listens on a local port for the browser host, which the browser starts for the add-on. Only connections whose
/// first line carries the token saved next to the port are kept, and the file is readable by this user only.
final class BrowserBridge: @unchecked Sendable {
    var onConnected: ((BrowserConnection) -> Void)?
    var onRequest: ((BrowserConnection, BrowserRequest) -> Void)?
    var onClosed: ((BrowserConnection) -> Void)?

    private let token = BrowserEndpoint.newToken()
    private let queue = DispatchQueue(label: "com.toneflix.flyylink.browser")
    private let log = Logger(subsystem: "com.toneflix.flyylink", category: "browser")
    private let lock = NSLock()
    private var listener: NWListener?
    private var open: [BrowserConnection] = []

    /// The longest line taken from a browser.
    private let maxLine = 4 * 1024 * 1024

    var connections: [BrowserConnection] { lock.withLock { open } }

    func start() {
        guard listener == nil else { return }

        let parameters = NWParameters.tcp
        parameters.requiredLocalEndpoint = .hostPort(host: .ipv4(.loopback), port: .any)
        parameters.acceptLocalOnly = true

        do {
            let listener = try NWListener(using: parameters)
            listener.newConnectionHandler = { [weak self] connection in self?.accept(connection) }
            listener.stateUpdateHandler = { [weak self, weak listener] state in
                guard let self else { return }

                switch state {
                case .ready:
                    if let port = listener?.port?.rawValue { self.publish(port) }
                case .failed(let error):
                    self.log.error("Browser bridge stopped: \(error.localizedDescription, privacy: .public)")
                default:
                    break
                }
            }
            listener.start(queue: queue)
            self.listener = listener
        } catch {
            log.error("Browser bridge unavailable: \(error.localizedDescription, privacy: .public)")
        }
    }

    func stop() {
        listener?.cancel()
        listener = nil
        connections.forEach { $0.close() }
        try? FileManager.default.removeItem(at: BrowserEndpoint.file)
    }

    func broadcast(_ line: String) {
        connections.forEach { $0.send(line) }
    }

    private func publish(_ port: UInt16) {
        do {
            try BrowserEndpoint(port: port, token: token).save()
        } catch {
            log.error("Could not save the browser bridge's port: \(error.localizedDescription, privacy: .public)")
        }
    }

    private func accept(_ connection: NWConnection) {
        let browser = BrowserConnection(connection)
        connection.stateUpdateHandler = { [weak self] state in
            switch state {
            case .failed, .cancelled: self?.closed(browser)
            default: break
            }
        }
        connection.start(queue: queue)
        read(connection, browser: browser, buffer: Data(), trusted: false)
    }

    private func read(_ connection: NWConnection, browser: BrowserConnection, buffer: Data, trusted: Bool) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 64 * 1024) { [weak self] data, _, isComplete, error in
            guard let self else { return }

            var buffer = buffer
            var trusted = trusted
            if let data { buffer.append(data) }

            while let newline = buffer.firstIndex(of: 0x0A) {
                let line = String(decoding: buffer[buffer.startIndex..<newline], as: UTF8.self)
                buffer = Data(buffer[(newline + 1)...])

                if trusted {
                    if let request = BrowserRequest(line: line) { self.onRequest?(browser, request) }
                } else if BrowserEndpoint(port: 0, token: self.token).accepts(line) {
                    trusted = true
                    self.lock.withLock { self.open.append(browser) }
                    self.onConnected?(browser)
                } else {
                    connection.cancel()
                    return
                }
            }

            if error != nil || isComplete || buffer.count > self.maxLine {
                connection.cancel()
            } else {
                self.read(connection, browser: browser, buffer: buffer, trusted: trusted)
            }
        }
    }

    private func closed(_ browser: BrowserConnection) {
        let known: Bool = lock.withLock {
            guard let index = open.firstIndex(where: { $0 === browser }) else { return false }

            open.remove(at: index)
            return true
        }
        if known { onClosed?(browser) }
    }
}

/// Tells Chrome, Edge, Brave, Arc, Vivaldi and Firefox where FlyyLink's browser host is, so the add-on can start it.
enum BrowserHosts {
    /// Each browser's folder in Application Support. A browser that isn't installed has none, and is skipped.
    private static let chromium = [
        "Google/Chrome", "Google/Chrome Beta", "Google/Chrome Canary", "Chromium", "Microsoft Edge",
        "BraveSoftware/Brave-Browser", "Vivaldi", "Arc/User Data",
    ]
    private static let firefox = ["Mozilla"]

    static var hostURL: URL { Bundle.main.bundleURL.appendingPathComponent("Contents/MacOS/FlyyLinkBrowserHost") }

    /// Writes the manifests again on every launch, so they follow the app when it moves.
    static func register() {
        let files = FileManager.default
        guard files.isExecutableFile(atPath: hostURL.path),
              let support = files.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
        else { return }

        let write = { (browsers: [String], isFirefox: Bool) in
            let manifest = Browser.hostManifest(hostPath: hostURL.path, firefox: isFirefox)
            for browser in browsers {
                let home = support.appendingPathComponent(browser, isDirectory: true)
                guard files.fileExists(atPath: home.path) else { continue }

                let folder = home.appendingPathComponent("NativeMessagingHosts", isDirectory: true)
                try? files.createDirectory(at: folder, withIntermediateDirectories: true)
                try? manifest.write(to: folder.appendingPathComponent("\(Browser.hostName).json"), options: .atomic)
            }
        }

        write(chromium, false)
        write(firefox, true)
    }
}
