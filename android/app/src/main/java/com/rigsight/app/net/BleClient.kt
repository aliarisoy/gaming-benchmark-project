package com.rigsight.app.net

import android.annotation.SuppressLint
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothGatt
import android.bluetooth.BluetoothGattCallback
import android.bluetooth.BluetoothGattCharacteristic
import android.bluetooth.BluetoothGattDescriptor
import android.bluetooth.BluetoothProfile
import android.content.Context
import android.os.Build
import android.os.Handler
import android.os.Looper
import com.rigsight.app.protocol.BleFrameCodec
import com.rigsight.app.protocol.BleUuids
import com.rigsight.app.protocol.MessageTypes
import com.rigsight.app.protocol.APP_VERSION
import com.rigsight.app.protocol.PROTOCOL_VERSION
import com.rigsight.app.protocol.RigJson
import com.rigsight.app.protocol.WireMessage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch
import java.util.UUID

/**
 * GATT central that talks to the PC's RigSight peripheral. Writes are framed and
 * serialized; notifications are reassembled before decoding.
 */
@SuppressLint("MissingPermission")
class BleClient(
    private val context: Context,
    private val scope: CoroutineScope,
    private val listener: Listener,
) {
    interface Listener {
        fun onConnected(deviceName: String?, address: String)
        fun onDisconnected()
        fun onMessage(json: String)
        fun onError(message: String)
    }

    private val main = Handler(Looper.getMainLooper())
    private val reassembler = BleFrameCodec.Reassembler()
    private val writeLock = Any()
    private val writeQueue = ArrayDeque<ByteArray>()

    private var gatt: BluetoothGatt? = null
    private var control: BluetoothGattCharacteristic? = null
    private var event: BluetoothGattCharacteristic? = null
    private var maxFrameSize = 20
    private var writing = false
    private var inFlightFrame: ByteArray? = null
    private var intentional = false
    private var disconnectedNotified = false

    @SuppressLint("MissingPermission")
    fun connect(device: BluetoothDevice, code: String?, token: String?, deviceName: String) {
        intentional = false
        disconnectedNotified = false
        pendingHello = WireMessage(
            type = MessageTypes.BLE_HELLO,
            deviceName = deviceName,
            token = token?.takeIf { it.isNotBlank() },
            code = code?.takeIf { it.isNotBlank() },
            protocolVersion = PROTOCOL_VERSION,
            appVersion = APP_VERSION,
        )
        try {
            gatt = device.connectGatt(context, false, callback, BluetoothDevice.TRANSPORT_LE)
            if (gatt == null) fail("Could not open a Bluetooth connection to the PC.")
        } catch (ex: SecurityException) {
            fail("Nearby devices permission is required to connect.")
        } catch (ex: Exception) {
            fail("Bluetooth connection failed: ${ex.message}")
        }
    }

    private var pendingHello: WireMessage? = null

    fun send(json: String) {
        val frames = BleFrameCodec.encode(json, maxFrameSize)
        synchronized(writeLock) { writeQueue.addAll(frames) }
        main.post { pumpWrites() }
    }

    @SuppressLint("MissingPermission")
    fun close() {
        intentional = true
        try { gatt?.disconnect() } catch (_: Exception) { }
        try { gatt?.close() } catch (_: Exception) { }
        gatt = null
        control = null
        event = null
        synchronized(writeLock) { writeQueue.clear() }
        writing = false
    }

    private val callback = object : BluetoothGattCallback() {
        @SuppressLint("MissingPermission")
        override fun onConnectionStateChange(g: BluetoothGatt, status: Int, newState: Int) {
            if (newState == BluetoothProfile.STATE_CONNECTED && status == BluetoothGatt.GATT_SUCCESS) {
                listenerName = try { g.device.name } catch (_: SecurityException) { null }
                try { g.requestConnectionPriority(BluetoothGatt.CONNECTION_PRIORITY_HIGH) } catch (_: Exception) { }
                if (!g.discoverServices()) fail("Could not discover the PC's Bluetooth services.")
            } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                cleanupAndNotify(status)
            } else if (status != BluetoothGatt.GATT_SUCCESS) {
                fail("Bluetooth link failed (status $status).")
            }
        }

        override fun onServicesDiscovered(g: BluetoothGatt, status: Int) {
            if (status != BluetoothGatt.GATT_SUCCESS) {
                fail("Could not read the PC's Bluetooth service.")
                return
            }
            val service = g.getService(UUID.fromString(BleUuids.SERVICE))
            if (service == null) {
                fail("This device is not advertising RigSight. Try scanning again.")
                return
            }
            control = service.getCharacteristic(UUID.fromString(BleUuids.CONTROL))
            event = service.getCharacteristic(UUID.fromString(BleUuids.EVENT))
            if (control == null || event == null) {
                fail("The PC's RigSight Bluetooth service is incomplete.")
                return
            }

            val enabled = try { g.setCharacteristicNotification(event, true) } catch (_: SecurityException) { false }
            if (!enabled) {
                fail("Could not subscribe to PC events.")
                return
            }
            val descriptor = event!!.getDescriptor(UUID.fromString(BleUuids.CLIENT_CHARACTERISTIC_CONFIG))
            if (descriptor == null) {
                // Older stacks may already have notifications on; send hello anyway.
                sendHello()
                return
            }
            try {
                if (Build.VERSION.SDK_INT >= 33) {
                    g.writeDescriptor(descriptor, BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE)
                } else {
                    @Suppress("DEPRECATION")
                    descriptor.value = BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE
                    @Suppress("DEPRECATION")
                    g.writeDescriptor(descriptor)
                }
            } catch (ex: SecurityException) {
                fail("Nearby devices permission is required to pair.")
            }
        }

        override fun onDescriptorWrite(g: BluetoothGatt, descriptor: BluetoothGattDescriptor, status: Int) {
            if (status == BluetoothGatt.GATT_SUCCESS) {
                // Ask for a larger MTU after the first message; a failed request is harmless.
                try { g.requestMtu(517) } catch (_: Exception) { }
                sendHello()
            } else {
                fail("Could not subscribe to PC events (status $status).")
            }
        }

        override fun onMtuChanged(g: BluetoothGatt, mtu: Int, status: Int) {
            if (status == BluetoothGatt.GATT_SUCCESS) {
                maxFrameSize = (mtu - 3).coerceIn(20, 512)
            }
        }

        @Deprecated("Used on Android 12 and lower")
        override fun onCharacteristicChanged(g: BluetoothGatt, characteristic: BluetoothGattCharacteristic) {
            @Suppress("DEPRECATION")
            handleNotification(characteristic.value ?: return)
        }

        override fun onCharacteristicChanged(
            g: BluetoothGatt,
            characteristic: BluetoothGattCharacteristic,
            value: ByteArray,
        ) {
            handleNotification(value)
        }

        override fun onCharacteristicWrite(
            g: BluetoothGatt,
            characteristic: BluetoothGattCharacteristic,
            status: Int,
        ) {
            synchronized(writeLock) {
                writing = false
                if (status != BluetoothGatt.GATT_SUCCESS) {
                    // Put the rejected frame back; the next attempt uses a smaller MTU.
                    inFlightFrame?.let { writeQueue.addFirst(it) }
                    if (maxFrameSize > 20) maxFrameSize = 20
                }
                inFlightFrame = null
            }
            main.postDelayed({ pumpWrites() }, 10)
        }
    }

    private var listenerName: String? = null

    @SuppressLint("MissingPermission")
    private fun sendHello() {
        val hello = pendingHello ?: return
        listener.onConnected(listenerName, gatt?.device?.address.orEmpty())
        send(RigJson.encodeToString(WireMessage.serializer(), hello))
    }

    private fun handleNotification(value: ByteArray) {
        val json = reassembler.add(value) ?: return
        scope.launch { listener.onMessage(json) }
    }

    @SuppressLint("MissingPermission")
    private fun pumpWrites() {
        val g = gatt ?: return
        val characteristic = control ?: return
        val frame: ByteArray
        synchronized(writeLock) {
            if (writing || writeQueue.isEmpty()) return
            frame = writeQueue.removeFirst()
            writing = true
            inFlightFrame = frame
        }

        val status: Int = try {
            if (Build.VERSION.SDK_INT >= 33) {
                g.writeCharacteristic(characteristic, frame, BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT)
            } else {
                @Suppress("DEPRECATION")
                characteristic.writeType = BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT
                @Suppress("DEPRECATION")
                characteristic.value = frame
                @Suppress("DEPRECATION")
                val ok = g.writeCharacteristic(characteristic)
                if (ok) BluetoothGatt.GATT_SUCCESS else -1
            }
        } catch (_: SecurityException) {
            -1
        } catch (_: Exception) {
            -1
        }

        if (status != BluetoothGatt.GATT_SUCCESS) {
            synchronized(writeLock) {
                writing = false
                writeQueue.addFirst(frame)
                inFlightFrame = null
            }
            main.postDelayed({ pumpWrites() }, 40)
        }
    }

    private fun handleDisconnected() {
        scope.launch { listener.onDisconnected() }
    }

    @SuppressLint("MissingPermission")
    private fun cleanupAndNotify(status: Int) {
        if (disconnectedNotified) return
        disconnectedNotified = true
        try { gatt?.close() } catch (_: Exception) { }
        gatt = null
        control = null
        event = null
        if (!intentional && status != BluetoothGatt.GATT_SUCCESS) {
            // A non-success status is informative; the disconnect callback still fires.
        }
        handleDisconnected()
    }

    private fun fail(message: String) {
        scope.launch { listener.onError(message) }
        intentional = false
        try { gatt?.disconnect() } catch (_: Exception) { }
        try { gatt?.close() } catch (_: Exception) { }
        gatt = null
        if (!disconnectedNotified) {
            disconnectedNotified = true
            handleDisconnected()
        }
    }
}
