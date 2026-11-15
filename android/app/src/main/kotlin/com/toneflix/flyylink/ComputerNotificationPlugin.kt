package com.toneflix.flyylink

import android.app.Notification
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.graphics.BitmapFactory
import android.graphics.drawable.Icon
import com.toneflix.flyylink.protocol.Device
import com.toneflix.flyylink.protocol.NetworkPacket
import com.toneflix.flyylink.protocol.NotificationRequests
import com.toneflix.flyylink.protocol.PacketType
import com.toneflix.flyylink.protocol.PhoneNotification
import com.toneflix.flyylink.protocol.Plugin
import java.io.File
import java.util.concurrent.Executors

/** Shows paired computers' notifications on the phone. Clearing one here clears it on the computer when it can. */
object ComputerNotificationPlugin : Plugin {
    const val CHANNEL_ID = "computer_notifications"

    private const val ACTION_CLEARED = "com.toneflix.flyylink.COMPUTER_NOTIFICATION_CLEARED"
    private const val EXTRA_DEVICE = "device"
    private const val EXTRA_ID = "id"
    private const val NOTIFICATION_ID = 1

    override val incoming = setOf(PacketType.NOTIFICATION)

    private var context: Context? = null

    /** Icons download over their own connection, so they stay off the link's thread. */
    private val downloads = Executors.newSingleThreadExecutor()

    fun start(context: Context) {
        this.context = context.applicationContext
    }

    override fun onPacket(device: Device, packet: NetworkPacket) {
        val context = context ?: return
        if (!device.link.identity.deviceType.isComputer) return

        PhoneNotification.cancelledId(packet)?.let {
            manager(context).cancel(tag(device.id, it), NOTIFICATION_ID)
            return
        }

        val note = PhoneNotification.fromPacket(packet) ?: return
        if (note.silent || (note.title.isBlank() && note.text.isBlank())) return

        val hash = note.iconHash
        if (hash == null || packet.payloadSize == null) {
            show(context, device, note, hash?.let { cachedIcon(context, it) })
            return
        }

        downloads.execute { show(context, device, note, downloadIcon(context, device, packet, hash)) }
    }

    private fun show(context: Context, device: Device, note: PhoneNotification, icon: File?) {
        val title = note.title.ifBlank { note.appName }
        val text = if (note.title.isBlank()) "" else note.text

        val builder = Notification.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.drawable.lucide_laptop)
            .setContentTitle(title)
            .setContentText(text)
            .setStyle(Notification.BigTextStyle().bigText(text))
            .setSubText(context.getString(R.string.computer_notification_from, note.appName, device.name))
            .setContentIntent(MainActivity.pendingIntent(context))
            .setDeleteIntent(cleared(context, device.id, note.id))
            .setAutoCancel(true)
            .setGroup(device.id)

        if (note.time > 0) builder.setWhen(note.time).setShowWhen(true)
        icon?.let { file -> BitmapFactory.decodeFile(file.path)?.let { builder.setLargeIcon(Icon.createWithBitmap(it)) } }

        runCatching { manager(context).notify(tag(device.id, note.id), NOTIFICATION_ID, builder.build()) }
    }

    private fun cachedIcon(context: Context, hash: String): File? = iconFile(context, hash).takeIf { it.exists() }

    private fun downloadIcon(context: Context, device: Device, packet: NetworkPacket, hash: String): File? {
        val target = iconFile(context, hash)
        target.parentFile?.mkdirs()
        val partial = File(target.path + ".part")
        val received = runCatching { partial.outputStream().use { device.receivePayload(packet, it) } }.isSuccess
        if (!received || !partial.renameTo(target)) {
            partial.delete()
            return null
        }

        return target
    }

    private fun iconFile(context: Context, hash: String): File =
        File(File(context.cacheDir, "computer-icons"), hash.filter { it.isLetterOrDigit() } + ".png")

    private fun cleared(context: Context, deviceId: String, id: String): PendingIntent {
        val intent = Intent(context, ClearedReceiver::class.java)
            .setAction(ACTION_CLEARED)
            .putExtra(EXTRA_DEVICE, deviceId)
            .putExtra(EXTRA_ID, id)

        return PendingIntent.getBroadcast(context, tag(deviceId, id).hashCode(), intent, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
    }

    private fun tag(deviceId: String, id: String) = "computer/$deviceId/$id"

    private fun manager(context: Context) = context.getSystemService(NotificationManager::class.java)

    /** Tells the computer the user cleared its notification on the phone. */
    class ClearedReceiver : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            val deviceId = intent.getStringExtra(EXTRA_DEVICE) ?: return
            val id = intent.getStringExtra(EXTRA_ID) ?: return
            val device = DeviceStore.find(deviceId) ?: return

            DeviceStore.send(device, NotificationRequests.dismiss(id))
        }
    }
}
