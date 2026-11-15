package com.toneflix.flyylink.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** ConnectivityTests.swift and ConnectivityTests.cs check the same. */
class ConnectivityTest {
    @Test fun readsKdeConnectivityReport() {
        val packet = NetworkPacket.parse("""{"id":1,"type":"kdeconnect.connectivity_report","body":{"signalStrengths":{"6":{"networkType":"LTE","signalStrength":3},"7":{"networkType":"GSM","signalStrength":9}}}}""")

        val report = ConnectivityReport.fromPacket(packet)!!

        assertEquals(CellSignal("LTE", 3), report.signals["6"])
        assertEquals(CellSignal("GSM", 4), report.signals["7"])
        assertEquals(CellSignal("GSM", 4), report.best)
    }

    @Test fun roundTripsAndPicksTheStrongestSim() {
        val report = ConnectivityReport(mapOf("1" to CellSignal(CellSignal.NR, 2), "2" to CellSignal(CellSignal.LTE, 2)))

        val back = ConnectivityReport.fromPacket(NetworkPacket.parse(report.toPacket().serialize()))!!

        assertEquals(report, back)
        assertEquals(CellSignal(CellSignal.NR, 2), back.best)
    }

    @Test fun emptyReportHasNoSignal() {
        val report = ConnectivityReport.fromPacket(NetworkPacket(PacketType.CONNECTIVITY_REPORT))!!

        assertNull(report.best)
        assertNull(ConnectivityReport.fromPacket(ConnectivityReport.request()))
    }

    @Test fun namesGenerations() {
        assertEquals("5G", CellSignal(CellSignal.NR, 4).generation)
        assertEquals("4G", CellSignal(CellSignal.LTE, 4).generation)
        assertEquals("3G", CellSignal(CellSignal.UMTS, 4).generation)
        assertEquals("E", CellSignal(CellSignal.EDGE, 4).generation)
        assertEquals("2G", CellSignal(CellSignal.GSM, 4).generation)
        assertEquals("", CellSignal(CellSignal.UNKNOWN, 0).generation)
    }
}
