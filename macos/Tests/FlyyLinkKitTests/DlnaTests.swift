import XCTest
@testable import FlyyLinkKit

final class DlnaTests: XCTestCase {
    private let renderer = """
        <?xml version="1.0"?>
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <specVersion><major>1</major><minor>0</minor></specVersion>
          <device>
            <deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType>
            <friendlyName>Living Room TV</friendlyName>
            <UDN>uuid:1234-abcd</UDN>
            <serviceList>
              <service>
                <serviceType>urn:schemas-upnp-org:service:RenderingControl:1</serviceType>
                <controlURL>/upnp/control/RenderingControl1</controlURL>
              </service>
              <service>
                <serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType>
                <controlURL>control/AVTransport1</controlURL>
              </service>
            </serviceList>
          </device>
        </root>
        """

    func testSearchMessageAsksForRenderers() {
        let message = Ssdp.searchMessage()

        XCTAssertTrue(message.hasPrefix("M-SEARCH * HTTP/1.1\r\n"))
        XCTAssertTrue(message.contains("ST: urn:schemas-upnp-org:device:MediaRenderer:1\r\n"))
        XCTAssertTrue(message.contains("MAN: \"ssdp:discover\"\r\n"))
        XCTAssertTrue(message.hasSuffix("\r\n\r\n"))
    }

    func testReadsSearchReplies() {
        let reply = Ssdp.parse(
            "HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=1800\r\nLocation: http://192.168.1.20:9197/dmr\r\n"
                + "ST: urn:schemas-upnp-org:device:MediaRenderer:1\r\nUSN: uuid:1234-abcd::urn:schemas-upnp-org:device:MediaRenderer:1\r\n\r\n"
        )

        XCTAssertEqual(reply?.location, "http://192.168.1.20:9197/dmr")
        XCTAssertEqual(reply?.target, Ssdp.renderer)
        XCTAssertEqual(reply?.usn, "uuid:1234-abcd::urn:schemas-upnp-org:device:MediaRenderer:1")
    }

    func testIgnoresSearchesAndByeNotices() {
        XCTAssertNil(Ssdp.parse(Ssdp.searchMessage()))
        XCTAssertNil(Ssdp.parse("NOTIFY * HTTP/1.1\r\nLOCATION: http://192.168.1.20/dmr\r\nNTS: ssdp:byebye\r\n\r\n"))
        XCTAssertNil(Ssdp.parse("HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\n\r\n"))
    }

    func testReadsRendererDescription() {
        let tv = TvRenderer.parse(renderer, location: "http://192.168.1.20:9197/dmr/desc.xml")

        XCTAssertEqual(
            tv,
            TvRenderer(
                id: "uuid:1234-abcd",
                name: "Living Room TV",
                avTransportUrl: "http://192.168.1.20:9197/dmr/control/AVTransport1",
                renderingControlUrl: "http://192.168.1.20:9197/upnp/control/RenderingControl1"
            )
        )
        XCTAssertEqual(tv?.host, "192.168.1.20")
    }

    func testUsesUrlBaseAndNestedDevices() {
        let xml = """
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <URLBase>http://10.0.0.5:8080/</URLBase>
              <device>
                <friendlyName>Hub</friendlyName>
                <UDN>uuid:hub</UDN>
                <deviceList>
                  <device>
                    <friendlyName>Hisense TV</friendlyName>
                    <UDN>uuid:tv</UDN>
                    <serviceList>
                      <service>
                        <serviceType>urn:schemas-upnp-org:service:AVTransport:2</serviceType>
                        <controlURL>/av</controlURL>
                      </service>
                    </serviceList>
                  </device>
                </deviceList>
              </device>
            </root>
            """

        let tv = TvRenderer.parse(xml, location: "http://10.0.0.5:1400/desc.xml")

        XCTAssertEqual(tv, TvRenderer(id: "uuid:tv", name: "Hisense TV", avTransportUrl: "http://10.0.0.5:8080/av"))
    }

    func testSkipsDevicesThatCantPlay() {
        XCTAssertNil(TvRenderer.parse(renderer.replacingOccurrences(of: "AVTransport", with: "ConnectionManager"), location: "http://192.168.1.20/desc.xml"))
        XCTAssertNil(TvRenderer.parse("not xml", location: "http://192.168.1.20/desc.xml"))
    }

