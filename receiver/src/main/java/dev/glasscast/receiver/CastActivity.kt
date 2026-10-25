package dev.glasscast.receiver

import android.annotation.SuppressLint
import android.graphics.Color
import android.graphics.Matrix
import android.graphics.SurfaceTexture
import android.os.Bundle
import android.view.GestureDetector
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.Surface
import android.view.TextureView
import android.view.View
import android.view.WindowManager
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import dev.glasscast.proto.Beacon
import dev.glasscast.proto.Hello
import dev.glasscast.proto.Lan
import dev.glasscast.proto.Packet
import dev.glasscast.proto.Wire
import dev.glasscast.receiver.databinding.ActivityCastBinding
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

class CastActivity : AppCompatActivity() {

    private lateinit var ui: ActivityCastBinding
    private var sink: VideoSink? = null
    private var jpeg: JpegSink? = null
    private var videoSurface: Surface? = null
    private var server: CastServer? = null
    private var beacon: Job? = null
    private var ticker: Job? = null
    private var lan: Lan? = null
    private var lastCsd: ByteArray? = null

    @Volatile private var hello: Hello? = null
    @Volatile private var codec = ""
    @Volatile private var streamW = 0
    @Volatile private var streamH = 0
    private var rotation = 0
    private var lastBytes = 0L
    private var lastFrames = 0L
    private var lastRendered = 0L
    private var lastTick = 0L
    private var kbps = 0
    private var inFps = 0
    private var outFps = 0
    private var bitrateKbps = 0

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        ui = ActivityCastBinding.inflate(layoutInflater)
        setContentView(ui.root)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        window.decorView.setBackgroundColor(Color.BLACK)
        hideBars()
        ui.stage.setBackgroundColor(Color.BLACK)
        ui.video.isOpaque = false
        ui.video.surfaceTextureListener = object : TextureView.SurfaceTextureListener {
            override fun onSurfaceTextureAvailable(st: SurfaceTexture, w: Int, h: Int) {
                videoSurface?.release()
                videoSurface = Surface(st)
                hello?.let { h264 ->
                    if (h264.codec != "mjpg") {
                        armH264(h264)
                        server?.requestKeyframe()
                    }
                }
                fitVideo()
            }
            override fun onSurfaceTextureSizeChanged(st: SurfaceTexture, w: Int, h: Int) {
                fitVideo()
            }
            override fun onSurfaceTextureDestroyed(st: SurfaceTexture): Boolean {
                sink?.release()
                sink = null
                videoSurface?.release()
                videoSurface = null
                return true
            }
            override fun onSurfaceTextureUpdated(st: SurfaceTexture) = Unit
        }
        listen()

