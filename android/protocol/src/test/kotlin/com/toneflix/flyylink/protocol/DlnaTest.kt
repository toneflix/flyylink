package com.toneflix.flyylink.protocol

import com.sun.net.httpserver.HttpServer
import java.io.ByteArrayInputStream
import java.net.HttpURLConnection
import java.net.InetSocketAddress
import java.net.URL
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class DlnaTest {
    private val description = """
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
    """.trimIndent()

    @Test fun searchMessageAsksForRenderers() {
        val message = Ssdp.searchMessage()

        assertTrue(message.startsWith("M-SEARCH * HTTP/1.1\r\n"))
        assertTrue("ST: urn:schemas-upnp-org:device:MediaRenderer:1\r\n" in message)
        assertTrue("MAN: \"ssdp:discover\"\r\n" in message)
        assertTrue(message.endsWith("\r\n\r\n"))
    }

    @Test fun readsSearchReplies() {
        val reply = Ssdp.parse(
            "HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=1800\r\nLocation: http://192.168.1.20:9197/dmr\r\n" +
                "ST: urn:schemas-upnp-org:device:MediaRenderer:1\r\nUSN: uuid:1234-abcd::urn:schemas-upnp-org:device:MediaRenderer:1\r\n\r\n",
        )

        assertEquals("http://192.168.1.20:9197/dmr", reply?.location)
        assertEquals(Ssdp.RENDERER, reply?.target)
        assertEquals("uuid:1234-abcd::urn:schemas-upnp-org:device:MediaRenderer:1", reply?.usn)
    }

    @Test fun ignoresSearchesAndByeNotices() {
        assertNull(Ssdp.parse(Ssdp.searchMessage()))
        assertNull(Ssdp.parse("NOTIFY * HTTP/1.1\r\nLOCATION: http://192.168.1.20/dmr\r\nNTS: ssdp:byebye\r\n\r\n"))
        assertNull(Ssdp.parse("HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\n\r\n"))
    }

    @Test fun readsRendererDescription() {
        val tv = TvRenderer.parse(description, "http://192.168.1.20:9197/dmr/desc.xml")

        assertEquals(
            TvRenderer(
                id = "uuid:1234-abcd",
                name = "Living Room TV",
                avTransportUrl = "http://192.168.1.20:9197/dmr/control/AVTransport1",
                renderingControlUrl = "http://192.168.1.20:9197/upnp/control/RenderingControl1",
            ),
            tv,
        )
        assertEquals("192.168.1.20", tv?.host)
    }

    @Test fun usesUrlBaseAndNestedDevices() {
        val xml = """
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
        """.trimIndent()

        val tv = TvRenderer.parse(xml, "http://10.0.0.5:1400/desc.xml")

        assertEquals(TvRenderer("uuid:tv", "Hisense TV", "http://10.0.0.5:8080/av", null), tv)
    }

    @Test fun skipsDevicesThatCantPlay() {
        assertNull(TvRenderer.parse(description.replace("AVTransport", "ConnectionManager"), "http://192.168.1.20/desc.xml"))
        assertNull(TvRenderer.parse("not xml", "http://192.168.1.20/desc.xml"))
    }

    @Test fun picksKindFromMime() {
        assertEquals(CastKind.PHOTO, CastKind.of("image/jpeg"))
        assertEquals(CastKind.VIDEO, CastKind.of("video/mp4"))
        assertEquals(CastKind.MUSIC, CastKind.of("audio/mpeg"))
        assertNull(CastKind.of("application/pdf"))
        assertNull(CastKind.of(null))
    }

    @Test fun buildsSoapEnvelope() {
        val call = AvTransport.seek(3_723_000)

        assertEquals("\"urn:schemas-upnp-org:service:AVTransport:1#Seek\"", call.soapAction)
        assertEquals(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body><u:Seek xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">" +
                "<InstanceID>0</InstanceID><Unit>REL_TIME</Unit><Target>1:02:03</Target>" +
                "</u:Seek></s:Body></s:Envelope>",
            call.envelope(),
        )
    }

    @Test fun escapesMetadataInsideTheEnvelope() {
        val call = AvTransport.setUri("http://192.168.1.5:5000/t/a%20b.mp4", "Tom & Jerry <1>", "video/mp4", 2048)
        val metadata = call.arguments.toMap().getValue("CurrentURIMetaData")

        assertTrue("<dc:title>Tom &amp; Jerry &lt;1&gt;</dc:title>" in metadata)
        assertTrue("<upnp:class>object.item.videoItem</upnp:class>" in metadata)
        assertTrue("<res protocolInfo=\"http-get:*:video/mp4:*\" size=\"2048\">http://192.168.1.5:5000/t/a%20b.mp4</res>" in metadata)
        assertTrue("<CurrentURIMetaData>&lt;DIDL-Lite " in call.envelope())
        assertTrue("Tom &amp;amp; Jerry" in call.envelope())
    }

    @Test fun readsPositionAndState() {
        val position = """
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body>
            <u:GetPositionInfoResponse xmlns:u="urn:schemas-upnp-org:service:AVTransport:1">
            <Track>1</Track><TrackDuration>0:04:03</TrackDuration><RelTime>0:01:01.500</RelTime>
            </u:GetPositionInfoResponse></s:Body></s:Envelope>
        """.trimIndent()
        val state = """
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body>
            <u:GetTransportInfoResponse xmlns:u="urn:schemas-upnp-org:service:AVTransport:1">
            <CurrentTransportState>PAUSED_PLAYBACK</CurrentTransportState>
            </u:GetTransportInfoResponse></s:Body></s:Envelope>
        """.trimIndent()

        assertEquals(CastPosition(61_500, 243_000), AvTransport.position(position))
        assertEquals(CastPosition(0, 0), AvTransport.position(position.replace("0:04:03", "NOT_IMPLEMENTED").replace("0:01:01.500", "0:00:00")))
        assertEquals(CastState.PAUSED, AvTransport.state(state))
        assertEquals(CastState.STOPPED, CastState.fromWire("NO_MEDIA_PRESENT"))
    }

    @Test fun formatsAndParsesTimes() {
        assertEquals("0:00:00", AvTransport.time(-5))
        assertEquals("0:01:05", AvTransport.time(65_900))
        assertEquals("12:00:00", AvTransport.time(43_200_000))
        assertEquals(3_723_000L, AvTransport.parseTime("1:02:03"))
        assertEquals(3_723_250L, AvTransport.parseTime("01:02:03.25"))
        assertNull(AvTransport.parseTime("NOT_IMPLEMENTED"))
    }

    @Test fun readsAndSetsVolume() {
        val response = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:GetVolumeResponse xmlns:u=\"urn:schemas-upnp-org:service:RenderingControl:1\"><CurrentVolume>23</CurrentVolume></u:GetVolumeResponse></s:Body></s:Envelope>"

        assertEquals(23, RenderingControl.volume(response))
        assertEquals(
            listOf("InstanceID" to "0", "Channel" to "Master", "DesiredVolume" to "100"),
            RenderingControl.setVolume(140).arguments,
        )
    }

    @Test fun parsesRanges() {
        assertNull(ByteRange.parse(null, 1000))
        assertNull(ByteRange.parse("items=0-1", 1000))
        assertNull(ByteRange.parse("bytes=0-1,5-6", 1000))
        assertNull(ByteRange.parse("bytes=9-3", 1000))
        assertEquals(ByteRange(0, 999), ByteRange.parse("bytes=0-", 1000))
        assertEquals(ByteRange(100, 199), ByteRange.parse("bytes=100-199", 1000))
        assertEquals(ByteRange(900, 999), ByteRange.parse("bytes=900-5000", 1000))
        assertEquals(ByteRange(800, 999), ByteRange.parse("bytes=-200", 1000))
        assertEquals(ByteRange(0, 999), ByteRange.parse("bytes=-5000", 1000))
        assertEquals(100L, ByteRange.parse("bytes=100-199", 1000)?.length)
        assertFalse(ByteRange.parse("bytes=1000-", 1000)!!.isSatisfiable)
    }

    @Test fun parsesRequestHead() {
        val head = HttpRequestHead.parse("GET /abc/clip.mp4 HTTP/1.1\r\nHost: 1.2.3.4\r\nRange: bytes=0-\r\n\r\n")

        assertEquals(HttpRequestHead("GET", "/abc/clip.mp4", mapOf("host" to "1.2.3.4", "range" to "bytes=0-")), head)
        assertNull(HttpRequestHead.parse(""))
    }

    @Test fun serverHandsOutWholeFilesAndRanges() {
        val bytes = ByteArray(10_000) { (it % 251).toByte() }
        val source = object : MediaServer.Source {
            override val name = "clip one.mp4"
            override val mime = "video/mp4"
            override val size = bytes.size.toLong()
            override fun open() = ByteArrayInputStream(bytes)
        }

        MediaServer().use { server ->
            val url = server.share(source, "127.0.0.1")
            assertTrue(url.endsWith("/clip%20one.mp4"))

            val whole = URL(url).openConnection() as HttpURLConnection
            assertEquals(200, whole.responseCode)
            assertEquals("Streaming", whole.getHeaderField("transferMode.dlna.org"))
            assertTrue(bytes.contentEquals(whole.inputStream.readBytes()))

            val part = URL(url).openConnection() as HttpURLConnection
            part.setRequestProperty("Range", "bytes=9000-")
            assertEquals(206, part.responseCode)
            assertEquals("bytes 9000-9999/10000", part.getHeaderField("Content-Range"))
            assertTrue(bytes.copyOfRange(9000, 10_000).contentEquals(part.inputStream.readBytes()))

            val past = URL(url).openConnection() as HttpURLConnection
            past.setRequestProperty("Range", "bytes=20000-")
            assertEquals(416, past.responseCode)

            server.clear()
            assertEquals(404, (URL(url).openConnection() as HttpURLConnection).responseCode)
        }
    }

    @Test fun playerSendsActionsToTheTv() {
        val actions = mutableListOf<String>()
        val bodies = mutableListOf<String>()
        val fake = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)

        fake.createContext("/av") { exchange ->
            actions += exchange.requestHeaders.getFirst("SOAPACTION")
            bodies += exchange.requestBody.readBytes().toString(Charsets.UTF_8)

            val reply = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:GetTransportInfoResponse xmlns:u=\"${AvTransport.SERVICE}\"><CurrentTransportState>PLAYING</CurrentTransportState></u:GetTransportInfoResponse></s:Body></s:Envelope>".toByteArray()
            exchange.sendResponseHeaders(200, reply.size.toLong())
            exchange.responseBody.use { it.write(reply) }
        }
        fake.start()

        try {
            val player = TvPlayer(TvRenderer("uuid:tv", "TV", "http://127.0.0.1:${fake.address.port}/av"))

            player.play("http://127.0.0.1/x.jpg", "x.jpg", "image/jpeg")
            assertEquals(CastState.PLAYING, player.state())
            assertNull(player.volume())

            assertEquals(
                listOf("SetAVTransportURI", "Play", "GetTransportInfo").map { "\"${AvTransport.SERVICE}#$it\"" },
                actions,
            )
            assertTrue("object.item.imageItem.photo" in bodies.first())
        } finally {
            fake.stop(0)
        }
    }

    @Test fun keepsTvsUntilTheyMissTwoSearches() {
        val living = TvRenderer("uuid:a", "Living Room", "http://10.0.0.2/av")
        val bedroom = TvRenderer("uuid:b", "bedroom", "http://10.0.0.3/av")
        val list = TvList()

        assertEquals(listOf(bedroom, living), list.update(listOf(living, bedroom)))
        assertEquals(listOf(bedroom, living), list.update(listOf(living)))
        assertEquals(listOf(bedroom, living), list.update(listOf(living, bedroom)))
        assertEquals(listOf(bedroom, living), list.update(emptyList()))
        assertEquals(emptyList<TvRenderer>(), list.update(emptyList()))
    }
}
