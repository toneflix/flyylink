import CoreImage
import CoreMedia
import Foundation
import ImageIO
import ScreenCaptureKit

/// Records the main display as JPEG pictures, for another computer to view. Needs Screen Recording access.
final class ScreenCapture: NSObject, SCStreamOutput, SCStreamDelegate {
    static let framesPerSecond: Int32 = 30
    static let quality = 0.8

    /// Called on the capture queue with each new picture, encoded as a JPEG.
    var onPicture: ((Data) -> Void)?

    /// Called when capture stops by itself, for example because access was taken away.
    var onStop: ((Error?) -> Void)?

    private let queue = DispatchQueue(label: "flyylink.screen-capture")
    private let context = CIContext()
    private let colorSpace = CGColorSpace(name: CGColorSpace.sRGB)!
    private var stream: SCStream?

    /// macOS asks the user the first time; until they allow it, capture fails.
    static var hasAccess: Bool { CGPreflightScreenCaptureAccess() }

    static func requestAccess() {
        _ = CGRequestScreenCaptureAccess()
    }

    /// Starts recording at most `maxSize` pixels on the longer side. `completion` gets the picture size, or the error.
    func start(maxSize: Int, completion: @escaping (Result<CGSize, Error>) -> Void) {
        SCShareableContent.getExcludingDesktopWindows(false, onScreenWindowsOnly: true) { [weak self] content, error in
            guard let self else { return }
            guard let content, let display = content.displays.first(where: { $0.displayID == CGMainDisplayID() }) ?? content.displays.first else {
                completion(.failure(error ?? CaptureError.noDisplay))
                return
            }

            let pixels = Self.pixels(of: display)
            let size = Self.size(width: pixels.width, height: pixels.height, maxSize: maxSize)
            let configuration = SCStreamConfiguration()
            configuration.width = Int(size.width)
            configuration.height = Int(size.height)
            configuration.minimumFrameInterval = CMTime(value: 1, timescale: Self.framesPerSecond)
            configuration.pixelFormat = kCVPixelFormatType_32BGRA
            configuration.showsCursor = true
            configuration.queueDepth = 3

            let stream = SCStream(filter: SCContentFilter(display: display, excludingWindows: []), configuration: configuration, delegate: self)
            do {
                try stream.addStreamOutput(self, type: .screen, sampleHandlerQueue: queue)
            } catch {
                completion(.failure(error))
                return
            }

            stream.startCapture { error in
                if let error {
                    completion(.failure(error))
                    return
                }

                self.stream = stream
                completion(.success(size))
            }
        }
    }

    func stop() {
        stream?.stopCapture { _ in }
        stream = nil
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        // Frames where nothing changed come without a picture.
        guard type == .screen, sampleBuffer.isValid, let pixels = sampleBuffer.imageBuffer else { return }

        let image = CIImage(cvPixelBuffer: pixels)
        let options = [kCGImageDestinationLossyCompressionQuality as CIImageRepresentationOption: Self.quality]
        guard let jpeg = context.jpegRepresentation(of: image, colorSpace: colorSpace, options: options) else { return }

        onPicture?(jpeg)
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        self.stream = nil
        onStop?(error)
    }

    /// The display's size in pixels; `SCDisplay` gives it in points, half that on a Retina screen.
    static func pixels(of display: SCDisplay) -> (width: Int, height: Int) {
        guard let mode = CGDisplayCopyDisplayMode(display.displayID), mode.pixelWidth > 0, mode.pixelHeight > 0 else {
            return (display.width, display.height)
        }

        return (mode.pixelWidth, mode.pixelHeight)
    }

    /// The display's size, shrunk to fit `maxSize` and rounded to even numbers.
    static func size(width: Int, height: Int, maxSize: Int) -> CGSize {
        let scale = min(1, Double(maxSize) / Double(max(width, height, 1)))

        func even(_ value: Int) -> Double { Double(max(2, Int(Double(value) * scale) & ~1)) }
        return CGSize(width: even(width), height: even(height))
    }

    enum CaptureError: Error {
        case noDisplay
    }
}
