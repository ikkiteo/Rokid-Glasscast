package dev.glasscast.proto

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.net.wifi.WifiManager
import android.util.Log
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlin.coroutines.resume

private const val TAG = "GlassLan"
const val NSD_TYPE = "_glasscast._tcp."

/**
 * Rokid / phone Wi‑Fi silently drops UDP unless the process holds a multicast lock.
 * MAL already learned this; GlassCast uses the same lock plus DNS‑SD as a second path.
 */
class Lan(context: Context) {
    private val app = context.applicationContext
    private var multicast: WifiManager.MulticastLock? = null
    private var wifi: WifiManager.WifiLock? = null
    private var nsd: NsdManager? = null
    private var registration: NsdManager.RegistrationListener? = null
    private var discovery: NsdManager.DiscoveryListener? = null

    fun hold() {
        val wm = app.getSystemService(Context.WIFI_SERVICE) as? WifiManager ?: return
        if (multicast?.isHeld != true) {
            val lock = wm.createMulticastLock("glasscast")
            lock.setReferenceCounted(false)
            runCatching { lock.acquire() }
            multicast = lock
        }
        @Suppress("DEPRECATION")
        if (wifi?.isHeld != true) {
            val lock = wm.createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "glasscast-wifi")
            lock.setReferenceCounted(false)
            runCatching { lock.acquire() }
            wifi = lock
        }
    }

    fun release() {
        stopNsd()
        runCatching { multicast?.release() }
        runCatching { wifi?.release() }
        multicast = null
        wifi = null
    }

    fun advertise(name: String, port: Int) {
        hold()
        stopNsd()
        val mgr = app.getSystemService(Context.NSD_SERVICE) as NsdManager
        nsd = mgr
        val info = NsdServiceInfo().apply {
            serviceName = "GlassCast-$name".take(40)
            serviceType = NSD_TYPE
            this.port = port
        }
        val listener = object : NsdManager.RegistrationListener {
            override fun onRegistrationFailed(s: NsdServiceInfo, c: Int) { Log.w(TAG, "nsd fail $c") }
            override fun onUnregistrationFailed(s: NsdServiceInfo, c: Int) = Unit
            override fun onServiceRegistered(s: NsdServiceInfo) { Log.i(TAG, "nsd ${s.serviceName}") }
            override fun onServiceUnregistered(s: NsdServiceInfo) = Unit
        }
        registration = listener
        runCatching { mgr.registerService(info, NsdManager.PROTOCOL_DNS_SD, listener) }
            .onFailure { Log.w(TAG, "register", it) }
    }

    suspend fun discover(millis: Long = 2500): List<Found> {
        hold()
        val found = LinkedHashMap<String, Found>()
        val mgr = app.getSystemService(Context.NSD_SERVICE) as NsdManager
        nsd = mgr
        return suspendCancellableCoroutine { cont ->
            val listener = object : NsdManager.DiscoveryListener {
                override fun onStartDiscoveryFailed(t: String, c: Int) {
                    Log.w(TAG, "discover fail $c")
                    if (cont.isActive) cont.resume(found.values.toList())
                }
                override fun onStopDiscoveryFailed(t: String, c: Int) = Unit
                override fun onDiscoveryStarted(t: String) = Unit
                override fun onDiscoveryStopped(t: String) {
                    if (cont.isActive) cont.resume(found.values.toList())
                }
                override fun onServiceLost(s: NsdServiceInfo) = Unit
                override fun onServiceFound(s: NsdServiceInfo) {
                    val type = s.serviceType ?: ""
                    val name = s.serviceName ?: ""
                    if (!type.contains("glasscast", true) && !name.contains("GlassCast", true)) return
                    resolve(mgr, s) { host, port ->
                        if (host.isBlank()) return@resolve
                        found[host] = Found(host, port, name.ifBlank { host }, 480, 640, 60)
                    }
                }
            }
            discovery = listener
            runCatching { mgr.discoverServices(NSD_TYPE, NsdManager.PROTOCOL_DNS_SD, listener) }
            val stopper = android.os.Handler(app.mainLooper)
            stopper.postDelayed({
                runCatching { mgr.stopServiceDiscovery(listener) }
                if (cont.isActive) cont.resume(found.values.toList())
            }, millis)
            cont.invokeOnCancellation {
                stopper.removeCallbacksAndMessages(null)
                runCatching { mgr.stopServiceDiscovery(listener) }
            }
        }
    }

    private fun stopNsd() {
        registration?.let { runCatching { nsd?.unregisterService(it) } }
        discovery?.let { runCatching { nsd?.stopServiceDiscovery(it) } }
        registration = null
        discovery = null
    }

    private fun resolve(mgr: NsdManager, info: NsdServiceInfo, done: (String, Int) -> Unit) {
        val listener = object : NsdManager.ResolveListener {
            override fun onResolveFailed(s: NsdServiceInfo, c: Int) = Unit
            override fun onServiceResolved(s: NsdServiceInfo) {
                val host = runCatching {
                    @Suppress("DEPRECATION")
                    s.host?.hostAddress
                }.getOrNull() ?: return
                val ip = host.substringAfterLast('/').substringBefore('%')
                if (ip.contains(':')) return
                done(ip, if (s.port > 0) s.port else Wire.TCP_PORT)
            }
        }
        runCatching { mgr.resolveService(info, listener) }
    }
}
