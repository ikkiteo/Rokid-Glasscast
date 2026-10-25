package dev.glasscast.proto

/** Encoder-friendly geometry: every hardware H.264 encoder wants even, ideally 16-aligned sides. */
object Fit {
    fun align(value: Int, step: Int = 16): Int {
        val v = (value / step) * step
        return if (v < step) step else v
    }

    /**
     * Largest size with the aspect of [srcW]x[srcH] that fits inside [maxW]x[maxH].
     * Both sides come back 16-aligned so the encoder never pads behind our back.
     */
    fun contain(srcW: Int, srcH: Int, maxW: Int, maxH: Int): Pair<Int, Int> {
        if (srcW <= 0 || srcH <= 0) return align(maxW) to align(maxH)
        val scale = minOf(maxW.toFloat() / srcW, maxH.toFloat() / srcH)
        val w = align((srcW * scale).toInt())
        val h = align((srcH * scale).toInt())
        return w to h
    }

    /** Scale down so the longer side is at most [maxLong]. Both sides stay 16-aligned. */
    fun limit(srcW: Int, srcH: Int, maxLong: Int): Pair<Int, Int> {
        if (srcW <= 0 || srcH <= 0) return align(maxLong) to align(maxLong)
        val scale = minOf(1f, maxLong.toFloat() / maxOf(srcW, srcH))
        return align((srcW * scale).toInt()) to align((srcH * scale).toInt())
    }

    /** Panel is portrait but the picture is landscape: rotating fills far more of the glass. */
    fun wantsRotation(srcW: Int, srcH: Int, panelW: Int, panelH: Int): Boolean {
        val srcLandscape = srcW >= srcH
        val panelLandscape = panelW >= panelH
        return srcLandscape != panelLandscape
    }
}
