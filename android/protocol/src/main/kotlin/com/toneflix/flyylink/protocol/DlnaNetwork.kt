package com.toneflix.flyylink.protocol

import java.io.BufferedOutputStream
import java.io.ByteArrayOutputStream
import java.io.Closeable
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.HttpURLConnection
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketTimeoutException
import java.net.URL
import java.net.URLEncoder
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors

/** Finds TVs on the network that play media sent to them ("Play on TV"). */
object TvFinder {
    private val targets = listOf(Ssdp.RENDERER, AvTransport.SERVICE)

    /** Searches for [timeoutMs] and returns each TV once, sorted by name. Blocks, so call it off the main thread. */
    fun search(timeoutMs: Long = 3000): List<TvRenderer> {
        val locations = LinkedHashSet<String>()

        DatagramSocket().use { socket ->
            val group = InetAddress.getByName(Ssdp.ADDRESS)
            val deadline = System.currentTimeMillis() + timeoutMs

            // Sent twice, since multicast over Wi-Fi drops packets.
            repeat(2) {
                for (target in targets) {
                    val message = Ssdp.searchMessage(target).toByteArray()
                    runCatching { socket.send(DatagramPacket(message, message.size, group, Ssdp.PORT)) }
                }
            }

            val buffer = ByteArray(4096)
            while (true) {
                val left = deadline - System.currentTimeMillis()
                if (left <= 0) break

                socket.soTimeout = left.toInt()
                val packet = DatagramPacket(buffer, buffer.size)
                try {
                    socket.receive(packet)
                } catch (_: SocketTimeoutException) {
                    break
                }

                Ssdp.parse(String(packet.data, 0, packet.length))?.let { locations += it.location }
            }
        }

        val pool = Executors.newFixedThreadPool(minOf(locations.size, 8).coerceAtLeast(1))
        val found = try {
            locations.map { location -> pool.submit<TvRenderer?> { describe(location) } }.mapNotNull { runCatching { it.get() }.getOrNull() }
        } finally {
            pool.shutdown()
        }

        return found.distinctBy { it.id }.sortedBy { it.name.lowercase() }
    }

    private fun describe(location: String): TvRenderer? = runCatching {
        val connection = URL(location).openConnection() as HttpURLConnection
        connection.connectTimeout = 2000
        connection.readTimeout = 3000

        try {
            TvRenderer.parse(connection.inputStream.readBytes().toString(Charsets.UTF_8), location)
        } finally {
            connection.disconnect()
        }
    }.getOrNull()
}

/** Plays media on one TV and controls it. Each call blocks on the network, so call them off the main thread. */
class TvPlayer(val tv: TvRenderer) {
    fun play(url: String, title: String, mime: String, size: Long? = null) {
        send(tv.avTransportUrl, AvTransport.setUri(url, title, mime, size))
        send(tv.avTransportUrl, AvTransport.play())
    }

    fun resume() {
        send(tv.avTransportUrl, AvTransport.play())
    }

    fun pause() {
        send(tv.avTransportUrl, AvTransport.pause())
    }

    fun stop() {
        send(tv.avTransportUrl, AvTransport.stop())
    }

    fun seek(positionMs: Long) {
        send(tv.avTransportUrl, AvTransport.seek(positionMs))
    }

    fun position(): CastPosition? = AvTransport.position(send(tv.avTransportUrl, AvTransport.positionInfo()))

    fun state(): CastState? = AvTransport.state(send(tv.avTransportUrl, AvTransport.transportInfo()))

    fun volume(): Int? = tv.renderingControlUrl?.let { RenderingControl.volume(send(it, RenderingControl.getVolume())) }

    fun setVolume(volume: Int) {
        tv.renderingControlUrl?.let { send(it, RenderingControl.setVolume(volume)) }
    }

    private fun send(url: String, call: SoapCall): String {
        val body = call.envelope().toByteArray()
        val connection = URL(url).openConnection() as HttpURLConnection

        try {
            connection.connectTimeout = 3000
            connection.readTimeout = 5000
            connection.requestMethod = "POST"
            connection.doOutput = true
            connection.setRequestProperty("Content-Type", "text/xml; charset=\"utf-8\"")
            connection.setRequestProperty("SOAPACTION", call.soapAction)
            connection.setFixedLengthStreamingMode(body.size)
            connection.outputStream.use { it.write(body) }

            val code = connection.responseCode
            if (code !in 200..299) throw IOException("${call.action} failed with HTTP $code")

            return connection.inputStream.readBytes().toString(Charsets.UTF_8)
        } finally {
            connection.disconnect()
        }
    }
}

/** A small HTTP server that hands files to TVs, with Range support so they can seek. */
class MediaServer : Closeable {
    /** A file to serve. [open] is called for every request, since TVs read a file several times. */
    interface Source {
        val name: String
        val mime: String
        val size: Long

        fun open(): InputStream
    }

    private val server = ServerSocket().apply {
        reuseAddress = true
        bind(InetSocketAddress(0))
    }
    private val sources = ConcurrentHashMap<String, Source>()
    private val workers = Executors.newCachedThreadPool { Thread(it, "media-server").apply { isDaemon = true } }

    val port: Int get() = server.localPort

    init {
        workers.execute(::acceptLoop)
    }

