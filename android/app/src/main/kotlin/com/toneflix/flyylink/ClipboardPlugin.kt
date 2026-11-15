package com.toneflix.flyylink

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.net.Uri
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.provider.OpenableColumns
import androidx.core.content.FileProvider
import com.toneflix.flyylink.protocol.Clipboard
import com.toneflix.flyylink.protocol.ClipboardFile
import com.toneflix.flyylink.protocol.Device
import com.toneflix.flyylink.protocol.NetworkPacket
import com.toneflix.flyylink.protocol.PacketType
import com.toneflix.flyylink.protocol.Plugin
import java.io.File
import java.io.FileNotFoundException
import java.util.UUID
import java.util.concurrent.Executors

/**
 * Puts what was copied on a computer, text, pictures or files, on the phone's clipboard. Since Android 10
 * only the app in front can read the clipboard, so sending the other way goes through [ClipboardActivity]
 * and [CopyToComputerActivity]. Older phones send every copy as it happens.
 */
object ClipboardPlugin : Plugin {
    override val incoming = setOf(PacketType.CLIPBOARD, PacketType.CLIPBOARD_CONNECT, PacketType.CLIPBOARD_FILE)

    private var context: Context? = null
    private val main = Handler(Looper.getMainLooper())
    private val transfers = Executors.newCachedThreadPool()

    /** The last text a computer put on this clipboard, which must not be sent back. */
    @Volatile private var received: String? = null

    fun start(context: Context) {
        if (this.context != null) return

        val app = context.applicationContext
        this.context = app
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) main.post { watch(app) }
    }

    private fun watch(context: Context) {
        clipboard(context).addPrimaryClipChangedListener {
            val text = clipboard(context).primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(context)?.toString()
            if (text.isNullOrEmpty() || text == received) return@addPrimaryClipChangedListener

            sendText(text)
        }
    }

    override fun onPacket(device: Device, packet: NetworkPacket) {
        val context = context ?: return

        Clipboard.file(packet)?.let { file ->
            transfers.execute { receive(context, device, packet, file) }
            return
        }

        val content = Clipboard.content(packet) ?: return
        received = content
        main.post { clipboard(context).setPrimaryClip(ClipData.newPlainText(device.name, content)) }
    }

    /** Sends the phone's clipboard to every paired computer. Call while an activity has focus. */
    fun send(context: Context): Boolean {
        val clip = clipboard(context).primaryClip?.takeIf { it.itemCount > 0 } ?: return false
        val item = clip.getItemAt(0)

        item.uri?.let { uri -> return sendFile(context, uri) }

        val text = item.coerceToText(context)?.toString()
        if (text.isNullOrEmpty()) return false

        sendText(text)
        return true
    }

    /** Sends text to every paired computer and returns how many got it. */
    fun sendText(text: String): Int {
        val packet = Clipboard.packet(text)
        val targets = DeviceStore.pairedDevices()

        targets.forEach { DeviceStore.send(it, packet) }
        return targets.size
    }

    private fun sendFile(context: Context, uri: Uri): Boolean {
        val resolver = context.contentResolver
        val (name, size) = resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)
            ?.use { cursor -> if (cursor.moveToFirst()) cursor.getString(0) to cursor.getLong(1) else null }
            ?: return false
        if (size <= 0 || size > Clipboard.FILE_LIMIT) return false

        val packet = Clipboard.filePacket(name ?: "Copied file", resolver.getType(uri) ?: "application/octet-stream")
        val targets = DeviceStore.pairedDevices()
        transfers.execute {
            targets.forEach { device ->
                runCatching {
                    device.sendWithPayload(packet, size, open = { resolver.openInputStream(uri) ?: throw FileNotFoundException(uri.toString()) })
                }
            }
        }

        return targets.isNotEmpty()
    }

    /** Saves the file in our cache and puts a link to it on the clipboard, which apps can paste. */
    private fun receive(context: Context, device: Device, packet: NetworkPacket, file: ClipboardFile) {
        val folder = File(context.cacheDir, "clipboard").apply { deleteRecursively() }
        val target = File(File(folder, UUID.randomUUID().toString()).apply { mkdirs() }, file.safeName)

        val received = runCatching { target.outputStream().use { device.receivePayload(packet, it) } }.isSuccess
        if (!received) {
            target.delete()
            return
        }

        val uri = FileProvider.getUriForFile(context, "${context.packageName}.files", target)
        main.post {
            val clip = ClipData(device.name, arrayOf(file.mimeType), ClipData.Item(uri))
            clipboard(context).setPrimaryClip(clip)
        }
    }

    private fun clipboard(context: Context) = context.getSystemService(ClipboardManager::class.java)
}
