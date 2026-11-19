import XCTest
@testable import FlyyLinkKit

/// Mirrors BrowserTests.cs.
final class BrowserTests: XCTestCase {
    private func object(_ json: String) -> [String: JSONValue] {
        BrowserEndpoint.object(json) ?? [:]
    }

    func testFramesRoundTripInPieces() {
        let bytes = NativeMessaging.frame(#"{"type":"hello"}"#) + NativeMessaging.frame(#"{"a":"é"}"#)
        var reader = NativeMessageReader()

        reader.append(bytes.prefix(3))
        XCTAssertNil(reader.next())
        reader.append(bytes.dropFirst(3))
        XCTAssertEqual(reader.next(), #"{"type":"hello"}"#)
        XCTAssertEqual(reader.next(), #"{"a":"é"}"#)
        XCTAssertNil(reader.next())
        XCTAssertEqual(Array(bytes.prefix(4)), [16, 0, 0, 0])
    }

    func testABrokenLengthStopsTheReader() {
        var reader = NativeMessageReader()
        reader.append(Data([0xFF, 0xFF, 0xFF, 0xFF, 1]))

        XCTAssertNil(reader.next())
        XCTAssertTrue(reader.isBroken)
    }

    func testManifestsNameTheAddOns() throws {
        let chrome = try XCTUnwrap(JSONSerialization.jsonObject(with: Browser.hostManifest(hostPath: "/Applications/FlyyLink.app/Contents/MacOS/FlyyLinkBrowserHost", firefox: false)) as? [String: Any])
        let firefox = try XCTUnwrap(JSONSerialization.jsonObject(with: Browser.hostManifest(hostPath: "/Applications/FlyyLink.app/Contents/MacOS/FlyyLinkBrowserHost", firefox: true)) as? [String: Any])

        XCTAssertEqual(chrome["name"] as? String, "com.toneflix.flyylink")
        XCTAssertEqual(chrome["type"] as? String, "stdio")
        XCTAssertEqual(chrome["allowed_origins"] as? [String], ["chrome-extension://dnlkpekmfbjelcenommpiibkcdbooacj/"])
        XCTAssertNil(chrome["allowed_extensions"])
        XCTAssertEqual(firefox["path"] as? String, "/Applications/FlyyLink.app/Contents/MacOS/FlyyLinkBrowserHost")
        XCTAssertEqual(firefox["allowed_extensions"] as? [String], ["browser@flyylink.toneflix.net"])
    }

    func testEndpointsCheckTheToken() {
        let endpoint = BrowserEndpoint(port: 52011, token: "abc123")

        XCTAssertEqual(BrowserEndpoint(json: endpoint.json), endpoint)
        XCTAssertTrue(endpoint.accepts(endpoint.helloLine))
        XCTAssertFalse(endpoint.accepts(#"{"token":"nope"}"#))
        XCTAssertFalse(endpoint.accepts("not json"))
        XCTAssertNil(BrowserEndpoint(json: #"{"port":70000,"token":"x"}"#))
        XCTAssertEqual(BrowserEndpoint.newToken().count, 32)
    }

    func testCleansNumbers() {
        XCTAssertEqual(Browser.cleanNumber(" +234 (803) 123-4567 "), "+2348031234567")
        XCTAssertEqual(Browser.cleanNumber("*123#"), "*123#")
        XCTAssertEqual(Browser.cleanNumber("0803+123 4567"), "08031234567")
        XCTAssertNil(Browser.cleanNumber("12"))
        XCTAssertNil(Browser.cleanNumber("call me"))
    }

    func testParsesRequests() {
        XCTAssertEqual(BrowserRequest(line: #"{"type":"hello","browser":"Chrome"}"#), BrowserRequest(.hello(browser: "Chrome")))
        XCTAssertEqual(
            BrowserRequest(line: #"{"type":"share","id":"7","device":"phone1","link":"https://example.com/a#:~:text=Hello"}"#),
            BrowserRequest(id: "7", .share(device: "phone1", item: .link("https://example.com/a#:~:text=Hello")))
        )
        XCTAssertEqual(
            BrowserRequest(line: #"{"type":"share","device":"tv","fetch":"https://example.com/cat.jpg","name":"../cat.jpg"}"#),
            BrowserRequest(.share(device: "tv", item: .fetch(url: "https://example.com/cat.jpg", name: "cat.jpg")))
        )
        XCTAssertEqual(
            BrowserRequest(line: #"{"type":"share","device":"phone1","text":"Hello there"}"#),
            BrowserRequest(.share(device: "phone1", item: .text("Hello there")))
        )
        XCTAssertEqual(BrowserRequest(line: #"{"type":"call","number":"tel:+234 803 123 4567"}"#), BrowserRequest(.call(device: nil, number: "+2348031234567")))
        XCTAssertEqual(
            BrowserRequest(line: #"{"type":"text","device":"phone1","number":"0803 123 4567","body":"On my way"}"#),
            BrowserRequest(.text(device: "phone1", number: "08031234567", body: "On my way"))
        )
        XCTAssertEqual(
            BrowserRequest(line: #"{"type":"playOnTv","tv":"tv-1","url":"https://cdn.example.com/v.mp4","title":"Trailer","mime":"video/mp4"}"#),
            BrowserRequest(.playOnTv(tv: "tv-1", url: "https://cdn.example.com/v.mp4", title: "Trailer", mime: "video/mp4"))
        )
    }

    func testRejectsBadRequests() {
        XCTAssertNil(BrowserRequest(line: "nonsense"))
        XCTAssertNil(BrowserRequest(line: #"{"type":"share","device":"p","link":"javascript:alert(1)"}"#))
        XCTAssertNil(BrowserRequest(line: #"{"type":"share","device":"p","file":"relative/path.txt"}"#))
        XCTAssertNil(BrowserRequest(line: #"{"type":"text","number":"0803","body":""}"#))
        XCTAssertNil(BrowserRequest(line: #"{"type":"playOnTv","tv":"t","url":"blob:https://x/1"}"#))
        XCTAssertNil(BrowserRequest(line: #"{"type":"launch"}"#))
    }

    func testWritesEvents() {
        let state = object(BrowserEvents.state(BrowserState(
            status: BrowserState.ready,
            computer: "Obi's MacBook",
            platform: "macos",
            devices: [BrowserDevice(id: "p1", name: "Pixel", type: "phone", canCall: true, canText: true)],
            tvs: [BrowserTv(id: "tv1", name: "Living room")]
        )))
        let control = object(BrowserEvents.control(player: "a", action: .playPause, seekTo: 5000))

        XCTAssertEqual(state["type"], "state")
        XCTAssertEqual(state["devices"]?.arrayValue?.first?.objectValue?["name"], "Pixel")
        XCTAssertEqual(state["devices"]?.arrayValue?.first?.objectValue?["canCall"], true)
        XCTAssertEqual(state["tvs"]?.arrayValue?.first?.objectValue?["name"], "Living room")
        XCTAssertEqual(control["action"], "PlayPause")
        XCTAssertEqual(control["seekTo"], 5000)
        XCTAssertEqual(object(BrowserEvents.result(id: "3", ok: false, error: "offline")), ["type": "result", "id": "3", "ok": false, "error": "offline"])
        XCTAssertEqual(object(BrowserEvents.code("123456", app: "Messages", device: "Pixel")), ["type": "code", "code": "123456", "app": "Messages", "device": "Pixel"])
    }

    func testNamesBrowserPlayers() throws {
        let media = BrowserMedia()
        let line = #"{"type":"media","players":[{"id":"1:0","site":"YouTube","title":"Song","playing":true,"position":1500.7,"length":200000,"canSeek":true,"artwork":"https://i.ytimg.com/a.jpg"},{"id":"2:0","site":"YouTube","title":"Other"}]}"#
        guard case .media(let players) = try XCTUnwrap(BrowserRequest(line: line)).kind else { return XCTFail("not media") }

        media.update(browser: "a", browserName: "Chrome", players: players)
        XCTAssertEqual(media.players.map(\.name), ["YouTube in Chrome", "YouTube in Chrome 2"])
        XCTAssertEqual(media.players[0].position, 1500)
        XCTAssertTrue(media.players[0].canSeek)
        XCTAssertEqual(media.players[0].albumArtUrl, "https://i.ytimg.com/a.jpg")
        XCTAssertEqual(media.find("YouTube in Chrome 2")?.browser, "a")
        XCTAssertEqual(media.find("YouTube in Chrome 2")?.playerId, "2:0")
        XCTAssertTrue(media.hasArtwork("https://i.ytimg.com/a.jpg"))

        media.update(browser: "a", browserName: "Chrome", players: [players[1]])
        XCTAssertEqual(media.players.map(\.name), ["YouTube in Chrome 2"])

        media.update(browser: "b", browserName: "Firefox", players: [players[0]])
        XCTAssertEqual(media.find("YouTube in Firefox")?.playerId, "1:0")

        media.forget(browser: "a")
        XCTAssertEqual(media.players.map(\.name), ["YouTube in Firefox"])
        XCTAssertNil(media.find("YouTube in Chrome 2"))
    }
}
