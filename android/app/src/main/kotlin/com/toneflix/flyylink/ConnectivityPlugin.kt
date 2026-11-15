package com.toneflix.flyylink

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.telephony.CellSignalStrength
import android.telephony.CellSignalStrengthCdma
import android.telephony.CellSignalStrengthGsm
import android.telephony.CellSignalStrengthLte
import android.telephony.CellSignalStrengthNr
import android.telephony.CellSignalStrengthTdscdma
import android.telephony.CellSignalStrengthWcdma
import android.telephony.PhoneStateListener
import android.telephony.SignalStrength
import android.telephony.SubscriptionManager
import android.telephony.TelephonyCallback
import android.telephony.TelephonyManager
import androidx.annotation.RequiresApi
import com.toneflix.flyylink.protocol.CellSignal
import com.toneflix.flyylink.protocol.ConnectivityReport
import com.toneflix.flyylink.protocol.Device
import com.toneflix.flyylink.protocol.NetworkPacket
import com.toneflix.flyylink.protocol.PacketType
import com.toneflix.flyylink.protocol.Plugin

/** Sends the mobile signal of the SIM used for data to paired devices whenever its bars or network type change. */
object ConnectivityPlugin : Plugin {
    override val incoming = setOf(PacketType.CONNECTIVITY_REPORT_REQUEST)

    @Volatile private var current: ConnectivityReport? = null
    private var telephony: TelephonyManager? = null
    private var subscription = SubscriptionManager.INVALID_SUBSCRIPTION_ID
    private var watcher: Watcher? = null
    private var appContext: Context? = null

    /** Call on the main thread. Does nothing on devices without mobile data. */
    fun start(context: Context) {
        if (!context.packageManager.hasSystemFeature(PackageManager.FEATURE_TELEPHONY)) return
        val manager = context.getSystemService(TelephonyManager::class.java) ?: return

        subscription = SubscriptionManager.getDefaultDataSubscriptionId()
        val forData = if (subscription == SubscriptionManager.INVALID_SUBSCRIPTION_ID) manager else manager.createForSubscriptionId(subscription)
        appContext = context.applicationContext
        telephony = forData

        watcher = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) CallbackWatcher(context, forData) else ListenerWatcher(forData)
        watcher?.start()
    }

    fun stop() {
        watcher?.stop()
        watcher = null
        telephony = null
        current = null
    }

    override fun onPaired(device: Device) {
        current?.let { DeviceStore.send(device, it.toPacket()) }
    }

    override fun onPacket(device: Device, packet: NetworkPacket) = onPaired(device)

    private fun changed(signal: SignalStrength) {
        val manager = telephony ?: return

        val report = report(manager, signal)
        if (report == current) return

        current = report
        DeviceStore.sendToPaired(report.toPacket())
    }

    private fun report(manager: TelephonyManager, signal: SignalStrength): ConnectivityReport {
        if (manager.simState != TelephonyManager.SIM_STATE_READY) return ConnectivityReport(emptyMap())

        val cells = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) signal.cellSignalStrengths else emptyList()
        val type = typeOf(manager, cells)
        val strength = signal.level.coerceIn(0, CellSignal.MAX_STRENGTH)
        val key = subscription.takeIf { it != SubscriptionManager.INVALID_SUBSCRIPTION_ID }?.toString() ?: "0"

        return ConnectivityReport(mapOf(key to CellSignal(type, strength)))
    }

    /** 5G shows as soon as the phone has a 5G cell, as the status bar does. Otherwise the data network names it. */
    private fun typeOf(manager: TelephonyManager, cells: List<CellSignalStrength>): String {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q && cells.any { it is CellSignalStrengthNr }) return CellSignal.NR

        return dataType(manager) ?: cells.firstNotNullOfOrNull { kindOf(it) } ?: CellSignal.UNKNOWN
    }

    /** The data network's name, when the phone permission is granted. */
    private fun dataType(manager: TelephonyManager): String? {
        val context = appContext ?: return null
        if (context.checkSelfPermission(Manifest.permission.READ_PHONE_STATE) != PackageManager.PERMISSION_GRANTED) return null

        @Suppress("DEPRECATION")
        val network = runCatching { if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) manager.dataNetworkType else manager.networkType }.getOrNull()

        return when (network) {
            TelephonyManager.NETWORK_TYPE_NR -> CellSignal.NR
            TelephonyManager.NETWORK_TYPE_LTE, TelephonyManager.NETWORK_TYPE_IWLAN -> CellSignal.LTE
            TelephonyManager.NETWORK_TYPE_HSPA, TelephonyManager.NETWORK_TYPE_HSPAP,
            TelephonyManager.NETWORK_TYPE_HSDPA, TelephonyManager.NETWORK_TYPE_HSUPA -> CellSignal.HSPA
            TelephonyManager.NETWORK_TYPE_UMTS, TelephonyManager.NETWORK_TYPE_TD_SCDMA -> CellSignal.UMTS
            TelephonyManager.NETWORK_TYPE_EVDO_0, TelephonyManager.NETWORK_TYPE_EVDO_A,
            TelephonyManager.NETWORK_TYPE_EVDO_B, TelephonyManager.NETWORK_TYPE_EHRPD -> CellSignal.CDMA2000
            TelephonyManager.NETWORK_TYPE_EDGE -> CellSignal.EDGE
            TelephonyManager.NETWORK_TYPE_GPRS -> CellSignal.GPRS
            TelephonyManager.NETWORK_TYPE_GSM -> CellSignal.GSM
            TelephonyManager.NETWORK_TYPE_CDMA, TelephonyManager.NETWORK_TYPE_1xRTT -> CellSignal.CDMA
            TelephonyManager.NETWORK_TYPE_IDEN -> CellSignal.IDEN
            else -> null
        }
    }

    /** The cell's own kind, for when the data network can't be read. */
    private fun kindOf(cell: CellSignalStrength): String? {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) return null

        return when (cell) {
            is CellSignalStrengthNr -> CellSignal.NR
            is CellSignalStrengthLte -> CellSignal.LTE
            is CellSignalStrengthWcdma, is CellSignalStrengthTdscdma -> CellSignal.UMTS
            is CellSignalStrengthCdma -> CellSignal.CDMA
            is CellSignalStrengthGsm -> CellSignal.GSM
            else -> null
        }
    }

    private interface Watcher {
        fun start()
        fun stop()
    }

    @RequiresApi(Build.VERSION_CODES.S)
    private class CallbackWatcher(private val context: Context, private val manager: TelephonyManager) : Watcher {
        private val callback = object : TelephonyCallback(), TelephonyCallback.SignalStrengthsListener {
            override fun onSignalStrengthsChanged(signalStrength: SignalStrength) = changed(signalStrength)
        }

        override fun start() {
            runCatching { manager.registerTelephonyCallback(context.mainExecutor, callback) }
        }

        override fun stop() {
            runCatching { manager.unregisterTelephonyCallback(callback) }
        }
    }

    @Suppress("DEPRECATION")
    private class ListenerWatcher(private val manager: TelephonyManager) : Watcher {
        private val listener = object : PhoneStateListener() {
            override fun onSignalStrengthsChanged(signalStrength: SignalStrength) = changed(signalStrength)
        }

        override fun start() {
            runCatching { manager.listen(listener, PhoneStateListener.LISTEN_SIGNAL_STRENGTHS) }
        }

        override fun stop() {
            runCatching { manager.listen(listener, PhoneStateListener.LISTEN_NONE) }
        }
    }
}
