package com.rigsight.app.net

import android.annotation.SuppressLint
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.bluetooth.le.ScanCallback
import android.bluetooth.le.ScanFilter
import android.bluetooth.le.ScanResult
import android.bluetooth.le.ScanSettings
import android.content.Context
import android.os.ParcelUuid
import com.rigsight.app.protocol.BleUuids
import java.util.UUID

/** Service-filtered BLE scan for PCs advertising the RigSight GATT service. */
class BleScanner(context: Context, private val listener: Listener) {
    interface Listener {
        fun onDevice(device: BluetoothDevice, name: String?, rssi: Int)
        fun onError(message: String)
    }

    private val adapter = context.getSystemService(BluetoothManager::class.java)?.adapter
    private var scanning = false

    private val callback = object : ScanCallback() {
        override fun onScanResult(callbackType: Int, result: ScanResult) {
            val device = result.device ?: return
            val name = try {
                result.scanRecord?.deviceName ?: device.name
            } catch (_: SecurityException) {
                result.scanRecord?.deviceName
            }
            listener.onDevice(device, name, result.rssi)
        }

        override fun onBatchScanResults(results: MutableList<ScanResult>) {
            results.forEach { onScanResult(ScanSettings.CALLBACK_TYPE_ALL_MATCHES, it) }
        }

        override fun onScanFailed(errorCode: Int) {
            scanning = false
            listener.onError(
                when (errorCode) {
                    SCAN_FAILED_ALREADY_STARTED -> "Bluetooth scan is already running."
                    SCAN_FAILED_APPLICATION_REGISTRATION_FAILED -> "Android could not start the Bluetooth scanner."
                    SCAN_FAILED_FEATURE_UNSUPPORTED -> "This phone does not support Bluetooth LE scanning."
                    else -> "Bluetooth scan failed (code $errorCode)."
                }
            )
        }
    }

    @SuppressLint("MissingPermission")
    fun start(): Boolean {
        val bluetoothAdapter = adapter ?: run {
            listener.onError("This phone has no Bluetooth adapter.")
            return false
        }
        if (!bluetoothAdapter.isEnabled) {
            listener.onError("Turn on Bluetooth, then scan again.")
            return false
        }
        val scanner = bluetoothAdapter.bluetoothLeScanner ?: run {
            listener.onError("Bluetooth LE scanning is unavailable right now.")
            return false
        }

        return try {
            val filter = ScanFilter.Builder()
                .setServiceUuid(ParcelUuid(UUID.fromString(BleUuids.SERVICE)))
                .build()
            val settings = ScanSettings.Builder()
                .setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY)
                .setReportDelay(0)
                .build()
            scanner.startScan(listOf(filter), settings, callback)
            scanning = true
            true
        } catch (ex: SecurityException) {
            listener.onError("Nearby devices permission is required to scan.")
            false
        } catch (ex: Exception) {
            listener.onError("Could not start Bluetooth scan: ${ex.message}")
            false
        }
    }

    @SuppressLint("MissingPermission")
    fun stop() {
        if (!scanning) return
        scanning = false
        try {
            adapter?.bluetoothLeScanner?.stopScan(callback)
        } catch (_: SecurityException) {
            // Permission may have been revoked while scanning.
        }
    }
}
