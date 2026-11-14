package com.rigsight.app.data

import android.content.ContentValues
import android.content.Context
import android.database.Cursor
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import com.rigsight.app.protocol.SessionSummary
import com.rigsight.app.protocol.TelemetrySample
import com.rigsight.app.util.toEpochMillis

/**
 * Local mirror of the PC's session index, plus downsampled charts delivered via
 * `get_session`. The PC remains the source of truth.
 */
class HistoryStore(context: Context) : SQLiteOpenHelper(context, "history.db", null, 1) {
    private val lock = Any()

    init {
        setWriteAheadLoggingEnabled(true)
    }

    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL(
            """
            CREATE TABLE sessions (
                id TEXT PRIMARY KEY NOT NULL,
                gameId TEXT NOT NULL DEFAULT '',
                gameName TEXT NOT NULL DEFAULT '',
                exeName TEXT NOT NULL DEFAULT '',
                startedAt TEXT NOT NULL DEFAULT '',
                endedAt TEXT NOT NULL DEFAULT '',
                startedAtMs INTEGER NOT NULL DEFAULT 0,
                durationMs INTEGER NOT NULL DEFAULT 0,
                sampleCount INTEGER NOT NULL DEFAULT 0,
                avgFps REAL, medianFps REAL, low1Fps REAL, low01Fps REAL,
                avgFrameTimeMs REAL,
                maxCpuTempC REAL, maxGpuTempC REAL,
                maxCpuPowerW REAL, maxGpuPowerW REAL,
                avgCpuLoadPct REAL, avgGpuLoadPct REAL,
                maxVramUsedMb REAL,
                stopReason TEXT NOT NULL DEFAULT 'user',
                schemaVersion INTEGER NOT NULL DEFAULT 1,
                syncedAt INTEGER NOT NULL DEFAULT 0
            )
            """.trimIndent()
        )
        db.execSQL(
            """
            CREATE TABLE samples (
                sessionId TEXT NOT NULL,
                t INTEGER NOT NULL,
                fps REAL, frameTimeMs REAL, low1Fps REAL, low01Fps REAL,
                cpuLoadPct REAL, cpuTempC REAL, cpuPowerW REAL, cpuClockMhz REAL,
                gpuLoadPct REAL, gpuTempC REAL, gpuHotspotC REAL, gpuPowerW REAL, gpuClockMhz REAL,
                vramUsedMb REAL, vramTotalMb REAL, ramUsedMb REAL, ramTotalMb REAL,
                PRIMARY KEY (sessionId, t)
            )
            """.trimIndent()
        )
        db.execSQL("CREATE INDEX idx_samples_session ON samples(sessionId, t)")
        db.execSQL("CREATE INDEX idx_sessions_started ON sessions(startedAtMs DESC)")
    }

    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) {
        // Version 1 is the first shipped schema. Future migrations go here.
    }

    fun getSessions(): List<SessionSummary> = synchronized(lock) {
        readableDatabase.query(
            "sessions", null, null, null, null, null, "startedAtMs DESC"
        ).use { cursor ->
            buildList {
                while (cursor.moveToNext()) add(cursor.toSummary())
            }
        }
    }

    fun getSession(id: String): SessionSummary? = synchronized(lock) {
        readableDatabase.query(
            "sessions", null, "id = ?", arrayOf(id), null, null, null
        ).use { cursor ->
            if (cursor.moveToFirst()) cursor.toSummary() else null
        }
    }

    fun hasSamples(sessionId: String): Boolean = synchronized(lock) {
        readableDatabase.rawQuery(
            "SELECT 1 FROM samples WHERE sessionId = ? LIMIT 1", arrayOf(sessionId)
        ).use { it.moveToFirst() }
    }

    fun upsertSummaries(summaries: List<SessionSummary>) = synchronized(lock) {
        val db = writableDatabase
        db.beginTransaction()
        try {
            for (summary in summaries) upsertSummary(db, summary)
            db.setTransactionSuccessful()
        } finally {
            db.endTransaction()
        }
    }

    fun upsertSummary(summary: SessionSummary) = synchronized(lock) {
        upsertSummary(writableDatabase, summary)
    }

    fun saveDetail(summary: SessionSummary, samples: List<TelemetrySample>) = synchronized(lock) {
        val db = writableDatabase
        db.beginTransaction()
        try {
            upsertSummary(db, summary)
            db.delete("samples", "sessionId = ?", arrayOf(summary.id))
            for (sample in samples) db.insertWithOnConflict(
                "samples", null, sample.toValues(summary.id), SQLiteDatabase.CONFLICT_REPLACE
            )
            db.setTransactionSuccessful()
        } finally {
            db.endTransaction()
        }
    }

    fun getSamples(sessionId: String): List<TelemetrySample> = synchronized(lock) {
        readableDatabase.query(
            "samples", null, "sessionId = ?", arrayOf(sessionId), null, null, "t ASC"
        ).use { cursor ->
            buildList {
                while (cursor.moveToNext()) add(cursor.toSample())
            }
        }
    }

    fun deleteSession(id: String) = synchronized(lock) {
        val db = writableDatabase
        db.beginTransaction()
        try {
            db.delete("samples", "sessionId = ?", arrayOf(id))
            db.delete("sessions", "id = ?", arrayOf(id))
            db.setTransactionSuccessful()
        } finally {
            db.endTransaction()
        }
    }

    fun clearAll() = synchronized(lock) {
        val db = writableDatabase
        db.beginTransaction()
        try {
            db.delete("samples", null, null)
            db.delete("sessions", null, null)
            db.setTransactionSuccessful()
        } finally {
            db.endTransaction()
        }
    }

    private fun upsertSummary(db: SQLiteDatabase, summary: SessionSummary) {
        val values = ContentValues().apply {
            put("id", summary.id)
            put("gameId", summary.gameId)
            put("gameName", summary.gameName)
            put("exeName", summary.exeName)
            put("startedAt", summary.startedAt)
            put("endedAt", summary.endedAt)
            put("startedAtMs", summary.startedAt.toEpochMillis())
            put("durationMs", summary.durationMs)
            put("sampleCount", summary.sampleCount)
            putNullable("avgFps", summary.avgFps)
            putNullable("medianFps", summary.medianFps)
            putNullable("low1Fps", summary.low1Fps)
            putNullable("low01Fps", summary.low01Fps)
            putNullable("avgFrameTimeMs", summary.avgFrameTimeMs)
            putNullable("maxCpuTempC", summary.maxCpuTempC)
            putNullable("maxGpuTempC", summary.maxGpuTempC)
            putNullable("maxCpuPowerW", summary.maxCpuPowerW)
            putNullable("maxGpuPowerW", summary.maxGpuPowerW)
            putNullable("avgCpuLoadPct", summary.avgCpuLoadPct)
            putNullable("avgGpuLoadPct", summary.avgGpuLoadPct)
            putNullable("maxVramUsedMb", summary.maxVramUsedMb)
            put("stopReason", summary.stopReason)
            put("schemaVersion", summary.schemaVersion)
            put("syncedAt", System.currentTimeMillis())
        }
        db.insertWithOnConflict("sessions", null, values, SQLiteDatabase.CONFLICT_REPLACE)
    }

    private fun TelemetrySample.toValues(sessionId: String) = ContentValues().apply {
        put("sessionId", sessionId)
        put("t", t)
        putNullable("fps", fps)
        putNullable("frameTimeMs", frameTimeMs)
        putNullable("low1Fps", low1Fps)
        putNullable("low01Fps", low01Fps)
        putNullable("cpuLoadPct", cpuLoadPct)
        putNullable("cpuTempC", cpuTempC)
        putNullable("cpuPowerW", cpuPowerW)
        putNullable("cpuClockMhz", cpuClockMhz)
        putNullable("gpuLoadPct", gpuLoadPct)
        putNullable("gpuTempC", gpuTempC)
        putNullable("gpuHotspotC", gpuHotspotC)
        putNullable("gpuPowerW", gpuPowerW)
        putNullable("gpuClockMhz", gpuClockMhz)
        putNullable("vramUsedMb", vramUsedMb)
        putNullable("vramTotalMb", vramTotalMb)
        putNullable("ramUsedMb", ramUsedMb)
        putNullable("ramTotalMb", ramTotalMb)
    }

    private fun Cursor.toSummary() = SessionSummary(
        id = getString(getColumnIndexOrThrow("id")),
        gameId = getString(getColumnIndexOrThrow("gameId")),
        gameName = getString(getColumnIndexOrThrow("gameName")),
        exeName = getString(getColumnIndexOrThrow("exeName")),
        startedAt = getString(getColumnIndexOrThrow("startedAt")),
        endedAt = getString(getColumnIndexOrThrow("endedAt")),
        durationMs = getLong(getColumnIndexOrThrow("durationMs")),
        sampleCount = getInt(getColumnIndexOrThrow("sampleCount")),
        avgFps = getNullableDouble("avgFps"),
        medianFps = getNullableDouble("medianFps"),
        low1Fps = getNullableDouble("low1Fps"),
        low01Fps = getNullableDouble("low01Fps"),
        avgFrameTimeMs = getNullableDouble("avgFrameTimeMs"),
        maxCpuTempC = getNullableDouble("maxCpuTempC"),
        maxGpuTempC = getNullableDouble("maxGpuTempC"),
        maxCpuPowerW = getNullableDouble("maxCpuPowerW"),
        maxGpuPowerW = getNullableDouble("maxGpuPowerW"),
        avgCpuLoadPct = getNullableDouble("avgCpuLoadPct"),
        avgGpuLoadPct = getNullableDouble("avgGpuLoadPct"),
        maxVramUsedMb = getNullableDouble("maxVramUsedMb"),
        stopReason = getString(getColumnIndexOrThrow("stopReason")),
        schemaVersion = getInt(getColumnIndexOrThrow("schemaVersion")),
    )

    private fun Cursor.toSample() = TelemetrySample(
        t = getLong(getColumnIndexOrThrow("t")),
        fps = getNullableDouble("fps"),
        frameTimeMs = getNullableDouble("frameTimeMs"),
        low1Fps = getNullableDouble("low1Fps"),
        low01Fps = getNullableDouble("low01Fps"),
        cpuLoadPct = getNullableDouble("cpuLoadPct"),
        cpuTempC = getNullableDouble("cpuTempC"),
        cpuPowerW = getNullableDouble("cpuPowerW"),
        cpuClockMhz = getNullableDouble("cpuClockMhz"),
        gpuLoadPct = getNullableDouble("gpuLoadPct"),
        gpuTempC = getNullableDouble("gpuTempC"),
        gpuHotspotC = getNullableDouble("gpuHotspotC"),
        gpuPowerW = getNullableDouble("gpuPowerW"),
        gpuClockMhz = getNullableDouble("gpuClockMhz"),
        vramUsedMb = getNullableDouble("vramUsedMb"),
        vramTotalMb = getNullableDouble("vramTotalMb"),
        ramUsedMb = getNullableDouble("ramUsedMb"),
        ramTotalMb = getNullableDouble("ramTotalMb"),
    )

    private fun Cursor.getNullableDouble(column: String): Double? {
        val index = getColumnIndexOrThrow(column)
        return if (isNull(index)) null else getDouble(index)
    }

    private fun ContentValues.putNullable(key: String, value: Double?) {
        if (value == null) putNull(key) else put(key, value)
    }
}
