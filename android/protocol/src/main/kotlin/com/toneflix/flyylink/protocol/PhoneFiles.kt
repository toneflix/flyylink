package com.toneflix.flyylink.protocol

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.longOrNull
import kotlinx.serialization.json.put

/** A file or folder in the phone's storage. [path] starts at the storage root, like "/DCIM/Camera/IMG_1.jpg"; [modified] is in milliseconds. */
data class PhoneFile(
    val path: String,
    val isFolder: Boolean,
    val size: Long = 0,
    val modified: Long = 0,
) {
    val name: String get() = PhoneFiles.name(path)

    fun toJson(): JsonObject = buildJsonObject {
        put("path", path)
        put("folder", isFolder)
        put("size", size)
        put("modified", modified)
    }

    companion object {
        fun fromJson(json: JsonObject): PhoneFile? {
            fun primitive(key: String) = json[key] as? JsonPrimitive
            val path = primitive("path")?.takeIf { it.isString }?.content?.let(PhoneFiles::clean) ?: return null

            return PhoneFile(
                path = path,
                isFolder = primitive("folder")?.booleanOrNull ?: false,
                size = primitive("size")?.longOrNull ?: 0,
                modified = primitive("modified")?.longOrNull ?: 0,
            )
        }
    }
}

/**
 * Browsing and changing the phone's storage from a computer. The computer sends [PacketType.FILES_REQUEST] with an
 * [Action], and the phone answers each with [PacketType.FILES] carrying the same request id. Uploads carry the file as
 * the request's payload; downloads come back as the answer's payload.
 */
object PhoneFiles {
    enum class Action(val wire: String) {
        LIST("list"),
        DOWNLOAD("download"),
        UPLOAD("upload"),
        FOLDER("folder"),
        MOVE("move"),
        DELETE("delete");

        companion object {
            fun of(wire: String?): Action? = entries.firstOrNull { it.wire == wire }
        }
    }

    /** Why a request failed. */
    enum class Failure(val wire: String) {
        /** The phone hasn't allowed computers to browse its files. */
        DENIED("denied"),
        MISSING("missing"),
        EXISTS("exists"),
        INVALID("invalid"),
        FAILED("failed");

        companion object {
            fun of(wire: String?): Failure = entries.firstOrNull { it.wire == wire } ?: FAILED
        }
    }

    /** [to] is where [Action.MOVE] puts the item. */
    data class Request(val id: Long, val action: Action, val path: String, val to: String? = null)

    /** [files] is a folder's contents for [Action.LIST]; [file] is the item a request made, moved or sent. */
    data class Answer(
        val id: Long,
        val failure: Failure? = null,
        val files: List<PhoneFile> = emptyList(),
        val file: PhoneFile? = null,
    ) {
        val ok: Boolean get() = failure == null
    }

    const val ROOT = "/"

    /** [path] with no empty or "." parts, starting with "/", or null when it would climb out of the root. */
    fun clean(path: String): String? {
        if ('\u0000' in path) return null

        val parts = path.split('/').filter { it.isNotEmpty() && it != "." }
        if (parts.any { it == ".." }) return null

        return ROOT + parts.joinToString("/")
    }

    fun isValidName(name: String): Boolean =
        name.isNotEmpty() && name != "." && name != ".." && '/' !in name && '\u0000' !in name

    fun join(folder: String, name: String): String = if (folder == ROOT) "$ROOT$name" else "$folder/$name"

    fun name(path: String): String = path.substringAfterLast('/')

    fun parent(path: String): String = path.substringBeforeLast('/', "").ifEmpty { ROOT }

    fun request(request: Request): NetworkPacket = NetworkPacket(
        PacketType.FILES_REQUEST,
        NetworkPacket.body("id" to request.id, "action" to request.action.wire, "path" to request.path, "to" to request.to),
    )

    fun request(packet: NetworkPacket): Request? {
        if (packet.type != PacketType.FILES_REQUEST) return null

        val id = packet.long("id") ?: return null
        val action = Action.of(packet.string("action")) ?: return null
        val path = packet.string("path")?.let(::clean) ?: return null
        val to = packet.string("to")?.let { clean(it) ?: return null }
        if (action == Action.MOVE && (to == null || to == ROOT)) return null

        return Request(id, action, path, to)
    }

    fun answer(answer: Answer): NetworkPacket = NetworkPacket(
        PacketType.FILES,
        buildJsonObject {
            put("id", answer.id)
            put("ok", answer.ok)
            answer.failure?.let { put("error", it.wire) }
            if (answer.files.isNotEmpty()) put("files", JsonArray(answer.files.map { it.toJson() }))
            answer.file?.let { put("file", it.toJson()) }
        },
    )

    fun answer(packet: NetworkPacket): Answer? {
        if (packet.type != PacketType.FILES) return null

        val id = packet.long("id") ?: return null
        val ok = packet.bool("ok") ?: false
        val files = (packet.body["files"] as? JsonArray).orEmpty().mapNotNull { (it as? JsonObject)?.let(PhoneFile::fromJson) }

        return Answer(
            id = id,
            failure = if (ok) null else Failure.of(packet.string("error")),
            files = files,
            file = (packet.body["file"] as? JsonObject)?.let(PhoneFile::fromJson),
        )
    }
}
