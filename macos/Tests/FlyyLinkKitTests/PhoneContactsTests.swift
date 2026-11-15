import XCTest
@testable import FlyyLinkKit

/// Mirrors PhoneContactsTest.kt and PhoneContactsTests.cs.
final class PhoneContactsTests: XCTestCase {
    private func wire(_ packet: NetworkPacket) throws -> NetworkPacket {
        try NetworkPacket.parse(try packet.serialize())
    }

    private let ada = PhoneContact(name: "Ada Obi", numbers: ["+234 803 555 0101", "0803-555-0102"])
    private let bayo = PhoneContact(name: "Bayo", numbers: ["08125550199"])
    private let nada = PhoneContact(name: "Nada", numbers: ["+44 20 7946 0000"])

    func testContactsRoundTrip() throws {
        XCTAssertEqual(PhoneContacts.contacts(try wire(PhoneContacts.packet([ada, bayo]))), [ada, bayo])
        XCTAssertTrue(PhoneContacts.isRequest(try wire(PhoneContacts.request())))
    }

    func testDropsContactsWithoutNameOrNumber() throws {
        let packet = try NetworkPacket.parse(#"{"id":1,"type":"flyylink.contacts","body":{"contacts":[{"name":"","numbers":["1"]},{"name":"Ann","numbers":[]},{"name":"Bo","numbers":[" 2 "]}]}}"#)

        XCTAssertEqual(PhoneContacts.contacts(packet), [PhoneContact(name: "Bo", numbers: ["2"])])
    }

    func testMatchesNamesAndNumbers() {
        XCTAssertTrue(ada.matches("obi"))
        XCTAssertTrue(ada.matches("555 0102"))
        XCTAssertTrue(ada.matches(""))
        XCTAssertFalse(ada.matches("Bayo"))
        XCTAssertFalse(ada.matches("+"))
    }

    func testSearchPutsNameStartsFirst() {
        XCTAssertEqual(PhoneContacts.search([nada, bayo, ada], "ada"), [ada, nada])
        XCTAssertEqual(PhoneContacts.search([nada, bayo, ada], "0812"), [bayo])
    }

    func testPhonesAndComputersShareContactsButTvsDont() {
        XCTAssertTrue(Capabilities.mvp.contains(PacketType.contacts))
        XCTAssertFalse(Capabilities.tv.contains(PacketType.contactsRequest))
    }
}
