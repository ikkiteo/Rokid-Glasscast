package dev.glasscast.receiver

import android.graphics.ColorMatrix
import android.graphics.ColorMatrixColorFilter
import android.graphics.Paint
import android.media.MediaCodec
import android.media.MediaFormat
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.util.Log
import android.view.Surface
import dev.glasscast.proto.Packet
import java.nio.ByteBuffer
import java.util.ArrayDeque

private const val TAG = "GlassSink"

/** Hardware H.264 → Surface. Black lift is done by the TextureView color matrix, not here. */
class VideoSink(private val surface: Surface, private val onSize: (Int, Int) -> Unit) {

    private val lock = Any()
    private val pending = ArrayDeque<Packet>()
    private val freeInputs = ArrayDeque<Int>()
    private var codec: MediaCodec? = null
    private var thread: HandlerThread? = null
    private var handler: Handler? = null
    private var csd: ByteArray? = null
    private var width = 16
    private var height = 16
    private var rotation = 0

    @Volatile var rendered = 0L
        private set
    @Volatile var dropped = 0L
        private set
    @Volatile var lastRenderAt = 0L
        private set
    @Volatile var pipeMs = 0L
        private set

    fun start(width: Int, height: Int, rotation: Int) {
        this.width = width.coerceAtLeast(16)
        this.height = height.coerceAtLeast(16)
        this.rotation = rotation
        val th = HandlerThread("glass-dec", Thread.MAX_PRIORITY).also { it.start() }
        thread = th
        handler = Handler(th.looper)
    }

    fun setRotation(degrees: Int) {
        rotation = ((degrees % 360) + 360) % 360
    }

    fun rotation(): Int = rotation

    fun onConfig(bytes: ByteArray) {
        if (bytes.isEmpty()) return
        if (csd?.contentEquals(bytes) == true && codec != null) return
        csd = bytes
        val keep = ArrayList<Packet>()
        synchronized(lock) {
            while (pending.isNotEmpty()) keep += pending.removeFirst()
            freeInputs.clear()
        }
        stopCodec()
        synchronized(lock) { keep.forEach { pending.addLast(it) } }
        configure(bytes)
    }

    fun feed(packet: Packet) {
        if (codec == null && csd == null && packet.bytes.isNotEmpty()) {
            onConfig(packet.bytes)
            if (packet.isConfig) return
        }
        synchronized(lock) {
            while (pending.size >= 2) {
                val old = pending.peekFirst() ?: break
                if (old.isKey) break
                pending.pollFirst()
                dropped++
            }
            pending.addLast(packet)
        }
        handler?.post { pump() }
    }

    fun release() {
        stopCodec()
        thread?.quitSafely()
        thread = null
        handler = null
    }

