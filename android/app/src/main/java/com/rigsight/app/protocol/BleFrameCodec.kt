package com.rigsight.app.protocol

import java.nio.charset.StandardCharsets
import kotlin.random.Random

/**
 * BLE fragments: 'R' | version | messageId (u16 BE) | index | count | UTF-8 JSON.
 * The receiver also accepts an untagged JSON write/notification, matching the PC.
 */
object BleFrameCodec {
    private const val MAGIC = 0x52
    private const val VERSION = 0x01
    const val HEADER_SIZE = 6

    fun encode(json: String, maxFrameSize: Int, messageId: Int = Random.nextInt(0, 65536)): List<ByteArray> {
        val payload = json.toByteArray(StandardCharsets.UTF_8)
        val frameSize = maxFrameSize.coerceAtLeast(23)
        val maxPayload = (frameSize - HEADER_SIZE).coerceAtLeast(1)
        val count = ((payload.size + maxPayload - 1) / maxPayload).coerceAtLeast(1)
        require(count <= 255) { "BLE message is too large to fragment" }

        return List(count) { index ->
            val offset = index * maxPayload
            val length = minOf(maxPayload, payload.size - offset)
            ByteArray(HEADER_SIZE + length).also { frame ->
                frame[0] = MAGIC.toByte()
                frame[1] = VERSION.toByte()
                frame[2] = ((messageId shr 8) and 0xFF).toByte()
                frame[3] = (messageId and 0xFF).toByte()
                frame[4] = index.toByte()
                frame[5] = count.toByte()
                payload.copyInto(frame, HEADER_SIZE, offset, offset + length)
            }
        }
    }

    class Reassembler {
        private data class Pending(
            val count: Int,
            val chunks: Array<ByteArray?>,
            var updatedAt: Long,
        )

        private val pending = HashMap<Int, Pending>()

        @Synchronized
        fun add(data: ByteArray): String? {
            trimExpired()
            if (data.size < 2 || (data[0].toInt() and 0xFF) != MAGIC) {
                return String(data, StandardCharsets.UTF_8)
            }
            if ((data[1].toInt() and 0xFF) != VERSION || data.size < HEADER_SIZE) return null

            val messageId = ((data[2].toInt() and 0xFF) shl 8) or (data[3].toInt() and 0xFF)
            val index = data[4].toInt() and 0xFF
            val count = data[5].toInt() and 0xFF
            if (count == 0 || index >= count) return null

            val state = pending.getOrPut(messageId) {
                Pending(count, arrayOfNulls(count), System.currentTimeMillis())
            }
            if (state.count != count) {
                pending.remove(messageId)
                return null
            }

            state.chunks[index] = data.copyOfRange(HEADER_SIZE, data.size)
            state.updatedAt = System.currentTimeMillis()
            if (state.chunks.any { it == null }) return null

            pending.remove(messageId)
            val total = state.chunks.sumOf { it!!.size }
            val merged = ByteArray(total)
            var cursor = 0
            for (chunk in state.chunks) {
                val bytes = chunk ?: return null
                bytes.copyInto(merged, cursor)
                cursor += bytes.size
            }
            return String(merged, StandardCharsets.UTF_8)
        }

        private fun trimExpired() {
            val cutoff = System.currentTimeMillis() - 5_000
            pending.entries.removeAll { it.value.updatedAt < cutoff }
        }
    }
}
