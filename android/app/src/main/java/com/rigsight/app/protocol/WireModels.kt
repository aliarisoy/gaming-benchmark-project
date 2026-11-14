package com.rigsight.app.protocol

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

const val PROTOCOL_VERSION = 1
const val APP_VERSION = "0.1.0"
const val DEFAULT_WS_PORT = 47823

/** Shared JSON settings; absent optional fields stay absent on the wire. */
val RigJson: Json = Json {
    ignoreUnknownKeys = true
    explicitNulls = false
    encodeDefaults = false
    isLenient = true
}

@Serializable
data class GameDto(
    val id: String = "",
    val name: String = "",
    val exeName: String = "",
)

@Serializable
data class PromptInfo(
    val id: String = "",
    val gameId: String = "",
    val gameName: String = "",
    val exeName: String = "",
    val processId: Int = 0,
    val expiresAt: Long = 0,
)

@Serializable
data class SessionInfo(
    val id: String = "",
    val gameId: String = "",
    val gameName: String = "",
    val exeName: String = "",
    val startedAt: String = "",
    val sampleIntervalMs: Int = 500,
)

@Serializable
data class TelemetrySample(
    @SerialName("t") val t: Long = 0,
    val fps: Double? = null,
    val frameTimeMs: Double? = null,
    val low1Fps: Double? = null,
    val low01Fps: Double? = null,
    val cpuLoadPct: Double? = null,
    val cpuTempC: Double? = null,
    val cpuPowerW: Double? = null,
    val cpuClockMhz: Double? = null,
    val gpuLoadPct: Double? = null,
    val gpuTempC: Double? = null,
    val gpuHotspotC: Double? = null,
    val gpuPowerW: Double? = null,
    val gpuClockMhz: Double? = null,
    val vramUsedMb: Double? = null,
    val vramTotalMb: Double? = null,
    val ramUsedMb: Double? = null,
    val ramTotalMb: Double? = null,
)

@Serializable
data class SessionSummary(
    val id: String = "",
    val gameId: String = "",
    val gameName: String = "",
    val exeName: String = "",
    val startedAt: String = "",
    val endedAt: String = "",
    val durationMs: Long = 0,
    val sampleCount: Int = 0,
    val avgFps: Double? = null,
    val medianFps: Double? = null,
    val low1Fps: Double? = null,
    val low01Fps: Double? = null,
    val avgFrameTimeMs: Double? = null,
    val maxCpuTempC: Double? = null,
    val maxGpuTempC: Double? = null,
    val maxCpuPowerW: Double? = null,
    val maxGpuPowerW: Double? = null,
    val avgCpuLoadPct: Double? = null,
    val avgGpuLoadPct: Double? = null,
    val maxVramUsedMb: Double? = null,
    val stopReason: String = "user",
    val schemaVersion: Int = 1,
)

/**
 * Flat envelope shared by BLE and WebSocket. The PC only sends fields relevant to
 * the message type, so everything is optional and defaults are harmless.
 */
@Serializable
data class WireMessage(
    val type: String = "",
    val protocolVersion: Int? = null,
    val serverVersion: String? = null,
    val token: String? = null,
    val deviceName: String? = null,
    val appVersion: String? = null,
    val pcName: String? = null,
    val pcId: String? = null,
    val ok: Boolean? = null,
    val requiresPairing: Boolean? = null,
    val wsPort: Int? = null,
    val wsHosts: List<String>? = null,
    val prompt: PromptInfo? = null,
    val session: SessionInfo? = null,
    val sample: TelemetrySample? = null,
    val summary: SessionSummary? = null,
    val sessions: List<SessionSummary>? = null,
    val samples: List<TelemetrySample>? = null,
    val games: List<GameDto>? = null,
    val promptId: String? = null,
    val sessionId: String? = null,
    val gameId: String? = null,
    val reason: String? = null,
    val code: String? = null,
    val message: String? = null,
)

object MessageTypes {
    const val HELLO = "hello"
    const val HELLO_ACK = "hello_ack"
    const val BLE_HELLO = "ble_hello"
    const val BLE_HELLO_ACK = "ble_hello_ack"
    const val DEVICE_INFO = "device_info"
    const val PROMPT = "prompt"
    const val PROMPT_CLOSED = "prompt_closed"
    const val SESSION_STARTED = "session_started"
    const val SAMPLE = "sample"
    const val SESSION_ENDED = "session_ended"
    const val SESSIONS = "sessions"
    const val SESSION_DETAIL = "session_detail"
    const val START_SESSION = "start_session"
    const val DECLINE_PROMPT = "decline_prompt"
    const val STOP_SESSION = "stop_session"
    const val LIST_SESSIONS = "list_sessions"
    const val GET_SESSION = "get_session"
    const val PING = "ping"
    const val PONG = "pong"
    const val ERROR = "error"
}

object BleUuids {
    const val SERVICE = "8f1e0001-5c3a-4f8e-9b7d-2a6c1f4e9a10"
    const val CONTROL = "8f1e0002-5c3a-4f8e-9b7d-2a6c1f4e9a10"
    const val EVENT = "8f1e0003-5c3a-4f8e-9b7d-2a6c1f4e9a10"
    const val CLIENT_CHARACTERISTIC_CONFIG = "00002902-0000-1000-8000-00805f9b34fb"
}
