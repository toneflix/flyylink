import Darwin
import XCTest
@testable import FlyyLinkKit

/// Mirrors BrowserHostRelayTests.cs.
final class BrowserHostRelayTests: XCTestCase {
    /// A listening socket on 127.0.0.1 standing in for the app.
    private final class FakeApp {
        let listener: Int32
        let port: UInt16
        private(set) var client: Int32 = -1
        private var pending = Data()

        init() {
            listener = socket(AF_INET, SOCK_STREAM, 0)
            var address = sockaddr_in()
            address.sin_family = sa_family_t(AF_INET)
            address.sin_addr.s_addr = inet_addr("127.0.0.1")
            address.sin_port = 0
            _ = withUnsafePointer(to: &address) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { bind(listener, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) } }
            listen(listener, 1)

            var bound = sockaddr_in()
            var length = socklen_t(MemoryLayout<sockaddr_in>.size)
            _ = withUnsafeMutablePointer(to: &bound) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { getsockname(listener, $0, &length) } }
            port = UInt16(bigEndian: bound.sin_port)
        }

        func accept() {
            client = Darwin.accept(listener, nil, nil)
        }

        func readLine() -> String? {
            var byte: UInt8 = 0
            while true {
                if let newline = pending.firstIndex(of: 0x0A) {
                    let line = String(decoding: pending[pending.startIndex..<newline], as: UTF8.self)
                    pending = Data(pending[(newline + 1)...])
                    return line
                }
                guard recv(client, &byte, 1, 0) == 1 else { return nil }
                pending.append(byte)
            }
        }

        func write(_ line: String) {
            let bytes = Array((line + "\n").utf8)
            _ = bytes.withUnsafeBytes { send(client, $0.baseAddress, $0.count, 0) }
        }

        func closeClient() {
            close(client)
        }

        deinit {
            close(listener)
        }
    }

    private final class Box: @unchecked Sendable {
        let lock = NSLock()
        var endpoint: BrowserEndpoint?
        var launched = false
    }

    private func readFrame(_ handle: FileHandle) -> String? {
        var reader = NativeMessageReader()
        while true {
            if let message = reader.next() { return message }

            let data = handle.readData(ofLength: 1)
            if data.isEmpty { return nil }
            reader.append(data)
        }
    }

    func testPassesMessagesBothWays() throws {
        let app = FakeApp()
        let toHost = Pipe()
        let fromHost = Pipe()
        let box = Box()

        let relay = BrowserHostRelay(input: toHost.fileHandleForReading, output: fromHost.fileHandleForWriting) {
            box.lock.withLock { box.endpoint }
        }
        relay.retryDelay = 0.05
        relay.launch = { box.lock.withLock { box.launched = true } }
        let finished = expectation(description: "relay stopped")
        Thread {
            relay.run()
            finished.fulfill()
        }.start()

        XCTAssertEqual(readFrame(fromHost.fileHandleForReading), BrowserEvents.closed())

        toHost.fileHandleForWriting.write(NativeMessaging.frame(#"{"type":"launch"}"#))
        toHost.fileHandleForWriting.write(NativeMessaging.frame(#"{"type":"hello","browser":"Chrome"}"#))
        Thread.sleep(forTimeInterval: 0.1)
        XCTAssertTrue(box.lock.withLock { box.launched })
        box.lock.withLock { box.endpoint = BrowserEndpoint(port: app.port, token: "secret") }

        app.accept()
        XCTAssertEqual(app.readLine(), #"{"token":"secret"}"#)
        XCTAssertEqual(app.readLine(), #"{"browser":"Chrome","type":"hello"}"#)

        toHost.fileHandleForWriting.write(NativeMessaging.frame(#"{"type":"call","number":"123"}"#))
        XCTAssertEqual(app.readLine(), #"{"number":"123","type":"call"}"#)

        app.write(#"{"type":"call","active":true}"#)
        XCTAssertEqual(readFrame(fromHost.fileHandleForReading), #"{"type":"call","active":true}"#)

        box.lock.withLock { box.endpoint = nil }
        app.closeClient()
        XCTAssertEqual(readFrame(fromHost.fileHandleForReading), BrowserEvents.closed())

        toHost.fileHandleForWriting.closeFile()
        wait(for: [finished], timeout: 5)
    }
}
