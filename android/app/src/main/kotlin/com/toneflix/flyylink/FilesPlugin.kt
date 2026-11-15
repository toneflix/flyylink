package com.toneflix.flyylink

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.media.MediaScannerConnection
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.Settings
import com.toneflix.flyylink.protocol.Device
import com.toneflix.flyylink.protocol.NetworkPacket
import com.toneflix.flyylink.protocol.PacketType
import com.toneflix.flyylink.protocol.PhoneFile
import com.toneflix.flyylink.protocol.PhoneFiles
import com.toneflix.flyylink.protocol.Plugin
import java.io.File
import java.io.FileInputStream
import java.util.concurrent.Executors

/** Lets paired computers browse the phone's storage and send, fetch, rename, move and delete files in it. */
object FilesPlugin : Plugin {
    override val incoming = setOf(PacketType.FILES_REQUEST)

    private var context: Context? = null

    /** A few at a time, so a long upload doesn't hold up opening folders. */
    private val worker = Executors.newFixedThreadPool(3)

    /** Below Android 11 these cover all of shared storage; from 11 on it takes "All files access". */
    val legacyPermissions = arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE, Manifest.permission.WRITE_EXTERNAL_STORAGE)

    val needsSettings: Boolean get() = Build.VERSION.SDK_INT >= Build.VERSION_CODES.R

    fun hasAccess(context: Context): Boolean = if (needsSettings) {
        Environment.isExternalStorageManager()
    } else {
        legacyPermissions.all { context.checkSelfPermission(it) == PackageManager.PERMISSION_GRANTED }
    }

    /** Android's "All files access" switch for FlyyLink. */
    fun accessSettingsIntent(context: Context): Intent =
        Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION, Uri.parse("package:${context.packageName}"))

    fun start(context: Context) {
        this.context = context.applicationContext
    }

    override fun onPacket(device: Device, packet: NetworkPacket) {
        val request = PhoneFiles.request(packet) ?: return
        val context = context ?: return

        worker.execute {
            val answer = if (hasAccess(context)) {
                runCatching { Storage(context).handle(device, request, packet) }
                    .getOrElse { PhoneFiles.Answer(request.id, PhoneFiles.Failure.FAILED) }
            } else {
                PhoneFiles.Answer(request.id, PhoneFiles.Failure.DENIED)
            }

            answer?.let { DeviceStore.send(device, PhoneFiles.answer(it)) }
        }
    }

    /** Carries out one request in the phone's main shared storage. */
    private class Storage(private val context: Context) {
        private val root: File = Environment.getExternalStorageDirectory()

        /** The answer to send, or null when it goes out with a payload instead. */
        fun handle(device: Device, request: PhoneFiles.Request, packet: NetworkPacket): PhoneFiles.Answer? {
            val file = locate(request.path)

            return when (request.action) {
                PhoneFiles.Action.LIST -> list(request, file)
                PhoneFiles.Action.DOWNLOAD -> download(device, request, file)
                PhoneFiles.Action.UPLOAD -> upload(device, request, packet, file)
                PhoneFiles.Action.FOLDER -> folder(request, file)
                PhoneFiles.Action.MOVE -> move(request, file, locate(request.to!!))
                PhoneFiles.Action.DELETE -> delete(request, file)
            }
        }

        private fun locate(path: String): File = if (path == PhoneFiles.ROOT) root else File(root, path.removePrefix("/"))

        private fun pathOf(file: File): String = PhoneFiles.clean(file.absolutePath.removePrefix(root.absolutePath)) ?: PhoneFiles.ROOT

        private fun describe(file: File) = PhoneFile(
            path = pathOf(file),
            isFolder = file.isDirectory,
            size = if (file.isDirectory) 0 else file.length(),
            modified = file.lastModified(),
        )

        private fun list(request: PhoneFiles.Request, folder: File): PhoneFiles.Answer {
            if (!folder.isDirectory) return failure(request, PhoneFiles.Failure.MISSING)

            val files = folder.listFiles().orEmpty().map(::describe)
            return PhoneFiles.Answer(request.id, files = files, file = describe(folder))
        }

        private fun download(device: Device, request: PhoneFiles.Request, file: File): PhoneFiles.Answer? {
            if (!file.isFile) return failure(request, PhoneFiles.Failure.MISSING)

            val answer = PhoneFiles.Answer(request.id, file = describe(file))
            DeviceStore.offer(device, PhoneFiles.answer(answer), file.length(), open = { FileInputStream(file) }, onDone = { error ->
                if (error is DeviceStore.PayloadNotOffered) DeviceStore.send(device, PhoneFiles.answer(failure(request, PhoneFiles.Failure.FAILED)))
            })

            return null
        }

        /** Saves next to the target first, so a broken transfer never leaves half a file in its place. */
        private fun upload(device: Device, request: PhoneFiles.Request, packet: NetworkPacket, file: File): PhoneFiles.Answer {
            val folder = file.parentFile
            if (packet.payloadSize == null || file == root) return failure(request, PhoneFiles.Failure.INVALID)
            if (folder == null || !folder.isDirectory) return failure(request, PhoneFiles.Failure.MISSING)
            if (file.isDirectory) return failure(request, PhoneFiles.Failure.EXISTS)

            val partial = File(folder, ".${file.name}.flyylink-${request.id}")
            try {
                partial.outputStream().use { device.receivePayload(packet, it) }
                if (file.exists()) file.delete()
                if (!partial.renameTo(file)) return failure(request, PhoneFiles.Failure.FAILED)
            } finally {
                partial.delete()
            }

            scan(file)
            return PhoneFiles.Answer(request.id, file = describe(file))
        }

        private fun folder(request: PhoneFiles.Request, folder: File): PhoneFiles.Answer {
            if (folder.exists()) return failure(request, PhoneFiles.Failure.EXISTS)
            if (folder.parentFile?.isDirectory != true) return failure(request, PhoneFiles.Failure.MISSING)
            if (!folder.mkdir()) return failure(request, PhoneFiles.Failure.FAILED)

            return PhoneFiles.Answer(request.id, file = describe(folder))
        }

        private fun move(request: PhoneFiles.Request, from: File, to: File): PhoneFiles.Answer {
            if (from == root) return failure(request, PhoneFiles.Failure.INVALID)
            if (!from.exists()) return failure(request, PhoneFiles.Failure.MISSING)
            if (to.exists()) return failure(request, PhoneFiles.Failure.EXISTS)
            if (to.parentFile?.isDirectory != true) return failure(request, PhoneFiles.Failure.MISSING)
            if (!from.renameTo(to)) return failure(request, PhoneFiles.Failure.FAILED)

            scan(from, to)
            return PhoneFiles.Answer(request.id, file = describe(to))
        }

        private fun delete(request: PhoneFiles.Request, file: File): PhoneFiles.Answer {
            if (file == root) return failure(request, PhoneFiles.Failure.INVALID)
            if (file.exists() && !file.deleteRecursively()) return failure(request, PhoneFiles.Failure.FAILED)

            scan(file)
            return PhoneFiles.Answer(request.id)
        }

        private fun failure(request: PhoneFiles.Request, failure: PhoneFiles.Failure) = PhoneFiles.Answer(request.id, failure)

        /** Tells the gallery and music apps what changed. */
        private fun scan(vararg files: File) {
            MediaScannerConnection.scanFile(context, files.map { it.absolutePath }.toTypedArray(), null, null)
        }
    }
}
