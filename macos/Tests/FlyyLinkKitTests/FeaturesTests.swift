import XCTest
@testable import FlyyLinkKit

final class FeaturesTests: XCTestCase {
    private func wire(_ packet: NetworkPacket) throws -> NetworkPacket {
        try NetworkPacket.parse(packet.serialize())
    }

    func testUnreportedFeaturesAreAllOn() {
        PhoneFeature.allCases.forEach { XCTAssertTrue(PhoneFeatures.all.contains($0)) }
        XCTAssertTrue(PhoneFeatures.all.sends(PacketType.smsMessages))
        XCTAssertTrue(PhoneFeatures.all.accepts(PacketType.screenInput))
    }

    func testFeaturesThatAreOffBlockTheirPackets() {
        let features = PhoneFeatures.of(.clipboard, .mirroring)

        XCTAssertTrue(features.sends(PacketType.clipboard))
        XCTAssertTrue(features.accepts(PacketType.screenRequest))
        XCTAssertFalse(features.sends(PacketType.notification))
        XCTAssertFalse(features.accepts(PacketType.smsRequestConversations))
        XCTAssertFalse(features.accepts(PacketType.screenInput))
    }

    func testPacketsOfNoFeatureAlwaysGo() {
        let none = PhoneFeatures.of()

        for type in [PacketType.ping, PacketType.shareRequest, PacketType.findMyPhone, PacketType.battery, PacketType.features] {
            XCTAssertTrue(none.sends(type))
            XCTAssertTrue(none.accepts(type))
        }
    }

    func testPhonesTakeComputerNotificationsWithTheirOwnOff() {
        let none = PhoneFeatures.of()

        XCTAssertTrue(none.accepts(PacketType.notification))
        XCTAssertTrue(none.sends(PacketType.notificationRequest))
    }

    func testPacketRoundTripsAndSkipsUnknownNames() throws {
        let features = PhoneFeatures.of(.photos, .location)

        XCTAssertEqual(PhoneFeatures(packet: try wire(features.toPacket())), features)

        let future = NetworkPacket(type: PacketType.features, body: ["features": .array([.string("photos"), .string("teleport")])])
        XCTAssertEqual(PhoneFeatures(packet: future), .of(.photos))
        XCTAssertNil(PhoneFeatures(packet: NetworkPacket(type: PacketType.features)))
        XCTAssertNil(PhoneFeatures(packet: Clipboard.packet("hi")))
    }

    func testIdentityCarriesFeaturesButTheAnnouncementDoesNot() throws {
        let identity = DeviceIdentity(
            deviceId: DeviceIdentity.newDeviceId(),
            deviceName: "Pixel 9",
            deviceType: .phone,
            tcpPort: 1716,
            features: .of(.notifications)
        )

        XCTAssertEqual(try DeviceIdentity(packet: wire(identity.toPacket())), identity)
        XCTAssertEqual(try DeviceIdentity(packet: wire(identity.announcement())).features, .all)
    }

    func testNothingOnIsNotTheSameAsUnreported() throws {
        let identity = DeviceIdentity(deviceId: DeviceIdentity.newDeviceId(), deviceName: "Pixel 9", deviceType: .phone, features: .of())
        let heard = try DeviceIdentity(packet: wire(identity.toPacket()))

        XCTAssertEqual(heard.features, .of())
        XCTAssertFalse(heard.features.contains(.files))
    }

    /// The same names as Features.kt and Features.cs.
    func testWireNamesMatchTheOtherApps() {
        XCTAssertEqual(
            PhoneFeature.allCases.map(\.rawValue),
            ["notifications", "media", "messages", "clipboard", "photos", "files", "mirroring", "control", "location"]
        )
    }
}
