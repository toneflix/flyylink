package com.toneflix.flyylink

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager

class FlyyLinkApp : Application() {
    override fun onCreate() {
        super.onCreate()
        val channel = NotificationChannel(
            LinkService.CHANNEL_ID,
            getString(R.string.channel_link),
            NotificationManager.IMPORTANCE_MIN,
        )
        // We play the ringtone ourselves, at full volume, so the channel stays silent.
        val find = NotificationChannel(
            FindMyPhonePlugin.CHANNEL_ID,
            getString(R.string.channel_find),
            NotificationManager.IMPORTANCE_HIGH,
        ).apply { setSound(null, null) }

        val transfers = NotificationChannel(
            SharePlugin.CHANNEL_ID,
            getString(R.string.channel_transfers),
            NotificationManager.IMPORTANCE_DEFAULT,
        )

        // Loud enough to pop up, since the Mac is waiting for the user to allow mirroring.
        val screen = NotificationChannel(
            ScreenPlugin.CHANNEL_ID,
            getString(R.string.channel_screen),
            NotificationManager.IMPORTANCE_HIGH,
        ).apply { setSound(null, null) }

        // Pops up, since the Mac is waiting for the user to turn the hotspot on.
        val hotspot = NotificationChannel(
            Hotspot.CHANNEL_ID,
            getString(R.string.channel_hotspot),
            NotificationManager.IMPORTANCE_HIGH,
        ).apply { setSound(null, null) }

        // Pops up, since someone nearby is waiting for an answer.
        val flyyDrop = NotificationChannel(
            FlyyDropPlugin.CHANNEL_ID,
            getString(R.string.channel_flyydrop),
            NotificationManager.IMPORTANCE_HIGH,
        )

        val computers = NotificationChannel(
            ComputerNotificationPlugin.CHANNEL_ID,
            getString(R.string.channel_computer_notifications),
            NotificationManager.IMPORTANCE_DEFAULT,
        )

        getSystemService(NotificationManager::class.java).createNotificationChannels(listOf(channel, find, transfers, screen, hotspot, flyyDrop, computers))
    }
}
