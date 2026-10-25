package dev.glasscast.receiver

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.os.Handler
import android.os.HandlerThread
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

class JpegSink(private val stage: StageView) {
    @Volatile var rendered = 0L
        private set
    @Volatile var dropped = 0L
        private set
    @Volatile var lastRenderAt = 0L
        private set
    @Volatile var pipeMs = 0L
        private set

    private val latest = AtomicReference<ByteArray?>(null)
    private val busy = AtomicBoolean(false)
    private val opts = BitmapFactory.Options().apply {
        inPreferredConfig = Bitmap.Config.RGB_565
        inMutable = true
    }
    private val thread = HandlerThread("glass-jpeg").also { it.start() }
    private val handler = Handler(thread.looper)

    fun flush() {
        latest.set(null)
    }

    fun draw(jpeg: ByteArray, arrivedAt: Long) {
        val gen = stage.generation
        val prev = latest.getAndSet(jpeg)
        if (prev != null) dropped++
        if (!busy.compareAndSet(false, true)) return
        handler.post { decode(arrivedAt, gen) }
    }

    private fun decode(arrivedAt: Long, gen: Int) {
        val data = latest.getAndSet(null)
        if (data == null || gen != stage.generation) {
            busy.set(false)
            return
        }
        opts.inSampleSize = 1
        var bmp = runCatching { BitmapFactory.decodeByteArray(data, 0, data.size, opts) }.getOrNull()
        if (bmp == null) {
            val plain = BitmapFactory.Options().apply { inPreferredConfig = Bitmap.Config.ARGB_8888 }
            bmp = runCatching { BitmapFactory.decodeByteArray(data, 0, data.size, plain) }.getOrNull()
        }
        if (bmp == null) {
            dropped++
            busy.set(false)
            val more = latest.get()
            if (more != null && gen == stage.generation) draw(more, System.currentTimeMillis())
            return
        }
        rendered++
        lastRenderAt = System.currentTimeMillis()
        pipeMs = (lastRenderAt - arrivedAt).coerceAtLeast(0)
        stage.post {
            if (gen != stage.generation) bmp.recycle()
            else stage.show(bmp)
            busy.set(false)
            val more = latest.get()
            if (more != null && gen == stage.generation) draw(more, System.currentTimeMillis())
        }
    }

    fun release() {
        latest.set(null)
        thread.quitSafely()
    }
}
