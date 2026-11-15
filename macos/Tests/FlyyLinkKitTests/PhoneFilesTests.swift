import XCTest
@testable import FlyyLinkKit

/// Mirrors PhoneFilesTest.kt and PhoneFilesTests.cs.
final class PhoneFilesTests: XCTestCase {
    private func wire(_ packet: NetworkPacket) throws -> NetworkPacket {
        try NetworkPacket.parse(try packet.serialize())
    }

    func testCleansPaths() {
        XCTAssertEqual(PhoneFiles.clean(""), "/")
        XCTAssertEqual(PhoneFiles.clean("/"), "/")
        XCTAssertEqual(PhoneFiles.clean("DCIM//./Camera/"), "/DCIM/Camera")
        XCTAssertNil(PhoneFiles.clean("/DCIM/../Android"))
        XCTAssertNil(PhoneFiles.clean("/a\0b"))
    }

    func testSplitsPaths() {
        XCTAssertEqual(PhoneFiles.join("/", "Music"), "/Music")
        XCTAssertEqual(PhoneFiles.join("/Music", "a.mp3"), "/Music/a.mp3")
        XCTAssertEqual(PhoneFiles.name(of: "/Music/a.mp3"), "a.mp3")
        XCTAssertEqual(PhoneFiles.parent(of: "/Music/a.mp3"), "/Music")
        XCTAssertEqual(PhoneFiles.parent(of: "/Music"), "/")
        XCTAssertTrue(PhoneFiles.isValidName("Notes.txt"))
        XCTAssertFalse(PhoneFiles.isValidName(".."))
        XCTAssertFalse(PhoneFiles.isValidName("a/b"))
    }

    func testRequestsRoundTrip() throws {
        let list = PhoneFiles.Request(id: 7, action: .list, path: "/DCIM")
        let move = PhoneFiles.Request(id: 8, action: .move, path: "/a.txt", to: "/Documents/a.txt")

        XCTAssertEqual(PhoneFiles.request(try wire(PhoneFiles.request(list))), list)
        XCTAssertEqual(PhoneFiles.request(try wire(PhoneFiles.request(move))), move)
    }

    func testRefusesBadRequests() throws {
        XCTAssertNil(PhoneFiles.request(try wire(PhoneFiles.request(.init(id: 1, action: .list, path: "/../etc")))))
        XCTAssertNil(PhoneFiles.request(try wire(PhoneFiles.request(.init(id: 1, action: .move, path: "/a")))))
        XCTAssertNil(PhoneFiles.request(try wire(PhoneFiles.request(.init(id: 1, action: .move, path: "/a", to: "/")))))
        XCTAssertNil(PhoneFiles.request(NetworkPacket(type: PacketType.filesRequest, body: ["id": .int(1), "action": .string("format"), "path": .string("/")])))
    }

    func testAnswersRoundTrip() throws {
        let files = [
            PhoneFile(path: "/DCIM", isFolder: true, modified: 1_700_000_000_000),
            PhoneFile(path: "/notes.txt", isFolder: false, size: 12, modified: 1_700_000_100_000),
        ]
        let listing = PhoneFiles.Answer(id: 7, files: files)
        let sent = PhoneFiles.Answer(id: 8, file: files[1])
        let failed = PhoneFiles.Answer(id: 9, failure: .denied)

        XCTAssertEqual(PhoneFiles.answer(try wire(PhoneFiles.answer(listing))), listing)
        XCTAssertEqual(PhoneFiles.answer(try wire(PhoneFiles.answer(sent))), sent)
        XCTAssertEqual(PhoneFiles.answer(try wire(PhoneFiles.answer(failed))), failed)
        XCTAssertEqual(files[1].name, "notes.txt")
    }

    func testReadsUnknownFailureAsFailed() throws {
        let packet = try NetworkPacket.parse(#"{"id":1,"type":"flyylink.files","body":{"id":3,"ok":false,"error":"disk on fire"}}"#)

        XCTAssertEqual(PhoneFiles.answer(packet)?.failure, .failed)
    }

    func testPhonesAdvertiseFilesButTvsDont() {
        XCTAssertTrue(Capabilities.mvp.contains(PacketType.filesRequest))
        XCTAssertFalse(Capabilities.tv.contains(PacketType.filesRequest))
    }
}
