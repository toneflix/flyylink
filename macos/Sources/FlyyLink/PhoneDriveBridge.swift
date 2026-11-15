import Foundation
import Network
import OSLog

/// Listens on a local port for the Finder extension's requests, one per connection, and answers each with `handle`.
/// Only requests carrying the token saved next to the port are taken.
final class PhoneDriveBridge {
    /// Answers one request. May call back on any thread.
    var handle: ((PhoneDriveMessage.Request, @escaping (PhoneDriveMessage.Reply) -> Void) -> Void)?

    private let token = (0..<4).map { _ in String(format: "%08x", UInt32.random(in: .min ... .max)) }.joined()
    private let queue = DispatchQueue(label: "com.toneflix.flyylink.drive")
    private let log = Logger(subsystem: "com.toneflix.flyylink", category: "drive")
    private var listener: NWListener?

    /// The longest request line taken, which is far more than any path needs.
    private let maxLine = 64 * 1024

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
                    self.log.error("Drive bridge stopped: \(error.localizedDescription, privacy: .public)")
                default:
                    break
                }
            }
            listener.start(queue: queue)
            self.listener = listener
        } catch {
            log.error("Drive bridge unavailable: \(error.localizedDescription, privacy: .public)")
        }
    }

    func stop() {
        listener?.cancel()
        listener = nil
        try? FileManager.default.removeItem(at: PhoneDriveMessage.endpointFile)
    }

    private func publish(_ port: UInt16) {
        do {
            try PhoneDriveMessage.Endpoint(port: port, token: token).save()
        } catch {
            log.error("Could not save the drive bridge's port: \(error.localizedDescription, privacy: .public)")
        }
    }

    private func accept(_ connection: NWConnection) {
        connection.start(queue: queue)
        read(connection, buffer: Data())
    }

    private func read(_ connection: NWConnection, buffer: Data) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 16 * 1024) { [weak self] data, _, isComplete, error in
            guard let self else { return }

            var buffer = buffer
            if let data { buffer.append(data) }

            if let end = buffer.firstIndex(of: 0x0A) {
                self.answer(buffer[..<end], on: connection)
            } else if error != nil || isComplete || buffer.count > self.maxLine {
                connection.cancel()
            } else {
                self.read(connection, buffer: buffer)
            }
        }
    }

    private func answer(_ line: Data, on connection: NWConnection) {
        guard let request = try? JSONDecoder().decode(PhoneDriveMessage.Request.self, from: line), request.token == token else {
            connection.cancel()
            return
        }
        guard let handle else {
            reply(PhoneDriveMessage.Reply(error: PhoneDriveMessage.Reply.offline), on: connection)
            return
        }

        handle(request) { [weak self] reply in
            self?.queue.async { self?.reply(reply, on: connection) }
        }
    }

    private func reply(_ reply: PhoneDriveMessage.Reply, on connection: NWConnection) {
        var data = (try? JSONEncoder().encode(reply)) ?? Data("{}".utf8)
        data.append(0x0A)

        connection.send(content: data, completion: .contentProcessed { _ in connection.cancel() })
    }
}
