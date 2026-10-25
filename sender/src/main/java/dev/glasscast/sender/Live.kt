package dev.glasscast.sender

import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.coroutineScope

object Live {
    private val _ui = MutableStateFlow(Snap())
    val ui = _ui.asStateFlow()

    @Volatile var running = false
    @Volatile var client: CastClient? = null

    fun note(text: String) {
        _ui.value = _ui.value.copy(line = text)
    }

    suspend fun tick(c: CastClient) = coroutineScope {
        var lastF = 0L
        var lastB = 0L
        var lastT = System.currentTimeMillis()
        while (isActive && running) {
            delay(500)
            val p = c.pipe
            val now = System.currentTimeMillis()
            val span = (now - lastT).coerceAtLeast(1)
            val f = p?.frames ?: 0L
            val b = p?.bytes ?: 0L
            val fps = ((f - lastF) * 1000 / span).toInt()
            val kbps = ((b - lastB) * 8 * 1000 / span / 1000).toInt()
            lastF = f
            lastB = b
            lastT = now
            _ui.value = _ui.value.copy(
                running = true,
                connected = c.connected,
                fps = fps,
                kbps = kbps,
                frames = f
            )
        }
        _ui.value = _ui.value.copy(running = false, connected = false, fps = 0, kbps = 0)
    }
}

data class Snap(
    val line: String = "найди очки в той же Wi‑Fi сети",
    val running: Boolean = false,
    val connected: Boolean = false,
    val fps: Int = 0,
    val kbps: Int = 0,
    val frames: Long = 0
)
