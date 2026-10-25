package dev.glasscast.proto

import android.content.Context
import android.util.Log
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONObject

private const val TAG = "GlassBeacon"

data class Found(val host: String, val port: Int, val name: String, val width: Int, val height: Int, val fps: Int)

/**
 * A three-line answering machine on UDP: senders shout into the subnet and every pair of
 * glasses replies with where to connect and how big its panel is.
 */
object Beacon {
    fun answer(scope: CoroutineScope, name: String, width: Int, height: Int, fps: Int): Job =
        scope.launch(Dispatchers.IO) {
            val reply = JSONObject()
                .put("app", "glasscast")
                .put("name", name)
                .put("port", Wire.TCP_PORT)
                .put("w", width)
                .put("h", height)
                .put("fps", fps)
                .put("v", 1)
                .toString()
                .toByteArray(Charsets.UTF_8)
            while (isActive) {
                runCatching {
                    DatagramSocket(null).use { sock ->
                        sock.reuseAddress = true
                        sock.broadcast = true
                        sock.bind(InetSocketAddress(Wire.UDP_PORT))
                        val buf = ByteArray(64)
                        while (isActive) {
                            val probe = DatagramPacket(buf, buf.size)
                            sock.receive(probe)
                            val text = String(probe.data, 0, probe.length, Charsets.US_ASCII)
                            if (!text.startsWith("GCASTQ")) continue
                            sock.send(DatagramPacket(reply, reply.size, probe.address, probe.port))
                        }
                    }
                }.onFailure { Log.w(TAG, "answer", it) }
                if (!isActive) return@launch
                kotlinx.coroutines.delay(800)
            }
        }

    suspend fun scan(context: Context, millis: Long = 2500, hint: String = ""): List<Found> =
        withContext(Dispatchers.IO) {
            val lan = Lan(context)
            lan.hold()
            val udp = udpScan(millis, hint)
            val nsd = runCatching { lan.discover(millis.coerceAtLeast(1800)) }.getOrDefault(emptyList())
            val out = LinkedHashMap<String, Found>()
            (udp + nsd).forEach { out[it.host] = it }
            out.values.toList()
        }

    private fun udpScan(millis: Long, hint: String): List<Found> {
        val out = LinkedHashMap<String, Found>()
        runCatching {
            DatagramSocket(null).use { sock ->
                sock.reuseAddress = true
                sock.broadcast = true
                sock.soTimeout = 300
                sock.bind(InetSocketAddress(0))
                val probe = Wire.PROBE.toByteArray(Charsets.US_ASCII)
                for (target in broadcastTargets(hint)) {
                    runCatching {
                        sock.send(DatagramPacket(probe, probe.size, target, Wire.UDP_PORT))
                    }
                }
                val until = System.currentTimeMillis() + millis
                val buf = ByteArray(1024)
                while (System.currentTimeMillis() < until) {
                    val pkt = DatagramPacket(buf, buf.size)
                    val got = runCatching { sock.receive(pkt); true }.getOrDefault(false)
                    if (!got) continue
                    val body = String(pkt.data, 0, pkt.length, Charsets.UTF_8)
                    val o = runCatching { JSONObject(body) }.getOrNull() ?: continue
                    if (o.optString("app") != "glasscast") continue
                    val host = pkt.address.hostAddress ?: continue
                    if (host.contains(':')) continue
                    out[host] = Found(
                        host = host,
                        port = o.optInt("port", Wire.TCP_PORT),
                        name = o.optString("name", host),
                        width = o.optInt("w", 480),
                        height = o.optInt("h", 640),
                        fps = o.optInt("fps", 60)
                    )
                }
            }
        }.onFailure { Log.w(TAG, "scan", it) }
        return out.values.toList()
    }

    private fun broadcastTargets(hint: String): List<InetAddress> {
        val out = ArrayList<InetAddress>()
        if (hint.isNotBlank()) runCatching { out += InetAddress.getByName(hint.trim()) }
        runCatching { out += InetAddress.getByName("255.255.255.255") }
        runCatching {
            java.net.NetworkInterface.getNetworkInterfaces().toList().forEach { nif ->
                if (!nif.isUp || nif.isLoopback) return@forEach
                nif.interfaceAddresses.forEach { addr ->
                    addr.broadcast?.let { out += it }
                }
            }
        }
        return out.distinct()
    }
}
