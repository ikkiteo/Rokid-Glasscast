package dev.glasscast.proto

import java.io.DataInputStream
import java.io.EOFException
import java.io.InputStream
import java.io.OutputStream
import org.json.JSONObject

/** Ports and sizes shared by every GlassCast peer. See PROTOCOL.md. */
object Wire {
    const val TCP_PORT = 47810
    const val UDP_PORT = 47811
    const val PROBE = "GCASTQ?\n"
    const val MAGIC = "GCS1"

    const val KIND_CONFIG = 0
    const val KIND_KEY = 1
    const val KIND_DELTA = 2
    const val KIND_ALIVE = 3

    const val CMD_KEYFRAME = 1
    const val CMD_BITRATE = 2
    const val CMD_FPS = 3
    const val CMD_STOP = 4

    /** Biggest frame we will ever believe in; a larger claim means a desynced stream. */
    const val MAX_FRAME = 4 shl 20
}

data class Hello(
    val src: String,
    val name: String,
    val width: Int,
    val height: Int,
    val fps: Int,
    val codec: String = "h264",
    val rotate: Int = 0
) {
    fun toJson(): String = JSONObject()
        .put("src", src)
        .put("name", name)
        .put("w", width)
        .put("h", height)
        .put("fps", fps)
        .put("codec", codec)
        .put("rotate", rotate)
        .toString()

    companion object {
        fun parse(json: String): Hello {
            val o = JSONObject(json)
            return Hello(
                src = o.optString("src", "unknown"),
                name = o.optString("name", "sender"),
                width = o.optInt("w", 0),
                height = o.optInt("h", 0),
                fps = o.optInt("fps", 60),
                codec = o.optString("codec", "h264"),
                rotate = o.optInt("rotate", 0)
            )
        }
    }
}

class FrameWriter(private val out: OutputStream) {
    private val head = ByteArray(13)

    @Synchronized
    fun frame(kind: Int, payload: ByteArray, offset: Int, length: Int, ptsUs: Long) {
        head[0] = kind.toByte()
        putInt(head, 1, length)
        putLong(head, 5, ptsUs)
        out.write(head)
        if (length > 0) out.write(payload, offset, length)
        out.flush()
    }

    @Synchronized
    fun keepAlive(ptsUs: Long) = frame(Wire.KIND_ALIVE, EMPTY, 0, 0, ptsUs)

    @Synchronized
    fun hello(hello: Hello) {
        val json = hello.toJson().toByteArray(Charsets.UTF_8)
        out.write(Wire.MAGIC.toByteArray(Charsets.US_ASCII))
        out.write(json.size and 0xFF)
        out.write((json.size shr 8) and 0xFF)
        out.write(json)
        out.flush()
    }

    private companion object {
        val EMPTY = ByteArray(0)

        fun putInt(b: ByteArray, at: Int, v: Int) {
            b[at] = (v and 0xFF).toByte()
            b[at + 1] = ((v shr 8) and 0xFF).toByte()
            b[at + 2] = ((v shr 16) and 0xFF).toByte()
            b[at + 3] = ((v shr 24) and 0xFF).toByte()
        }

        fun putLong(b: ByteArray, at: Int, v: Long) {
            for (i in 0 until 8) b[at + i] = ((v shr (8 * i)) and 0xFF).toByte()
        }
    }
}

class FrameReader(stream: InputStream) {
    private val input = DataInputStream(stream.buffered(1 shl 16))
    private val head = ByteArray(13)

    fun hello(): Hello {
        val magic = ByteArray(4)
        input.readFully(magic)
        if (String(magic, Charsets.US_ASCII) != Wire.MAGIC) error(Text.t("чужой поток: ${String(magic)}", "unknown stream: ${String(magic)}"))
        val lo = input.read()
        val hi = input.read()
        if (lo < 0 || hi < 0) throw EOFException()
        val len = (hi shl 8) or lo
        if (len <= 0 || len > 4096) error(Text.t("плохая длина приветствия: $len", "bad hello length: $len"))
        val json = ByteArray(len)
        input.readFully(json)
        return Hello.parse(String(json, Charsets.UTF_8))
    }

    /** Reads one frame into a fresh array. Returns null at end of stream. */
    fun next(): Packet? {
        var read = 0
        while (read < head.size) {
            val n = input.read(head, read, head.size - read)
            if (n < 0) return null
            read += n
        }
        val kind = head[0].toInt() and 0xFF
        val size = int(head, 1)
        val pts = long(head, 5)
        if (size < 0 || size > Wire.MAX_FRAME) error(Text.t("битый кадр: $size байт", "broken frame: $size bytes"))
        val body = if (size == 0) EMPTY else ByteArray(size).also { input.readFully(it) }
        return Packet(kind, body, pts)
    }

    private companion object {
        val EMPTY = ByteArray(0)

        fun int(b: ByteArray, at: Int): Int =
            (b[at].toInt() and 0xFF) or
                ((b[at + 1].toInt() and 0xFF) shl 8) or
                ((b[at + 2].toInt() and 0xFF) shl 16) or
                ((b[at + 3].toInt() and 0xFF) shl 24)

        fun long(b: ByteArray, at: Int): Long {
            var v = 0L
            for (i in 7 downTo 0) v = (v shl 8) or (b[at + i].toLong() and 0xFF)
            return v
        }
    }
}

data class Packet(val kind: Int, val bytes: ByteArray, val ptsUs: Long) {
    val isConfig get() = kind == Wire.KIND_CONFIG
    val isKey get() = kind == Wire.KIND_KEY
    val isAlive get() = kind == Wire.KIND_ALIVE

    override fun equals(other: Any?): Boolean = this === other
    override fun hashCode(): Int = System.identityHashCode(this)
}

class ControlWriter(private val out: OutputStream) {
    @Synchronized
    fun send(cmd: Int, arg: Int = 0) {
        val b = ByteArray(5)
        b[0] = cmd.toByte()
        b[1] = (arg and 0xFF).toByte()
        b[2] = ((arg shr 8) and 0xFF).toByte()
        b[3] = ((arg shr 16) and 0xFF).toByte()
        b[4] = ((arg shr 24) and 0xFF).toByte()
        out.write(b)
        out.flush()
    }
}

class ControlReader(stream: InputStream) {
    private val input = DataInputStream(stream)

    /** Blocks for the next command. Returns null when the peer hung up. */
    fun next(): Pair<Int, Int>? {
        val b = ByteArray(5)
        var read = 0
        while (read < b.size) {
            val n = input.read(b, read, b.size - read)
            if (n < 0) return null
            read += n
        }
        val arg = (b[1].toInt() and 0xFF) or
            ((b[2].toInt() and 0xFF) shl 8) or
            ((b[3].toInt() and 0xFF) shl 16) or
            ((b[4].toInt() and 0xFF) shl 24)
        return (b[0].toInt() and 0xFF) to arg
    }
}