        val taps = GestureDetector(this, object : GestureDetector.SimpleOnGestureListener() {
            override fun onSingleTapConfirmed(e: MotionEvent): Boolean {
                rotation = (rotation + 90) % 360
                ui.picture.spin = if (codec == "mjpg") rotation else 0
                fitVideo()
                server?.requestKeyframe()
                ui.picture.overlay = "поворот $rotation°"
                ui.picture.invalidate()
                return true
            }
            override fun onDoubleTap(e: MotionEvent): Boolean {
                server?.requestKeyframe()
                return true
            }
        })
        ui.root.setOnTouchListener { _, e -> taps.onTouchEvent(e); true }
    }

    private fun listen() {
        val panel = panelSize()
        val name = android.os.Build.MODEL ?: "glass"
        lan = Lan(this).also {
            it.hold()
            it.advertise(name, Wire.TCP_PORT)
        }
        jpeg = JpegSink(ui.picture)
        val srv = CastServer(
            scope = lifecycleScope,
            onHello = { h ->
                hello = h
                codec = h.codec
                if (h.codec != "mjpg") armH264(h)
                runOnUiThread {
                    streamW = h.width
                    streamH = h.height
                    rotation = 0
                    ui.picture.connected = true
                    ui.picture.cover = h.codec == "mjpg"
                    ui.picture.spin = 0
                    ui.picture.overlay = "${h.name}  ${h.width}x${h.height}"
                    ui.video.visibility = if (h.codec == "mjpg") View.INVISIBLE else View.VISIBLE
                    fitVideo()
                    ui.picture.invalidate()
                }
            },
            onPacket = { p -> onPacket(p) },
            onGone = { who -> runOnUiThread { onGone(who) } }
        )
        server = srv
        srv.start()
        beacon = Beacon.answer(lifecycleScope, name, panel.first, panel.second, 60)
        ticker = lifecycleScope.launch {
            while (isActive) {
                delay(1000)
                tick()
            }
        }
        ui.picture.overlay = "GlassCast\n${ipHint()}:${Wire.TCP_PORT}\nждём источник"
        ui.picture.invalidate()
    }

    @Synchronized
    private fun armH264(h: Hello) {
        val surf = videoSurface ?: return
        sink?.release()
        val decoder = VideoSink(surf) { w, ht ->
            streamW = w
            streamH = ht
            ui.video.post { fitVideo() }
        }
        decoder.start(h.width.coerceAtLeast(16), h.height.coerceAtLeast(16), 0)
        sink = decoder
        lastCsd?.let { decoder.onConfig(it) }
        ui.video.post { fitVideo() }
    }

    private fun fitVideo() {
        val vw = ui.video.width
        val vh = ui.video.height
        if (vw <= 0 || vh <= 0) return
        val videoW = streamW.coerceAtLeast(1)
        val videoH = streamH.coerceAtLeast(1)
        val rw = if (rotation % 180 == 0) videoW else videoH
        val rh = if (rotation % 180 == 0) videoH else videoW
        val scale = minOf(vw / rw.toFloat(), vh / rh.toFloat())
        val dw = rw * scale
        val dh = rh * scale
        val matrix = Matrix()
        if (rotation != 0) matrix.postRotate(rotation.toFloat(), vw / 2f, vh / 2f)
        matrix.postScale(dw / vw, dh / vh, vw / 2f, vh / 2f)
        ui.video.setTransform(matrix)
    }

    private fun onPacket(p: Packet) {
        if (codec == "mjpg") {
            if (!p.isAlive && p.bytes.isNotEmpty()) jpeg?.draw(p.bytes, System.currentTimeMillis())
            return
        }
        if (p.isConfig) lastCsd = p.bytes
        val decoder = sink ?: run {
            hello?.let { if (it.codec != "mjpg") armH264(it) }
            sink
        } ?: return
        when {
            p.isConfig -> decoder.onConfig(p.bytes)
            p.isAlive -> Unit
            else -> decoder.feed(p)
        }
    }

    private fun onGone(who: String) {
        hello = null
        codec = ""
        lastCsd = null
        sink?.release()
        sink = null
        jpeg?.flush()
        ui.picture.clear()
        ui.picture.overlay = "связь с $who разорвана\n${ipHint()}:${Wire.TCP_PORT}\nждём источник"
        ui.video.visibility = View.INVISIBLE
        ui.picture.invalidate()
    }

    private fun tick() {
        val srv = server ?: return
        val decoder = sink
        val pictures = jpeg
        val now = System.currentTimeMillis()
        val span = (now - lastTick).coerceAtLeast(1)
        val rendered = if (codec == "mjpg") pictures?.rendered ?: 0L else decoder?.rendered ?: 0L
        if (lastTick != 0L) {
            kbps = (((srv.bytes - lastBytes) * 8 * 1000) / span / 1000).toInt()
            inFps = (((srv.frames - lastFrames) * 1000) / span).toInt()
            outFps = (((rendered - lastRendered) * 1000) / span).toInt()
        }
        lastTick = now
        lastBytes = srv.bytes
        lastFrames = srv.frames
        lastRendered = rendered
        val h = hello
        if (h != null) {
            ui.picture.overlay = "${h.name}  $inFps→$outFps к/с  $kbps кбит  ${ipHint()}"
            ui.picture.postInvalidate()
        }
    }

    override fun onKeyDown(code: Int, event: KeyEvent?): Boolean {
        val srv = server
        return when (code) {
            KeyEvent.KEYCODE_VOLUME_UP -> {
                bitrateKbps = (if (bitrateKbps == 0) 8000 else bitrateKbps + 2000).coerceAtMost(24000)
                srv?.setBitrate(bitrateKbps)
                true
            }
            KeyEvent.KEYCODE_VOLUME_DOWN -> {
                bitrateKbps = (if (bitrateKbps == 0) 4000 else bitrateKbps - 2000).coerceAtLeast(1000)
                srv?.setBitrate(bitrateKbps)
                true
            }
            else -> super.onKeyDown(code, event)
        }
    }

    private fun panelSize(): Pair<Int, Int> {
        val m = resources.displayMetrics
        return m.widthPixels to m.heightPixels
    }

    private fun ipHint(): String {
        val nifs = runCatching { java.net.NetworkInterface.getNetworkInterfaces().toList() }.getOrNull()
            ?: return "?"
        for (nif in nifs) {
            if (!runCatching { nif.isUp }.getOrDefault(false) || runCatching { nif.isLoopback }.getOrDefault(true)) continue
            for (addr in nif.inetAddresses.toList()) {
                val host = addr.hostAddress ?: continue
                if (!addr.isLoopbackAddress && host.count { it == '.' } == 3) return host
            }
        }
        return "?"
    }

    @SuppressLint("ObsoleteSdkInt")
    private fun hideBars() {
        window.decorView.systemUiVisibility = (
            View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or
                View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
                View.SYSTEM_UI_FLAG_FULLSCREEN or
                View.SYSTEM_UI_FLAG_LAYOUT_STABLE
            )
    }

    override fun onResume() {
        super.onResume()
        hideBars()
    }

    override fun onDestroy() {
        ticker?.cancel()
        beacon?.cancel()
        server?.stop()
        sink?.release()
        jpeg?.release()
        lan?.release()
        videoSurface?.release()
        super.onDestroy()
    }
}
