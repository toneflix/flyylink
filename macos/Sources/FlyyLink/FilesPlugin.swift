import Foundation
import FlyyLinkKit

/// Asks a phone about its storage ("flyylink.files.request") and matches each answer ("flyylink.files") to its request.
final class FilesPlugin: Plugin {
    let incoming: Set<String> = [PacketType.files]

    enum Failure: Error {
        /// The phone isn't connected, or stopped answering.
        case offline
        case phone(PhoneFiles.Failure)
    }

    private struct Waiting {
        let deviceId: String
        /// Where a download is saved.
        let saveTo: URL?
        let completion: (Result<PhoneFiles.Answer, Failure>) -> Void
    }

    /// How long the phone may take to answer, counted from the request or from the end of an upload.
    private let patience: TimeInterval = 60

    private var waiting: [Int64: Waiting] = [:]
    private var lastId = Int64(Date().timeIntervalSince1970 * 1000)
    private let lock = NSLock()

    /// Sends one request. `upload` is the file an upload sends; `saveTo` is where a download goes. Call off the event loops.
    func send(
        _ action: PhoneFiles.Action,
        path: String,
        to: String? = nil,
        upload: URL? = nil,
        saveTo: URL? = nil,
        device: Device,
        completion: @escaping (Result<PhoneFiles.Answer, Failure>) -> Void
    ) {
        let id: Int64 = lock.withLock {
            lastId += 1
            waiting[lastId] = Waiting(deviceId: device.id, saveTo: saveTo, completion: completion)
            return lastId
        }
        let packet = PhoneFiles.request(.init(id: id, action: action, path: path, to: to))

        guard let upload else {
            if device.send(packet) {
                expire(id, after: patience)
            } else {
                finish(id, .failure(.offline))
            }
            return
        }

        guard let source = try? FileHandle(forReadingFrom: upload),
              let size = (try? FileManager.default.attributesOfItem(atPath: upload.path)[.size] as? NSNumber)?.int64Value
        else {
            finish(id, .failure(.phone(.invalid)))
            return
        }

        // On failure it calls the completion itself.
        device.sendWithPayload(packet, size: size, source: source, completion: { [weak self] error in
            try? source.close()

            guard let self else { return }

            if error != nil {
                self.finish(id, .failure(.offline))
            } else {
                self.expire(id, after: self.patience)
            }
        })
    }

    func onPacket(_ packet: NetworkPacket, from device: Device) {
        guard let answer = PhoneFiles.answer(packet) else { return }

        let request: Waiting? = lock.withLock {
            guard waiting[answer.id]?.deviceId == device.id else { return nil }
            return waiting.removeValue(forKey: answer.id)
        }
        guard let request else { return }

        if let failure = answer.failure {
            request.completion(.failure(.phone(failure)))
        } else if let target = request.saveTo, packet.payloadSize != nil {
            receive(packet, into: target, answer: answer, from: device, completion: request.completion)
        } else {
            request.completion(.success(answer))
        }
    }

    private func receive(
        _ packet: NetworkPacket,
        into target: URL,
        answer: PhoneFiles.Answer,
        from device: Device,
        completion: @escaping (Result<PhoneFiles.Answer, Failure>) -> Void
    ) {
        let files = FileManager.default

        try? files.createDirectory(at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
        guard files.createFile(atPath: target.path, contents: nil),
              let sink = try? FileHandle(forWritingTo: target),
              let download = try? device.receivePayload(packet, into: sink)
        else {
            completion(.failure(.offline))
            return
        }

        download.whenComplete { result in
            try? sink.close()

            if case .success = result {
                completion(.success(answer))
            } else {
                try? files.removeItem(at: target)
                completion(.failure(.offline))
            }
        }
    }

    private func expire(_ id: Int64, after delay: TimeInterval) {
        DispatchQueue.global().asyncAfter(deadline: .now() + delay) { [weak self] in
            self?.finish(id, .failure(.offline))
        }
    }

    private func finish(_ id: Int64, _ result: Result<PhoneFiles.Answer, Failure>) {
        lock.withLock { waiting.removeValue(forKey: id) }?.completion(result)
    }
}
