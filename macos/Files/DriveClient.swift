import FileProvider
import Foundation
import Network

/// Sends one phone's storage requests to FlyyLink over its local port and turns failed answers into File Provider errors.
final class DriveClient {
    private let phone: String
    private let queue = DispatchQueue(label: "com.toneflix.flyylink.files.client")

    init(phone: String) {
        self.phone = phone
    }

    func send(
        _ action: PhoneDriveMessage.Action,
        path: String,
        to: String? = nil,
        file: URL? = nil
    ) async throws -> PhoneDriveMessage.Reply {
        guard let endpoint = PhoneDriveMessage.Endpoint.load(), let port = NWEndpoint.Port(rawValue: endpoint.port) else {
            throw NSFileProviderError(.serverUnreachable)
        }

        let request = PhoneDriveMessage.Request(token: endpoint.token, phone: phone, action: action, path: path, to: to, file: file?.path)
        var line = try JSONEncoder().encode(request)
        line.append(0x0A)

        let reply = try await exchange(line, port: port)
        if let error = reply.error { throw Self.error(error) }

        return reply
    }

    private func exchange(_ line: Data, port: NWEndpoint.Port) async throws -> PhoneDriveMessage.Reply {
        let connection = NWConnection(host: .ipv4(.loopback), port: port, using: .tcp)
        let answer = Answer(connection)

        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                answer.continuation = continuation
                connection.stateUpdateHandler = { state in
                    switch state {
                    case .failed, .waiting:
                        answer.finish(.failure(NSFileProviderError(.serverUnreachable)))
                    default:
                        break
                    }
                }
                connection.start(queue: queue)
                connection.send(content: line, completion: .contentProcessed { error in
                    if error != nil { answer.finish(.failure(NSFileProviderError(.serverUnreachable))) }
                })
                read(connection, buffer: Data(), answer: answer)
            }
        } onCancel: {
            answer.finish(.failure(CocoaError(.userCancelled)))
        }
    }

    private func read(_ connection: NWConnection, buffer: Data, answer: Answer) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 256 * 1024) { [weak self] data, _, isComplete, error in
            var buffer = buffer
            if let data { buffer.append(data) }

            if let end = buffer.firstIndex(of: 0x0A) {
                if let reply = try? JSONDecoder().decode(PhoneDriveMessage.Reply.self, from: buffer[..<end]) {
                    answer.finish(.success(reply))
                } else {
                    answer.finish(.failure(NSFileProviderError(.serverUnreachable)))
                }
            } else if error != nil || isComplete {
                answer.finish(.failure(NSFileProviderError(.serverUnreachable)))
            } else {
                self?.read(connection, buffer: buffer, answer: answer)
            }
        }
    }

    private static func error(_ code: String) -> Error {
        switch code {
        case PhoneDriveMessage.Reply.offline: return NSFileProviderError(.serverUnreachable)
        case "denied": return NSFileProviderError(.notAuthenticated)
        case "missing": return NSFileProviderError(.noSuchItem)
        case "exists": return NSFileProviderError(.filenameCollision)
        default: return CocoaError(.fileWriteUnknown)
        }
    }

    /// Resumes the request once, whichever of the reply, a failure or a cancel comes first, and closes the connection.
    private final class Answer: @unchecked Sendable {
        var continuation: CheckedContinuation<PhoneDriveMessage.Reply, Error>?
        private let connection: NWConnection
        private let lock = NSLock()

        init(_ connection: NWConnection) {
            self.connection = connection
        }

        func finish(_ result: Result<PhoneDriveMessage.Reply, Error>) {
            let continuation: CheckedContinuation<PhoneDriveMessage.Reply, Error>? = lock.withLock {
                defer { self.continuation = nil }
                return self.continuation
            }
            guard let continuation else { return }

            connection.cancel()
            continuation.resume(with: result)
        }
    }
}