    private fun configure(config: ByteArray) {
        val h = handler ?: return
        h.post {
            runCatching {
                val format = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, width, height)
                val (sps, pps) = splitCsd(config)
                format.setByteBuffer("csd-0", ByteBuffer.wrap(sps))
                if (pps != null) format.setByteBuffer("csd-1", ByteBuffer.wrap(pps))
                if (Build.VERSION.SDK_INT >= 30) format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
                format.setInteger(MediaFormat.KEY_PRIORITY, 0)
                format.setInteger(MediaFormat.KEY_OPERATING_RATE, 120)
                val dec = MediaCodec.createDecoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
                dec.setCallback(callback, h)
                dec.configure(format, surface, null, 0)
                dec.start()
                codec = dec
                Log.i(TAG, "decoder surface ${width}x$height")
                pump()
            }.onFailure { Log.e(TAG, "configure", it) }
        }
    }

    private val callback = object : MediaCodec.Callback() {
        override fun onInputBufferAvailable(mc: MediaCodec, index: Int) {
            synchronized(lock) { freeInputs.addLast(index) }
            pump()
        }

        override fun onOutputBufferAvailable(mc: MediaCodec, index: Int, info: MediaCodec.BufferInfo) {
            runCatching {
                mc.releaseOutputBuffer(index, info.size > 0)
                if (info.size > 0) {
                    rendered++
                    lastRenderAt = System.currentTimeMillis()
                }
            }
        }

        override fun onOutputFormatChanged(mc: MediaCodec, format: MediaFormat) {
            val w = runCatching { format.getInteger(MediaFormat.KEY_WIDTH) }.getOrDefault(width)
            val ht = runCatching { format.getInteger(MediaFormat.KEY_HEIGHT) }.getOrDefault(height)
            if (w > 0 && ht > 0) onSize(w, ht)
        }

        override fun onError(mc: MediaCodec, e: MediaCodec.CodecException) {
            Log.e(TAG, "codec ${e.diagnosticInfo}", e)
            val keep = csd
            stopCodec()
            if (keep != null) configure(keep)
        }
    }

    private fun pump() {
        val dec = codec ?: return
        while (true) {
            val pair: Pair<Int, Packet>? = synchronized(lock) {
                val i = freeInputs.pollFirst()
                val p = pending.pollFirst()
                if (i == null || p == null) {
                    if (i != null) freeInputs.addFirst(i)
                    if (p != null) pending.addFirst(p)
                    null
                } else i to p
            }
            val (index, p) = pair ?: return
            val ok = runCatching {
                val buf = dec.getInputBuffer(index) ?: return@runCatching false
                buf.clear()
                buf.put(p.bytes)
                val flags = if (p.isConfig) MediaCodec.BUFFER_FLAG_CODEC_CONFIG else 0
                dec.queueInputBuffer(index, 0, p.bytes.size, p.ptsUs, flags)
                true
            }.getOrDefault(false)
            if (!ok) return
        }
    }

    private fun stopCodec() {
        val dec = codec ?: return
        codec = null
        synchronized(lock) { freeInputs.clear() }
        runCatching { dec.stop() }
        runCatching { dec.release() }
    }

    companion object {
        fun splitCsd(data: ByteArray): Pair<ByteArray, ByteArray?> {
            val nals = nals(data)
            val sps = nals.firstOrNull { (it[0].toInt() and 0x1F) == 7 } ?: data
            val pps = nals.firstOrNull { (it[0].toInt() and 0x1F) == 8 }
            return start(sps) to pps?.let { start(it) }
        }

        private fun start(nal: ByteArray): ByteArray {
            val out = ByteArray(4 + nal.size)
            out[3] = 1
            System.arraycopy(nal, 0, out, 4, nal.size)
            return out
        }

        private fun nals(data: ByteArray): List<ByteArray> {
            val out = ArrayList<ByteArray>()
            var i = 0
            fun at(p: Int) = data[p].toInt() and 0xFF
            while (i + 3 < data.size) {
                val sc = when {
                    at(i) == 0 && at(i + 1) == 0 && at(i + 2) == 1 -> 3
                    i + 4 <= data.size && at(i) == 0 && at(i + 1) == 0 && at(i + 2) == 0 && at(i + 3) == 1 -> 4
                    else -> 0
                }
                if (sc == 0) { i++; continue }
                val from = i + sc
                var j = from
                var next = data.size
                while (j + 3 < data.size) {
                    val n = when {
                        at(j) == 0 && at(j + 1) == 0 && at(j + 2) == 1 -> 3
                        j + 4 <= data.size && at(j) == 0 && at(j + 1) == 0 && at(j + 2) == 0 && at(j + 3) == 1 -> 4
                        else -> 0
                    }
                    if (n != 0) { next = j; break }
                    j++
                }
                if (from < next) out += data.copyOfRange(from, next)
                i = next
            }
            return out
        }

        fun liftPaint(): Paint {
            val p = Paint()
            p.colorFilter = ColorMatrixColorFilter(
                ColorMatrix(
                    floatArrayOf(
                        1f, 0f, 0f, 0f, 48f,
                        0f, 1f, 0f, 0f, 48f,
                        0f, 0f, 1f, 0f, 48f,
                        0f, 0f, 0f, 1f, 0f
                    )
                )
            )
            return p
        }
    }
}