    /** Serves [source] and returns its URL as [tvHost] can reach it. */
    fun share(source: Source, tvHost: String): String {
        val token = UUID.randomUUID().toString()
        sources[token] = source

        val name = URLEncoder.encode(source.name, "UTF-8").replace("+", "%20")
        val address = localAddressFacing(tvHost).hostAddress

        return "http://$address:$port/$token/$name"
    }

    /** Stops serving everything shared so far. */
    fun clear() {
        sources.clear()
    }

    override fun close() {
        sources.clear()
        runCatching { server.close() }
        workers.shutdownNow()
    }

    private fun acceptLoop() {
        while (!server.isClosed) {
            val socket = runCatching { server.accept() }.getOrNull() ?: continue
            workers.execute { socket.use(::handle) }
        }
    }

    private fun handle(socket: Socket) {
        socket.soTimeout = 15_000
        val head = readHead(socket.getInputStream()) ?: return
        val request = HttpRequestHead.parse(head) ?: return
        val output = BufferedOutputStream(socket.getOutputStream(), 64 * 1024)
        val source = sources[request.path.trimStart('/').substringBefore('/')]

        when {
            request.method != "GET" && request.method != "HEAD" -> respond(output, "405 Method Not Allowed")
            source == null -> respond(output, "404 Not Found")
            else -> serve(output, request, source)
        }

        output.flush()
    }

    private fun serve(output: OutputStream, request: HttpRequestHead, source: Source) {
        val range = ByteRange.parse(request.headers["range"], source.size)

        if (range != null && !range.isSatisfiable) {
            respond(output, "416 Range Not Satisfiable", "Content-Range" to "bytes */${source.size}")
            return
        }

        val start = range?.start ?: 0
        val length = range?.length ?: source.size
        val headers = mutableListOf(
            "Content-Type" to source.mime,
            "Content-Length" to length.toString(),
            "Accept-Ranges" to "bytes",
            "transferMode.dlna.org" to if (CastKind.of(source.mime) == CastKind.PHOTO) "Interactive" else "Streaming",
            "contentFeatures.dlna.org" to "DLNA.ORG_OP=01;DLNA.ORG_CI=0;DLNA.ORG_FLAGS=01700000000000000000000000000000",
        )
        if (range != null) headers += "Content-Range" to "bytes ${range.start}-${range.end}/${source.size}"

        respond(output, if (range != null) "206 Partial Content" else "200 OK", *headers.toTypedArray())
        if (request.method == "HEAD") return

        runCatching {
            source.open().use { input ->
                skipFully(input, start)
                copy(input, output, length)
            }
        }
    }

    private fun respond(output: OutputStream, status: String, vararg headers: Pair<String, String>) {
        val text = buildString {
            append("HTTP/1.1 $status\r\n")
            headers.forEach { (name, value) -> append("$name: $value\r\n") }
            if (headers.none { it.first == "Content-Length" }) append("Content-Length: 0\r\n")
            append("Connection: close\r\n\r\n")
        }

        output.write(text.toByteArray())
    }

    private fun readHead(input: InputStream): String? {
        val bytes = ByteArrayOutputStream()
        var matched = 0

        while (bytes.size() < 16 * 1024) {
            val b = runCatching { input.read() }.getOrDefault(-1)
            if (b < 0) return null

            bytes.write(b)
            matched = if (b == "\r\n\r\n"[matched].code) matched + 1 else if (b == '\r'.code) 1 else 0
            if (matched == 4) return bytes.toString(Charsets.ISO_8859_1.name())
        }

        return null
    }

    private fun skipFully(input: InputStream, count: Long) {
        var left = count
        val scratch = ByteArray(64 * 1024)

        while (left > 0) {
            val skipped = input.skip(left)
            if (skipped > 0) {
                left -= skipped
                continue
            }

            val read = input.read(scratch, 0, minOf(left, scratch.size.toLong()).toInt())
            if (read < 0) throw IOException("file ended early")
            left -= read
        }
    }

    private fun copy(input: InputStream, output: OutputStream, count: Long) {
        var left = count
        val buffer = ByteArray(64 * 1024)

        while (left > 0) {
            val read = input.read(buffer, 0, minOf(left, buffer.size.toLong()).toInt())
            if (read < 0) break

            output.write(buffer, 0, read)
            left -= read
        }
    }

    companion object {
        /** The local address on the network that leads to [host]. No packet is sent. */
        fun localAddressFacing(host: String): InetAddress = DatagramSocket().use { socket ->
            socket.connect(InetAddress.getByName(host), Ssdp.PORT)
            socket.localAddress
        }
    }
}

/** The TVs seen lately. A TV stays listed until it misses [misses] searches in a row, since multicast answers get lost. */
class TvList(private val misses: Int = 2) {
    private val known = LinkedHashMap<String, TvRenderer>()
    private val missed = HashMap<String, Int>()

    val tvs: List<TvRenderer> get() = known.values.sortedBy { it.name.lowercase() }

    /** Takes one search's results and returns the TVs to show. */
    @Synchronized
    fun update(found: List<TvRenderer>): List<TvRenderer> {
        val ids = found.map { it.id }.toSet()

        for (id in known.keys.toList() - ids) {
            val count = (missed[id] ?: 0) + 1
            if (count >= misses) {
                known.remove(id)
                missed.remove(id)
            } else {
                missed[id] = count
            }
        }

        found.forEach {
            known[it.id] = it
            missed.remove(it.id)
        }

        return tvs
    }
}
