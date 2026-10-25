package dev.glasscast.sender

import android.media.projection.MediaProjection
import android.util.Log
import dev.glasscast.proto.ControlReader
import dev.glasscast.proto.FrameWriter
import dev.glasscast.proto.Hello
import dev.glasscast.proto.Wire
import java.net.InetSocketAddress
import java.net.Socket
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

private const val TAG = "GlassClient"

class CastClient(
    private val scope: CoroutineScope,
    private val projection: MediaProjection,
    private val host: String,
    private val port: Int,
    private val hello: Hello,
    private val dpi: Int,
    private val bitrate: Int,
    private val onStatus: (String) -> Unit
) {
    @Volatile var pipe: ScreenPipe? = null
        private set
    @Volatile var connected = false
        private set

    private var job: Job? = null
    private var sock: Socket? = null

    fun start() {
        job = scope.launch(Dispatchers.IO) {
            while (isActive) {
                val err = runCatching { session(); null }.exceptionOrNull()
                connected = false
                pipe?.stop()
                pipe = null
                runCatching { sock?.close() }
                sock = null
                if (!isActive) return@launch
                onStatus(
                    if (err == null) "очки закрыли связь · переподключаюсь"
                    else "нет связи с $host · ${err.message ?: err.javaClass.simpleName}"
                )
                delay(1200)
            }
        }
    }

    fun stop() {
        job?.cancel()
        job = null
        runCatching { sock?.close() }
        pipe?.stop()
        pipe = null
        connected = false
    }

    private fun session() {
        onStatus("подключаюсь к $host…")
        val s = Socket()
        s.tcpNoDelay = true
        s.keepAlive = true
        s.sendBufferSize = 64 * 1024
        s.connect(InetSocketAddress(host, port), 8000)
        sock = s
        val writer = FrameWriter(s.getOutputStream())
        writer.hello(hello)
        val p = ScreenPipe(projection, writer, hello.width, hello.height, dpi, hello.fps, bitrate)
        pipe = p
        p.start()
        connected = true
        onStatus("идёт трансляция на $host")
        scope.launch {
            delay(2500)
            if (!connected) return@launch
            val sent = p.frames
            if (sent == 0L) onStatus("связь есть, кадров нет — снова разреши захват экрана")
            else onStatus("картинка идёт на $host")
        }
        val controls = ControlReader(s.getInputStream())
        while (true) {
            val cmd = controls.next() ?: break
            when (cmd.first) {
                Wire.CMD_KEYFRAME -> p.requestKeyframe()
                Wire.CMD_BITRATE -> p.setBitrate(cmd.second)
                Wire.CMD_FPS -> Unit
                Wire.CMD_STOP -> break
            }
        }
    }
}