    func testPicksKindFromMime() {
        XCTAssertEqual(CastKind.of("image/jpeg"), .photo)
        XCTAssertEqual(CastKind.of("video/mp4"), .video)
        XCTAssertEqual(CastKind.of("audio/mpeg"), .music)
        XCTAssertNil(CastKind.of("application/pdf"))
        XCTAssertNil(CastKind.of(nil))
    }

    func testBuildsSoapEnvelope() {
        let call = AvTransport.seek(3_723_000)

        XCTAssertEqual(call.soapAction, "\"urn:schemas-upnp-org:service:AVTransport:1#Seek\"")
        XCTAssertEqual(
            call.envelope(),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">"
                + "<s:Body><u:Seek xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">"
                + "<InstanceID>0</InstanceID><Unit>REL_TIME</Unit><Target>1:02:03</Target>"
                + "</u:Seek></s:Body></s:Envelope>"
        )
    }

    func testEscapesMetadataInsideTheEnvelope() throws {
        let call = AvTransport.setUri("http://192.168.1.5:5000/t/a%20b.mp4", title: "Tom & Jerry <1>", mime: "video/mp4", size: 2048)
        let metadata = try XCTUnwrap(call.argument("CurrentURIMetaData"))

        XCTAssertTrue(metadata.contains("<dc:title>Tom &amp; Jerry &lt;1&gt;</dc:title>"))
        XCTAssertTrue(metadata.contains("<upnp:class>object.item.videoItem</upnp:class>"))
        XCTAssertTrue(metadata.contains("<res protocolInfo=\"http-get:*:video/mp4:*\" size=\"2048\">http://192.168.1.5:5000/t/a%20b.mp4</res>"))
        XCTAssertTrue(call.envelope().contains("<CurrentURIMetaData>&lt;DIDL-Lite "))
        XCTAssertTrue(call.envelope().contains("Tom &amp;amp; Jerry"))
    }

    func testReadsPositionAndState() {
        let position = """
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body>
            <u:GetPositionInfoResponse xmlns:u="urn:schemas-upnp-org:service:AVTransport:1">
            <Track>1</Track><TrackDuration>0:04:03</TrackDuration><RelTime>0:01:01.500</RelTime>
            </u:GetPositionInfoResponse></s:Body></s:Envelope>
            """
        let state = """
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body>
            <u:GetTransportInfoResponse xmlns:u="urn:schemas-upnp-org:service:AVTransport:1">
            <CurrentTransportState>PAUSED_PLAYBACK</CurrentTransportState>
            </u:GetTransportInfoResponse></s:Body></s:Envelope>
            """
        let unknown = position.replacingOccurrences(of: "0:04:03", with: "NOT_IMPLEMENTED").replacingOccurrences(of: "0:01:01.500", with: "0:00:00")

        XCTAssertEqual(AvTransport.position(position), CastPosition(position: 61_500, duration: 243_000))
        XCTAssertEqual(AvTransport.position(unknown), CastPosition(position: 0, duration: 0))
        XCTAssertEqual(AvTransport.state(state), .paused)
        XCTAssertEqual(CastState.fromWire("NO_MEDIA_PRESENT"), .stopped)
    }

    func testFormatsAndParsesTimes() {
        XCTAssertEqual(AvTransport.time(-5), "0:00:00")
        XCTAssertEqual(AvTransport.time(65_900), "0:01:05")
        XCTAssertEqual(AvTransport.time(43_200_000), "12:00:00")
        XCTAssertEqual(AvTransport.parseTime("1:02:03"), 3_723_000)
        XCTAssertEqual(AvTransport.parseTime("01:02:03.25"), 3_723_250)
        XCTAssertNil(AvTransport.parseTime("NOT_IMPLEMENTED"))
    }

    func testReadsAndSetsVolume() {
        let response = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:GetVolumeResponse xmlns:u=\"urn:schemas-upnp-org:service:RenderingControl:1\"><CurrentVolume>23</CurrentVolume></u:GetVolumeResponse></s:Body></s:Envelope>"

        XCTAssertEqual(RenderingControl.volume(response), 23)
        XCTAssertEqual(
            RenderingControl.setVolume(140),
            SoapCall(service: RenderingControl.service, action: "SetVolume", arguments: [("InstanceID", "0"), ("Channel", "Master"), ("DesiredVolume", "100")])
        )
    }

