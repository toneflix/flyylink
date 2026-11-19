import AppKit
import FlyyLinkKit

/// Moves files, links and text between the Mac and the phone, and any accepted FlyyDrop. Files go to Downloads, links open in the
/// browser, and text lands on the clipboard. A file that stops halfway resumes when the phone reconnects,
/// even after FlyyLink restarts.
final class SharePlugin: Plugin {
    let incoming: Set<String> = [PacketType.shareRequest, PacketType.shareResume]

    /// Called with each new or updated Recent entry, on any thread.
    var onActivity: ((Activity) -> Void)?

    /// Called when a file has arrived, to tell the user.
    var onFileReceived: ((URL, Device) -> Void)?

    private let folder: URL
    private let store: TransferStore
    private let files = FileManager.default
    private let lock = NSLock()

    /// The Recent entry of each unfinished transfer, so a resume updates the same line.
    private var activities: [String: Activity] = [:]

    init(folder: URL, store: TransferStore = TransferStore()) {
        self.folder = folder
        self.store = store
    }

    /// Lists the transfers left unfinished last time, and deletes the ones that waited too long.
    func restore() {
        for partial in store.removeExpired() {
            try? files.removeItem(atPath: partial.path)
        }

        for (id, file) in store.partial {
            report(id, Activity(.failed, title: (file.path as NSString).lastPathComponent, detail: "Paused · resumes when \(file.deviceName) reconnects"))
        }
        for (id, file) in store.offered {
            report(id, Activity(.failed, title: (file.path as NSString).lastPathComponent, detail: "Stopped · resumes when \(file.deviceName) reconnects"))
        }
    }

    /// Gives up on every transfer that isn't moving right now, so it no longer waits to resume or comes back after a restart.
    func forgetUnfinished() {
        let moving = lock.withLock { Set(activities.filter { $0.value.isInProgress }.keys) }

        for (id, file) in store.partial where !moving.contains(id) {
            store.setPartial(id, nil)
            try? files.removeItem(atPath: file.path)
        }
        for id in store.offered.keys where !moving.contains(id) {
            store.setOffered(id, nil)
        }

        lock.withLock { activities = activities.filter { moving.contains($0.key) } }
    }

    func onPaired(_ device: Device) {
        for (id, file) in store.partial where file.deviceId == device.id {
            let have = (try? files.attributesOfItem(atPath: file.path)[.size] as? NSNumber)?.int64Value ?? 0
            device.send(Share.resumePacket(transferId: id, offset: have))
        }
    }

    func onPacket(_ packet: NetworkPacket, from device: Device) {
        if let request = Share.resumeRequest(packet) {
            resume(request.transferId, from: request.offset, to: device)
            return
        }

        guard let share = Share(packet: packet) else { return }

        switch share {
        case let .file(name, size, transferId, offset): receive(packet, name: name, size: size, transferId: transferId, offset: offset, from: device)
        case .link(let link): open(link, from: device)
        case .text(let text): paste(text, from: device)
        }
    }

    // MARK: Sending

    /// Offers a file to the phone. Call off the main thread. With `flyyDropId` the file belongs to an accepted
    /// FlyyDrop, which can't be resumed; `onBytes` and `onDone` then report how it goes.
    func send(
        _ url: URL,
        to device: Device,
        flyyDropId: String? = nil,
        onBytes: ((Int64) -> Void)? = nil,
        onDone: ((Error?) -> Void)? = nil
    ) {
        let id = UUID().uuidString
        let activity = report(id, Activity(.sending, title: url.lastPathComponent, detail: "Sending to \(device.name)"))

        if flyyDropId == nil {
            _ = store.removeExpired()
            store.setOffered(id, .init(path: url.path, deviceId: device.id, deviceName: device.name, expires: Date().addingTimeInterval(TransferStore.keep)))
        }

        offer(url, id: id, flyyDropId: flyyDropId, from: 0, to: device, activity: activity, onBytes: onBytes, onDone: onDone)
    }

    private func resume(_ id: String, from offset: Int64, to device: Device) {
        guard let file = store.offered[id], file.deviceId == device.id else { return }

        let url = URL(fileURLWithPath: file.path)
        var activity = lock.withLock { activities[id] } ?? Activity(.sending, title: url.lastPathComponent, detail: "")
        activity.kind = .sending
        activity.detail = "Resuming to \(device.name)"
        report(id, activity)

        DispatchQueue.global(qos: .userInitiated).async { [self] in
            offer(url, id: id, from: offset, to: device, activity: activity)
        }
    }

    private func offer(
        _ url: URL,
        id: String,
        flyyDropId: String? = nil,
        from offset: Int64,
        to device: Device,
        activity: Activity,
        onBytes: ((Int64) -> Void)? = nil,
        onDone: ((Error?) -> Void)? = nil
    ) {
        let resumable = flyyDropId == nil
        let finish: (Error?) -> Void = { [weak self] error in
            var done = activity
            done.date = Date()
            done.progress = nil
            done.kind = error == nil ? .sent : .failed
            done.detail = error == nil
                ? "Sent to \(device.name)"
                : resumable ? "Stopped · resumes when \(device.name) reconnects" : "Couldn't finish sending to \(device.name)"
            if error == nil || !resumable { self?.store.setOffered(id, nil) }
            self?.report(id, done, finished: error == nil || !resumable)
            onDone?(error)
        }

        guard let attributes = try? files.attributesOfItem(atPath: url.path),
              let source = try? FileHandle(forReadingFrom: url)
        else {
            finish(ProtocolError("could not open \(url.lastPathComponent)"))
            return
        }

        let size = (attributes[.size] as? NSNumber)?.int64Value ?? 0
        let start = min(max(offset, 0), size)
        let modified = (attributes[.modificationDate] as? Date).map { Int64($0.timeIntervalSince1970 * 1000) }
        let packet = flyyDropId.map { FlyyDrop.file($0, FlyyDrop.Item(name: url.lastPathComponent, size: size)) }
            ?? Share.filePacket(name: url.lastPathComponent, size: size, lastModifiedMillis: modified, transferId: id, offset: start)
        let progress = TransferProgress(total: size, alreadyDone: start)

        do {
            try source.seek(toOffset: UInt64(start))
        } catch {
            try? source.close()
            finish(error)
            return
        }

        let onProgress: (Int64) -> Void = { [weak self] bytes in
            onBytes?(bytes)
            guard let reading = progress.advance(bytes) else { return }

            var moving = activity
            moving.progress = reading
            self?.report(id, moving)
        }

        // On failure it calls the completion itself.
        device.sendWithPayload(packet, size: size - start, source: source, onProgress: onProgress) { error in
            try? source.close()
            finish(error)
        }
    }

