package dev.glasscast.sender

import android.graphics.Bitmap
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.Image
import android.media.ImageReader
import android.media.projection.MediaProjection
import android.os.Handler
import android.os.HandlerThread
import android.os.Looper
import android.util.Log
import dev.glasscast.proto.FrameWriter
import dev.glasscast.proto.Wire
import java.io.ByteArrayOutputStream
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

private const val TAG = "GlassPipe"

/**
 * Phone screen → JPEG → socket.
 *
 * The glasses already paint Motion-JPEG (that is what the PC sender uses). Hardware H.264 from
 * the phone was accepted as a connection and then produced nothing the panel could show.
 */
class ScreenPipe(
    private val projection: MediaProjection,
    private val writer: FrameWriter,
    private val width: Int,
    private val height: Int,
    private val dpi: Int,
    private val fps: Int,
    bitrateOrQuality: Int
) {
    private var reader: ImageReader? = null
    private var display: VirtualDisplay? = null
    private var thread: HandlerThread? = null
    private var bitmap: Bitmap? = null
    private var pixels: IntArray? = null
    private var rowBuf: ByteArray? = null
    private val jpeg = ByteArrayOutputStream(256 * 1024)
    private val stopped = AtomicBoolean(false)
    private val startNs = System.nanoTime()
    private var lastSentNs = 0L

    @Volatile var quality: Int = qualityOf(bitrateOrQuality)
        private set

    @Volatile var frames = 0L
        private set
    @Volatile var bytes = 0L
        private set

    fun start() {
        val w = width.coerceAtLeast(16) and 1.inv()
        val h = height.coerceAtLeast(16) and 1.inv()
        val ready = CountDownLatch(1)
        var fail: Throwable? = null
        Handler(Looper.getMainLooper()).post {
            try {
                open(w, h)
            } catch (t: Throwable) {
                fail = t
            }
            ready.countDown()
        }
        if (!ready.await(8, TimeUnit.SECONDS)) error("захват экрана не открылся")
        fail?.let { throw it }
    }

    fun requestKeyframe() = Unit

    fun setBitrate(kbps: Int) {
        quality = qualityOf(kbps)
    }

    fun stop() {
        if (!stopped.compareAndSet(false, true)) return
        val disp = display
        val rd = reader
        val th = thread
        display = null
        reader = null
        thread = null
        runCatching { rd?.setOnImageAvailableListener(null, null) }
        runCatching { disp?.release() }
        runCatching { rd?.close() }
        th?.quitSafely()
        runCatching { th?.join(1500) }
        val bmp = bitmap
        bitmap = null
        runCatching { bmp?.recycle() }
    }

    private fun open(w: Int, h: Int) {
        val th = HandlerThread("glass-cap", android.os.Process.THREAD_PRIORITY_DISPLAY).also { it.start() }
        thread = th
        val handler = Handler(th.looper)
        val imageReader = ImageReader.newInstance(w, h, android.graphics.PixelFormat.RGBA_8888, 3)
        reader = imageReader
        imageReader.setOnImageAvailableListener({ src -> onImage(src) }, handler)
        val flags = DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR
        val created = projection.createVirtualDisplay(
            "glasscast",
            w,
            h,
            dpi.coerceAtLeast(120),
            flags,
            imageReader.surface,
            null,
            handler
        )
        if (created == null) {
            imageReader.close()
            reader = null
            th.quitSafely()
            thread = null
            error("система не открыла захват экрана")
        }
        display = created
        Log.i(TAG, "jpeg ${w}x$h @$fps q=$quality")
    }

    private fun onImage(src: ImageReader) {
        if (stopped.get()) return
        val image = runCatching { src.acquireLatestImage() }.getOrNull() ?: return
        try {
            if (stopped.get()) return
            val minGap = 1_000_000_000L / fps.coerceIn(1, 60)
            val now = System.nanoTime()
            if (now - lastSentNs < minGap) return
            val encoded = toJpeg(image) ?: return
            writer.frame(Wire.KIND_KEY, encoded, 0, encoded.size, (now - startNs) / 1000)
            lastSentNs = now
            frames++
            bytes += encoded.size.toLong()
        } catch (e: Exception) {
            Log.w(TAG, "frame", e)
        } finally {
            runCatching { image.close() }
        }
    }

    private fun toJpeg(image: Image): ByteArray? {
        if (image.planes.isEmpty()) return null
        val plane = image.planes[0]
        val buffer = plane.buffer
        val pixelStride = plane.pixelStride.coerceAtLeast(1)
        val rowStride = plane.rowStride
        val w = image.width
        val h = image.height
        if (w < 2 || h < 2 || rowStride < pixelStride * 3) return null
        val argb = pixels?.takeIf { it.size == w * h } ?: IntArray(w * h).also { pixels = it }
        val row = rowBuf?.takeIf { it.size >= rowStride } ?: ByteArray(rowStride).also { rowBuf = it }
        buffer.rewind()
        for (y in 0 until h) {
            val start = y * rowStride
            if (start >= buffer.limit()) return null
            buffer.position(start)
            val n = minOf(row.size, buffer.remaining())
            if (n < pixelStride * w) return null
            buffer.get(row, 0, n)
            var o = 0
            val dst = y * w
            for (x in 0 until w) {
                val r = row[o].toInt() and 0xFF
                val g = row[o + 1].toInt() and 0xFF
                val b = row[o + 2].toInt() and 0xFF
                val a = if (pixelStride > 3) row[o + 3].toInt() and 0xFF else 255
                argb[dst + x] = (a shl 24) or (r shl 16) or (g shl 8) or b
                o += pixelStride
            }
        }
        val bmp = reuse(w, h)
        bmp.setPixels(argb, 0, w, 0, 0, w, h)
        jpeg.reset()
        val ok = bmp.compress(Bitmap.CompressFormat.JPEG, quality.coerceIn(40, 95), jpeg)
        if (!ok || jpeg.size() < 32) return null
        return jpeg.toByteArray()
    }

    private fun reuse(w: Int, h: Int): Bitmap {
        val have = bitmap
        if (have != null && !have.isRecycled && have.width == w && have.height == h) return have
        have?.recycle()
        val created = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
        bitmap = created
        return created
    }

    companion object {
        fun qualityOf(value: Int): Int {
            if (value in 40..100) return value
            val q = 50 + (value.coerceIn(1_000, 24_000) - 1_000) * 40 / 23_000
            return q.coerceIn(50, 90)
        }
    }
}
