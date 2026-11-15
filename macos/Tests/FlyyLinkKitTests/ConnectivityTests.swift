import XCTest
@testable import FlyyLinkKit

/// ConnectivityTest.kt and ConnectivityTests.cs check the same.
final class ConnectivityTests: XCTestCase {
    func testReadsKdeConnectivityReport() throws {
        let packet = try NetworkPacket.parse(#"{"id":1,"type":"kdeconnect.connectivity_report","body":{"signalStrengths":{"6":{"networkType":"LTE","signalStrength":3},"7":{"networkType":"GSM","signalStrength":9}}}}"#)

        let report = try XCTUnwrap(ConnectivityReport(packet: packet))

        XCTAssertEqual(report.signals["6"], CellSignal(networkType: "LTE", strength: 3))
        XCTAssertEqual(report.signals["7"], CellSignal(networkType: "GSM", strength: 4))
        XCTAssertEqual(report.best, CellSignal(networkType: "GSM", strength: 4))
    }

    func testRoundTripsAndPicksTheStrongestSim() throws {
        let report = ConnectivityReport(signals: ["1": CellSignal(networkType: CellSignal.nr, strength: 2), "2": CellSignal(networkType: CellSignal.lte, strength: 2)])

        let back = try XCTUnwrap(ConnectivityReport(packet: try NetworkPacket.parse(try report.toPacket().serialize())))

        XCTAssertEqual(back, report)
        XCTAssertEqual(back.best, CellSignal(networkType: CellSignal.nr, strength: 2))
    }

    func testEmptyReportHasNoSignal() throws {
        let report = try XCTUnwrap(ConnectivityReport(packet: NetworkPacket(type: PacketType.connectivityReport)))

        XCTAssertNil(report.best)
        XCTAssertNil(ConnectivityReport(packet: ConnectivityReport.request()))
    }

    func testNamesGenerations() {
        XCTAssertEqual(CellSignal(networkType: CellSignal.nr, strength: 4).generation, "5G")
        XCTAssertEqual(CellSignal(networkType: CellSignal.lte, strength: 4).generation, "4G")
        XCTAssertEqual(CellSignal(networkType: CellSignal.umts, strength: 4).generation, "3G")
        XCTAssertEqual(CellSignal(networkType: CellSignal.edge, strength: 4).generation, "E")
        XCTAssertEqual(CellSignal(networkType: CellSignal.gsm, strength: 4).generation, "2G")
        XCTAssertEqual(CellSignal(networkType: CellSignal.unknown, strength: 0).generation, "")
    }
}