    // MARK: Receiving

    private func receive(_ packet: NetworkPacket, name: String, size: Int64, transferId: String?, offset: Int64, from device: Device) {
        let partial = transferId.flatMap { store.partial[$0] }.flatMap { $0.deviceId == device.id ? $0 : nil }
        let total = partial?.total ?? offset + size

        let target = partial.flatMap { openForAppend(URL(fileURLWithPath: $0.path), at: offset) }
            ?? (offset == 0 ? reserveFile(Share.safeName(name)) : nil)
        guard let target else {
            onActivity?(Activity(.failed, title: name, detail: "We couldn't save it to Downloads"))
            return
        }

        let (url, sink) = target
        let key = transferId ?? UUID().uuidString
        var activity = lock.withLock { activities[key] } ?? Activity(.receiving, title: url.lastPathComponent, detail: "")
        activity.kind = .receiving
        activity.detail = "\(partial == nil ? "Receiving" : "Resuming") from \(device.name)"
        let started = report(key, activity)

        // Recorded before any bytes arrive, so a quit or crash midway can still resume.
        if let transferId {
            let expires = Date().addingTimeInterval(TransferStore.keep)
            store.setPartial(transferId, .init(path: url.path, deviceId: device.id, deviceName: device.name, total: total, expires: expires))
        }

        let finish: (Error?) -> Void = { [weak self] error in
            try? sink.close()
            guard let self else { return }

            var done = started
            done.date = Date()
            done.progress = nil

            if error == nil {
                done.kind = .received(url)
                done.detail = "Received · \(Bytes.text(total))"
                if let transferId { store.setPartial(transferId, nil) }
                onFileReceived?(url, device)
            } else if transferId != nil {
                done.kind = .failed
                done.detail = "Paused · resumes when \(device.name) reconnects"
            } else {
                try? files.removeItem(at: url)
                done.kind = .failed
                done.detail = "Transfer from \(device.name) stopped"
            }

            report(key, done, finished: error == nil || transferId == nil)
        }

        let progress = TransferProgress(total: total, alreadyDone: offset)
        let onProgress: (Int64) -> Void = { [weak self] bytes in
            guard let reading = progress.advance(bytes) else { return }

            var moving = started
            moving.progress = reading
            self?.report(key, moving)
        }

        do {
            try device.receivePayload(packet, into: sink, onProgress: onProgress).whenComplete { result in
                if case .failure(let error) = result { finish(error) } else { finish(nil) }
            }
        } catch {
            finish(error)
        }
    }

    /// Shows `activity` in Recent and remembers it under `id` until the transfer is `finished`.
    @discardableResult
    private func report(_ id: String, _ activity: Activity, finished: Bool = false) -> Activity {
        lock.withLock {
            if finished { activities[id] = nil } else { activities[id] = activity }
        }
        onActivity?(activity)

        return activity
    }

    /// Opens a partly received file to add the rest, if it is as long as the sender thinks.
    private func openForAppend(_ url: URL, at offset: Int64) -> (URL, FileHandle)? {
        guard let handle = try? FileHandle(forWritingTo: url),
              let end = try? handle.seekToEnd(), end == UInt64(offset)
        else { return nil }

        return (url, handle)
    }

    /// Creates an empty file with a name not taken yet, such as "photo (2).jpg".
    private func reserveFile(_ name: String) -> (URL, FileHandle)? {
        lock.withLock {
            let base = (name as NSString).deletingPathExtension
            let ext = (name as NSString).pathExtension
            var url = folder.appendingPathComponent(name)
            var copy = 1

            while files.fileExists(atPath: url.path) {
                copy += 1
                let numbered = "\(base) (\(copy))"
                url = folder.appendingPathComponent(ext.isEmpty ? numbered : "\(numbered).\(ext)")
            }

            guard files.createFile(atPath: url.path, contents: nil),
                  let handle = try? FileHandle(forWritingTo: url)
            else { return nil }

            return (url, handle)
        }
    }

    // MARK: Links and text

    private func open(_ link: String, from device: Device) {
        guard let url = URL(string: link), ["http", "https"].contains(url.scheme?.lowercased()) else {
            // Anything but a web link could launch an app, so only offer it as text.
            paste(link, from: device)
            return
        }

        DispatchQueue.main.async { NSWorkspace.shared.open(url) }
        onActivity?(Activity(.link(url), title: url.host ?? link, detail: "Opened from \(device.name)"))
    }

    private func paste(_ text: String, from device: Device) {
        DispatchQueue.main.async {
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(text, forType: .string)
        }

        let preview = text.split(whereSeparator: \.isNewline).first.map(String.init) ?? text
        onActivity?(Activity(.text, title: "“\(preview)”", detail: "Copied from \(device.name)"))
    }
}
