package dev.glasscast.receiver

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.content.Context
import android.util.AttributeSet
import android.view.View
import java.util.concurrent.atomic.AtomicReference

/**
 * JPEG blit. Letterbox around the picture is pure black so the glasses emit no light there.
 * H.264 is a TextureView underneath; this view stays transparent while that path is active.
 */
class StageView @JvmOverloads constructor(ctx: Context, attrs: AttributeSet? = null) : View(ctx, attrs) {

    @Volatile var overlay = "GlassCast\nждём подключения"
    @Volatile var connected = false
    @Volatile var cover = true
    @Volatile var spin = 0
    @Volatile var generation = 0
        private set

    private val pending = AtomicReference<Bitmap?>(null)
    private var frame: Bitmap? = null
    private val dst = Rect()
    private val src = Rect()
    private val wait = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = 0xFFE8F6FF.toInt()
        textSize = 34f
        textAlign = Paint.Align.CENTER
        typeface = android.graphics.Typeface.MONOSPACE
    }
    private val blit = Paint(Paint.FILTER_BITMAP_FLAG)

    fun show(bmp: Bitmap) {
        val old = pending.getAndSet(bmp)
        if (old != null && old !== bmp) old.recycle()
        postInvalidate()
    }

    fun clear() {
        generation++
        pending.getAndSet(null)?.recycle()
        frame?.recycle()
        frame = null
        connected = false
        cover = true
        spin = 0
        overlay = "GlassCast\nждём подключения"
        postInvalidate()
    }

    override fun onDraw(canvas: Canvas) {
        val next = pending.getAndSet(null)
        if (next != null) {
            val old = frame
            frame = next
            if (old != null && old !== next) old.recycle()
        }
        if (cover) canvas.drawColor(Color.BLACK)
        val bmp = frame
        if (bmp != null && !bmp.isRecycled) {
            val rot = spin
            if (rot % 180 != 0) {
                canvas.save()
                canvas.rotate(rot.toFloat(), width / 2f, height / 2f)
                src.set(0, 0, bmp.width, bmp.height)
                fit(bmp.width, bmp.height, height, width, dst)
                val dx = (width - height) / 2
                val dy = (height - width) / 2
                dst.offset(dx, dy)
                canvas.drawBitmap(bmp, src, dst, blit)
                canvas.restore()
            } else {
                src.set(0, 0, bmp.width, bmp.height)
                fit(bmp.width, bmp.height, width, height, dst)
                if (rot != 0) {
                    canvas.save()
                    canvas.rotate(rot.toFloat(), width / 2f, height / 2f)
                    canvas.drawBitmap(bmp, src, dst, blit)
                    canvas.restore()
                } else canvas.drawBitmap(bmp, src, dst, blit)
            }
        } else if (cover) {
            val lines = overlay.split('\n')
            val cx = width / 2f
            var y = height / 2f - (lines.size - 1) * 26f
            for (line in lines) {
                canvas.drawText(line, cx, y, wait)
                y += 40f
            }
        }
    }

    companion object {
        fun fit(bw: Int, bh: Int, vw: Int, vh: Int, out: Rect) {
            val s = minOf(vw / bw.toFloat(), vh / bh.toFloat())
            val dw = (bw * s).toInt().coerceAtLeast(1)
            val dh = (bh * s).toInt().coerceAtLeast(1)
            val x = (vw - dw) / 2
            val y = (vh - dh) / 2
            out.set(x, y, x + dw, y + dh)
        }
    }
}
