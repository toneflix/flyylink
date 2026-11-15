import XCTest
@testable import FlyyLinkKit

final class InputTests: XCTestCase {
    func testEveryInputRoundTrips() throws {
        let inputs: [RemoteInput] = [
            .enter(edge: .left, position: 0.25, platform: RemoteInput.windows),
            .leave(edge: .bottom, position: 1),
            .release,
            .move(dx: -3.5, dy: 12),
            .moveTo(x: 0.5, y: 0),
            .button(.right, down: true),
            .scroll(dx: 0, dy: -40),
            .key(code: "KeyA", down: false),
            .key(code: "MetaLeft", down: true),
            .key(code: "AudioVolumeUp", down: true),
            .begin(platform: RemoteInput.android, ready: true),
            .begin(platform: RemoteInput.macOS, ready: false),
            .text("Héllo 👋"),
        ]

        for input in inputs {
            let back = RemoteInput(packet: try NetworkPacket.parse(try input.toPacket().serialize()))
            XCTAssertEqual(back, input)
        }
    }

    func testRejectsNonsense() {
        func packet(_ body: [String: JSONValue]) -> NetworkPacket { NetworkPacket(type: PacketType.input, body: body) }

        XCTAssertNil(RemoteInput(packet: packet(["action": "enter", "edge": "middle", "position": .double(0.5)])))
        XCTAssertNil(RemoteInput(packet: packet(["action": "moveTo", "x": .double(1.5), "y": .double(0.5)])))
        XCTAssertNil(RemoteInput(packet: packet(["action": "move", "dx": 50_000, "dy": 0])))
        XCTAssertNil(RemoteInput(packet: packet(["action": "key", "code": "Hyper", "down": true])))
        XCTAssertNil(RemoteInput(packet: packet(["action": "button", "button": "left"])))
        XCTAssertNil(RemoteInput(packet: packet(["action": "text", "text": ""])))
        XCTAssertNil(RemoteInput(packet: packet(["action": "text", "text": .string(String(repeating: "a", count: RemoteInput.maxText + 1))])))
        XCTAssertNil(RemoteInput(packet: packet(["action": "dance"])))
    }

    func testBeginWithoutReadyMeansReady() {
        let packet = NetworkPacket(type: PacketType.input, body: ["action": "begin", "platform": .string(RemoteInput.windows)])

        XCTAssertEqual(RemoteInput(packet: packet), .begin(platform: RemoteInput.windows, ready: true))
    }

    func testEdgesMapPointsBothWays() {
        XCTAssertEqual(ScreenEdge.left.opposite, .right)
        XCTAssertEqual(ScreenEdge.top.opposite, .bottom)

        XCTAssertEqual(ScreenEdge.right.position(x: 1920, y: 270, inMinX: 0, minY: 0, maxX: 1920, maxY: 1080), 0.25)
        XCTAssertEqual(ScreenEdge.bottom.position(x: -10, y: 1080, inMinX: 0, minY: 0, maxX: 1920, maxY: 1080), 0)

        let point = ScreenEdge.left.point(at: 0.5, minX: 0, minY: 0, maxX: 1440, maxY: 900)
        XCTAssertEqual(point.x, 2)
        XCTAssertEqual(point.y, 450)

        XCTAssertTrue(ScreenEdge.top.reached(x: 100, y: 0, minX: 0, minY: 0, maxX: 1440, maxY: 900))
        XCTAssertFalse(ScreenEdge.right.reached(x: 1000, y: 0, minX: 0, minY: 0, maxX: 1440, maxY: 900))
    }

    func testKeyNamesCoverTheKeyboard() {
        XCTAssertTrue(KeyCodes.all.contains("KeyZ"))
        XCTAssertTrue(KeyCodes.all.contains("Digit0"))
        XCTAssertTrue(KeyCodes.all.contains("F12"))
        XCTAssertTrue(KeyCodes.all.contains("NumpadEnter"))
        XCTAssertTrue(KeyCodes.all.contains("MediaPlayPause"))
        XCTAssertEqual(KeyCodes.swappingControlAndMeta("ControlLeft"), "MetaLeft")
        XCTAssertEqual(KeyCodes.swappingControlAndMeta("KeyC"), "KeyC")
    }
}
