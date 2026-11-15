import AppKit
import Foundation
import FlyyLinkKit

/// Each app's name and a small PNG of its icon, by bundle id, for notifications sent to other devices.
final class AppIcons {
    struct App {
        let name: String
        let iconHash: String?
    }

    private static let size = 96

    private var apps: [String: App] = [:]
    private let lock = NSLock()
    private let folder: URL

    init() {
        let files = FileManager.default
        let caches = files.urls(for: .cachesDirectory, in: .userDomainMask).first ?? files.temporaryDirectory
        folder = caches.appendingPathComponent("FlyyLink/AppIcons", isDirectory: true)
        try? files.createDirectory(at: folder, withIntermediateDirectories: true)
    }

    func app(_ bundleId: String) -> App {
        if let known = lock.withLock({ apps[bundleId] }) { return known }

        let app = load(bundleId)
        lock.withLock { apps[bundleId] = app }
        return app
    }

    /// The saved icon with this hash, if there is one.
    func iconFile(hash: String) -> URL? {
        let url = file(hash)
        return FileManager.default.fileExists(atPath: url.path) ? url : nil
    }

    private func file(_ hash: String) -> URL {
        folder.appendingPathComponent("\(hash).png")
    }

    private func load(_ bundleId: String) -> App {
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleId) else {
            return App(name: bundleId, iconHash: nil)
        }

        let name = (FileManager.default.displayName(atPath: url.path) as NSString).deletingPathExtension
        guard let png = Self.png(NSWorkspace.shared.icon(forFile: url.path)) else { return App(name: name, iconHash: nil) }

        let hash = PhoneNotification.iconHash([UInt8](png))
        guard (try? png.write(to: file(hash), options: .atomic)) != nil else { return App(name: name, iconHash: nil) }

        return App(name: name, iconHash: hash)
    }

    private static func png(_ image: NSImage) -> Data? {
        guard let bitmap = NSBitmapImageRep(
            bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8, samplesPerPixel: 4,
            hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
        ) else { return nil }

        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
        image.draw(in: NSRect(x: 0, y: 0, width: size, height: size))
        NSGraphicsContext.restoreGraphicsState()

        return bitmap.representation(using: .png, properties: [:])
    }
}
