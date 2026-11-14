package com.rigsight.app.net

import com.rigsight.app.protocol.DEFAULT_WS_PORT
import com.rigsight.app.protocol.SessionInfo
import com.rigsight.app.protocol.SessionSummary
import com.rigsight.app.protocol.TelemetrySample

enum class LinkPhase { Disconnected, Connecting, Connected, PairingRequired }

enum class LinkTransport { None, Wifi, Bluetooth }

data class ConnectionUiState(
    val phase: LinkPhase = LinkPhase.Disconnected,
    val transport: LinkTransport = LinkTransport.None,
    val wifiConnected: Boolean = false,
    val bluetoothConnected: Boolean = false,
    val pcName: String? = null,
    val pcId: String? = null,
    val hosts: List<String> = emptyList(),
    val port: Int = DEFAULT_WS_PORT,
    val lastError: String? = null,
    val pairingInProgress: Boolean = false,
)

data class ActiveSessionInfo(
    val id: String,
    val gameId: String,
    val gameName: String,
    val exeName: String,
    val startedAt: String,
    val sampleIntervalMs: Int,
) {
    companion object {
        fun from(session: SessionInfo) = ActiveSessionInfo(
            id = session.id,
            gameId = session.gameId,
            gameName = session.gameName,
            exeName = session.exeName,
            startedAt = session.startedAt,
            sampleIntervalMs = session.sampleIntervalMs,
        )
    }
}

data class BleDeviceInfo(
    val name: String,
    val address: String,
    val rssi: Int,
)

data class SessionDetail(
    val summary: SessionSummary,
    val samples: List<TelemetrySample>,
)
