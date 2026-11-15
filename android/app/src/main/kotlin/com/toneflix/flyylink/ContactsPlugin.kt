package com.toneflix.flyylink

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.provider.ContactsContract
import com.toneflix.flyylink.protocol.Device
import com.toneflix.flyylink.protocol.NetworkPacket
import com.toneflix.flyylink.protocol.PacketType
import com.toneflix.flyylink.protocol.PhoneContact
import com.toneflix.flyylink.protocol.PhoneContacts
import com.toneflix.flyylink.protocol.Plugin
import java.util.concurrent.Executors

/** Sends the phone's contacts with their numbers, so a computer can search them when texting or calling. */
object ContactsPlugin : Plugin {
    override val incoming = setOf(PacketType.CONTACTS_REQUEST)

    private var context: Context? = null

    private val worker = Executors.newSingleThreadExecutor()

    fun start(context: Context) {
        this.context = context.applicationContext
    }

    override fun onPacket(device: Device, packet: NetworkPacket) {
        if (!PhoneContacts.isRequest(packet)) return
        val context = context ?: return
        if (context.checkSelfPermission(Manifest.permission.READ_CONTACTS) != PackageManager.PERMISSION_GRANTED) return

        worker.execute { DeviceStore.send(device, PhoneContacts.packet(read(context))) }
    }

    private fun read(context: Context): List<PhoneContact> {
        val columns = arrayOf(
            ContactsContract.CommonDataKinds.Phone.CONTACT_ID,
            ContactsContract.CommonDataKinds.Phone.DISPLAY_NAME,
            ContactsContract.CommonDataKinds.Phone.NUMBER,
        )
        val people = linkedMapOf<Long, Pair<String, MutableList<String>>>()

        runCatching {
            context.contentResolver.query(
                ContactsContract.CommonDataKinds.Phone.CONTENT_URI,
                columns,
                null,
                null,
                "${ContactsContract.CommonDataKinds.Phone.DISPLAY_NAME} COLLATE NOCASE ASC",
            )?.use { cursor ->
                while (cursor.moveToNext()) {
                    val name = cursor.getString(1)?.trim().orEmpty()
                    val number = cursor.getString(2)?.trim().orEmpty()
                    if (name.isEmpty() || number.isEmpty()) continue

                    val numbers = people.getOrPut(cursor.getLong(0)) { name to mutableListOf() }.second
                    if (numbers.none { PhoneContacts.digits(it) == PhoneContacts.digits(number) }) numbers += number
                }
            }
        }

        return people.values.map { (name, numbers) -> PhoneContact(name, numbers) }
    }
}
