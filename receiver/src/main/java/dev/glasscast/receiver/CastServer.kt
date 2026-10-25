package dev.glasscast.receiver

import android.util.Log
import dev.glasscast.proto.ControlWriter
import dev.glasscast.proto.FrameReader
import dev.glasscast.proto.Hello
import dev.glasscast.proto.Packet
import dev.glasscast.proto.Wire
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

private const val TAG = "GlassServer"

/**
 * Listens for one sender at a time. A fresh connection always wins: if the phone reconnects after
 * a WiFi hiccup, the wearer should not have to close a stale session by hand.
 */
class CastServer(
    private val scope: CoroutineScope,
    private val onHello: (Hello) -> Unit,
    private val onPacket: (Packet) -> Unit,
    private val onGone: (String) -> Unit
) {
    @Volatile private var server: ServerSocket? = null
    @Volatile private var client: Socket? = null
    @Volatile private var control: ControlWriter? = null

    @Volatile var bytes = 0L
        private set
    @Volatile var frames = 0L
        private set
    @Volatile var peer: String = ""
        private set

    fun start(): Job = scope.launch(Dispatchers.IO) {
        while (isActive) {
            val sock = runCatching {
                ServerSocket().apply {
                    reuseAddress = true
                    bind(InetSocketAddress(Wire.TCP_PORT), 2)
                }
            }.getOrNull()
            if (sock == null) {
                Log.w(TAG, "port ${Wire.TCP_PORT} busy, retrying")
                kotlinx.coroutines.delay(1500)
                continue
            }
            server = sock
            Log.i(TAG, "listening on ${Wire.TCP_PORT}")
            while (isActive) {
                val next = runCatching { sock.accept() }.getOrNull() ?: break
                client?.let { runCatching { it.close() } }
                client = next
                scope.launch(Dispatchers.IO) { serve(next) }
            }
            runCatching { sock.close() }
            server = null
            if (isActive) kotlinx.coroutines.delay(500)
        }
    }

    fun requestKeyframe() = runCatching { control?.send(Wire.CMD_KEYFRAME) }.let { }

    fun setBitrate(kbps: Int) = runCatching { control?.send(Wire.CMD_BITRATE, kbps) }.let { }

    fun stop() {
        runCatching { client?.close() }
        runCatching { server?.close() }
        client = null
        server = null
        control = null
    }

    private fun serve(sock: Socket) {
        peer = sock.inetAddress?.hostAddress ?: "?"
        runCatching {
            sock.tcpNoDelay = true
            sock.keepAlive = true
            sock.receiveBufferSize = 128 * 1024
            sock.soTimeout = 3000
            val reader = FrameReader(sock.getInputStream())
            control = ControlWriter(sock.getOutputStream())
            val hello = reader.hello()
            Log.i(TAG, "sender ${hello.name} ${hello.width}x${hello.height}@${hello.fps}")
            onHello(hello)
            // The sender always leads with codec config, but asking costs nothing and rescues us
            // when we joined a stream that was already running.
            control?.send(Wire.CMD_KEYFRAME)
            while (true) {
                val packet = reader.next() ?: break
                bytes += packet.bytes.size + 13
                if (!packet.isAlive) frames++
                onPacket(packet)
            }
        }.onFailure { Log.i(TAG, "session end: ${it.message}") }
        runCatching { sock.close() }
        if (client === sock) {
            client = null
            control = null
            onGone(peer)
        }
    }
}
