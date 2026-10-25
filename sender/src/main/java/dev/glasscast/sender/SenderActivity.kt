package dev.glasscast.sender

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Bundle
import dev.glasscast.proto.Text as L
import android.util.DisplayMetrics
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import dev.glasscast.proto.Beacon
import dev.glasscast.proto.Fit
import dev.glasscast.proto.Found
import dev.glasscast.proto.Lan
import dev.glasscast.proto.Wire
import kotlinx.coroutines.launch

private val Ink = Color(0xFF071018)
private val Panel = Color(0xFF0E1C24)
private val Line = Color(0xFF1C3A3A)
private val Mint = Color(0xFF35E8A0)
private val Fog = Color(0xFFD7F6EA)
private val Dim = Color(0xFF7AA89A)

class SenderActivity : ComponentActivity() {
    private var lan: Lan? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        lan = Lan(this).also { it.hold() }
        if (Build.VERSION.SDK_INT >= 33) {
            val ask = mutableListOf<String>()
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS)
                != PackageManager.PERMISSION_GRANTED
            ) ask += Manifest.permission.POST_NOTIFICATIONS
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.NEARBY_WIFI_DEVICES)
                != PackageManager.PERMISSION_GRANTED
            ) ask += Manifest.permission.NEARBY_WIFI_DEVICES
            if (ask.isNotEmpty()) requestPermissions(ask.toTypedArray(), 11)
        }
        setContent { MaterialTheme(colorScheme = darkColorScheme(primary = Mint, background = Ink)) { App() } }
    }

    @Composable
    private fun App() {
        val snap by Live.ui.collectAsState()
        val scope = rememberCoroutineScope()
        var glasses by remember { mutableStateOf(listOf<Found>()) }
        var scanning by remember { mutableStateOf(false) }
        var host by remember { mutableStateOf("") }
        var picked by remember { mutableStateOf<Found?>(null) }
        var preset by remember { mutableIntStateOf(1) }
        var fps by remember { mutableIntStateOf(30) }
        var mbps by remember { mutableFloatStateOf(6f) }
        var pending by remember { mutableStateOf<Start?>(null) }

        val capture = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { res ->
            val data = res.data
            val job = pending
            pending = null
            if (res.resultCode != RESULT_OK || data == null || job == null) {
                Live.note(L.t("захват экрана не выдан", "screen capture was denied"))
                return@rememberLauncherForActivityResult
            }
            CastService.start(this, job.host, job.port, job.w, job.h, job.fps, job.bitrate, res.resultCode, data)
        }

        Surface(Modifier.fillMaxSize(), color = Ink) {
            Column(
                Modifier
                    .fillMaxSize()
                    .verticalScroll(rememberScrollState())
                    .padding(20.dp)
            ) {
                Text("GLASSCAST", color = Mint, fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, fontSize = 22.sp)
                Text(L.t("экран телефона → очки", "phone screen → glasses"), color = Dim, fontFamily = FontFamily.Monospace, fontSize = 13.sp)
                Spacer(Modifier.height(16.dp))
                Text(
                    snap.line.ifBlank { L.t("найди очки в той же Wi‑Fi сети", "find the glasses on the same Wi‑Fi") },
                    color = Fog, fontFamily = FontFamily.Monospace, fontSize = 14.sp
                )
                if (snap.running) {
                    Spacer(Modifier.height(8.dp))
                    Text(
                        L.t(
                            "${snap.fps} к/с · ${snap.kbps} кбит/с · кадров ${snap.frames}",
                            "${snap.fps} fps · ${snap.kbps} kbps · frames ${snap.frames}"
                        ),
                        color = Mint,
                        fontFamily = FontFamily.Monospace,
                        fontSize = 13.sp
                    )
                }
                Spacer(Modifier.height(18.dp))

                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    Action(L.t("найти очки", "find glasses"), enabled = !scanning) {
                        scanning = true
                        Live.note(L.t("ищу очки в сети…", "looking for glasses…"))
                        scope.launch {
                            glasses = Beacon.scan(this@SenderActivity, 2800, host)
                            scanning = false
                            if (glasses.isEmpty()) Live.note(L.t("очки не ответили · введи IP с экрана очков", "glasses did not answer · type the IP from the glasses"))
                            else {
                                picked = glasses.first()
                                host = glasses.first().host
                                Live.note(L.t("нашёл ${glasses.size}: ${glasses.first().host}", "found ${glasses.size}: ${glasses.first().host}"))
                            }
                        }
                    }
                    if (snap.running) Action(L.t("стоп", "stop"), fill = false) { CastService.stop(this@SenderActivity) }
                }

                glasses.forEach { g ->
                    val on = picked?.host == g.host
                    Column(
                        Modifier
                            .fillMaxWidth()
                            .padding(top = 10.dp)
                            .border(1.dp, if (on) Mint else Line, RoundedCornerShape(10.dp))
                            .background(Panel, RoundedCornerShape(10.dp))
                            .clickable {
                                picked = g
                                host = g.host
                            }
                            .padding(12.dp)
                    ) {
                        Text(g.name, color = Fog, fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold)
                        Text(L.t("${g.host}:${g.port} · панель ${g.width}×${g.height}", "${g.host}:${g.port} · panel ${g.width}×${g.height}"), color = Dim, fontFamily = FontFamily.Monospace, fontSize = 12.sp)
                    }
                }

                Spacer(Modifier.height(14.dp))
                OutlinedTextField(
                    value = host,
                    onValueChange = { host = it.trim() },
                    label = { Text(L.t("IP очков", "glasses IP")) },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    colors = fieldColors()
                )

                Spacer(Modifier.height(16.dp))
                Text(L.t("картинка", "picture"), color = Dim, fontFamily = FontFamily.Monospace, fontSize = 12.sp)
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Chip(L.t("2× панель", "2× panel"), preset == 0) { preset = 0 }
                    Chip("1280", preset == 1) { preset = 1 }
                    Chip(L.t("экран", "screen"), preset == 2) { preset = 2 }
                }
                Spacer(Modifier.height(10.dp))
                Text(L.t("кадры в секунду", "frames per second"), color = Dim, fontFamily = FontFamily.Monospace, fontSize = 12.sp)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Chip("30", fps == 30) { fps = 30 }
                    Chip("60", fps == 60) { fps = 60 }
                    Chip("90", fps == 90) { fps = 90 }
                }
                Spacer(Modifier.height(10.dp))
                Text(L.t("качество  ${qualityOf(mbps)}", "quality  ${qualityOf(mbps)}"), color = Dim, fontFamily = FontFamily.Monospace, fontSize = 12.sp)
                Slider(
                    value = mbps,
                    onValueChange = { mbps = it },
                    valueRange = 2f..20f,
                    colors = SliderDefaults.colors(thumbColor = Mint, activeTrackColor = Mint)
                )

                Spacer(Modifier.height(8.dp))
                Action(if (snap.running) L.t("перезапустить", "restart") else L.t("транслировать", "stream"), enabled = host.isNotBlank()) {
                    val metrics = DisplayMetrics()
                    @Suppress("DEPRECATION")
                    windowManager.defaultDisplay.getRealMetrics(metrics)
                    val panel = picked
                    val (w, h) = when (preset) {
                        0 -> {
                            val pw = (panel?.width ?: 480).coerceAtLeast(320) * 2
                            val ph = (panel?.height ?: 640).coerceAtLeast(320) * 2
                            Fit.contain(metrics.widthPixels, metrics.heightPixels, pw, ph)
                        }
                        1 -> Fit.limit(metrics.widthPixels, metrics.heightPixels, 1280)
                        else -> Fit.align(metrics.widthPixels) to Fit.align(metrics.heightPixels)
                    }
                    pending = Start(host, picked?.port?.takeIf { it > 0 } ?: Wire.TCP_PORT, w, h, fps, qualityOf(mbps))
                    val mgr = getSystemService(MediaProjectionManager::class.java)
                    capture.launch(mgr.createScreenCaptureIntent())
                }

                Spacer(Modifier.height(18.dp))
                Text(
                    L.t(
                        "телефон и очки — одна Wi‑Fi сеть. на очках открой GlassCast и оставь его на экране. картинка уходит как JPEG, тем же путём, что и с компьютера. одно нажатие поворачивает картинку.",
                        "The phone and the glasses share one Wi‑Fi. Open GlassCast on the glasses and leave it on screen. The picture is JPEG, the same path as the PC. One tap rotates it."
                    ),
                    color = Dim,
                    fontFamily = FontFamily.Monospace,
                    fontSize = 12.sp,
                    lineHeight = 16.sp
                )
            }
        }
    }

    @Composable
    private fun Action(label: String, enabled: Boolean = true, fill: Boolean = true, onClick: () -> Unit) {
        Button(
            onClick = onClick,
            enabled = enabled,
            modifier = if (fill) Modifier.fillMaxWidth() else Modifier,
            colors = ButtonDefaults.buttonColors(containerColor = Mint, contentColor = Ink, disabledContainerColor = Line),
            shape = RoundedCornerShape(10.dp)
        ) { Text(label, fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold) }
    }

    @Composable
    private fun Chip(label: String, on: Boolean, click: () -> Unit) {
        Text(
            label,
            color = if (on) Ink else Fog,
            fontFamily = FontFamily.Monospace,
            modifier = Modifier
                .background(if (on) Mint else Panel, RoundedCornerShape(8.dp))
                .border(1.dp, if (on) Mint else Line, RoundedCornerShape(8.dp))
                .clickable(onClick = click)
                .padding(horizontal = 12.dp, vertical = 8.dp)
        )
    }

    @Composable
    private fun fieldColors() = OutlinedTextFieldDefaults.colors(
        focusedBorderColor = Mint,
        unfocusedBorderColor = Line,
        focusedLabelColor = Mint,
        unfocusedLabelColor = Dim,
        cursorColor = Mint,
        focusedTextColor = Fog,
        unfocusedTextColor = Fog
    )

    private fun qualityOf(mbps: Float): Int = (50 + (mbps - 2f) * 40f / 18f).toInt().coerceIn(50, 90)

    private data class Start(val host: String, val port: Int, val w: Int, val h: Int, val fps: Int, val bitrate: Int)

    override fun onDestroy() {
        lan?.release()
        lan = null
        super.onDestroy()
    }
}