    func testParsesRanges() {
        XCTAssertNil(ByteRange.parse(nil, size: 1000))
        XCTAssertNil(ByteRange.parse("items=0-1", size: 1000))
        XCTAssertNil(ByteRange.parse("bytes=0-1,5-6", size: 1000))
        XCTAssertNil(ByteRange.parse("bytes=9-3", size: 1000))
        XCTAssertEqual(ByteRange.parse("bytes=0-", size: 1000), ByteRange(start: 0, end: 999))
        XCTAssertEqual(ByteRange.parse("bytes=100-199", size: 1000), ByteRange(start: 100, end: 199))
        XCTAssertEqual(ByteRange.parse("bytes=900-5000", size: 1000), ByteRange(start: 900, end: 999))
        XCTAssertEqual(ByteRange.parse("bytes=-200", size: 1000), ByteRange(start: 800, end: 999))
        XCTAssertEqual(ByteRange.parse("bytes=-5000", size: 1000), ByteRange(start: 0, end: 999))
        XCTAssertEqual(ByteRange.parse("bytes=100-199", size: 1000)?.length, 100)
        XCTAssertEqual(ByteRange.parse("bytes=1000-", size: 1000)?.isSatisfiable, false)
    }

    func testParsesRequestHead() {
        let head = HttpRequestHead.parse("GET /abc/clip.mp4 HTTP/1.1\r\nHost: 1.2.3.4\r\nRange: bytes=0-\r\n\r\n")

        XCTAssertEqual(head, HttpRequestHead(method: "GET", path: "/abc/clip.mp4", headers: ["host": "1.2.3.4", "range": "bytes=0-"]))
        XCTAssertNil(HttpRequestHead.parse(""))
    }

    func testServerHandsOutWholeFilesAndRanges() async throws {
        let bytes = Data((0..<10_000).map { UInt8($0 % 251) })
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("clip one.mp4")
        try bytes.write(to: file)
        defer { try? FileManager.default.removeItem(at: file) }

        let server = try MediaServer()
        defer { server.close() }

        let link = try server.share(MediaFile(url: file, mime: "video/mp4"), tvHost: "127.0.0.1")
        XCTAssertTrue(link.hasSuffix("/clip%20one.mp4"))
        let url = try XCTUnwrap(URL(string: link))

        let (whole, wholeResponse) = try await URLSession.shared.data(from: url)
        let wholeHttp = try XCTUnwrap(wholeResponse as? HTTPURLResponse)
        XCTAssertEqual(wholeHttp.statusCode, 200)
        XCTAssertEqual(wholeHttp.value(forHTTPHeaderField: "transferMode.dlna.org"), "Streaming")
        XCTAssertEqual(whole, bytes)

        var ranged = URLRequest(url: url)
        ranged.setValue("bytes=9000-", forHTTPHeaderField: "Range")
        let (part, partResponse) = try await URLSession.shared.data(for: ranged)
        let partHttp = try XCTUnwrap(partResponse as? HTTPURLResponse)
        XCTAssertEqual(partHttp.statusCode, 206)
        XCTAssertEqual(partHttp.value(forHTTPHeaderField: "Content-Range"), "bytes 9000-9999/10000")
        XCTAssertEqual(part, bytes.subdata(in: 9000..<10_000))

        server.clear()
        let (_, gone) = try await URLSession.shared.data(from: url)
        XCTAssertEqual((gone as? HTTPURLResponse)?.statusCode, 404)
    }

    func testKeepsTvsUntilTheyMissTwoSearches() {
        let living = TvRenderer(id: "uuid:a", name: "Living Room", avTransportUrl: "http://10.0.0.2/av")
        let bedroom = TvRenderer(id: "uuid:b", name: "bedroom", avTransportUrl: "http://10.0.0.3/av")
        let list = TvList()

        XCTAssertEqual(list.update([living, bedroom]), [bedroom, living])
        XCTAssertEqual(list.update([living]), [bedroom, living])
        XCTAssertEqual(list.update([living, bedroom]), [bedroom, living])
        XCTAssertEqual(list.update([]), [bedroom, living])
        XCTAssertEqual(list.update([]), [])
    }
}
