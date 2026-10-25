package dev.glasscast.sender

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.util.Log
import dev.glasscast.proto.Hello
import kotlinx.coroutines.CoroutineExceptionHandler
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

private const val TAG = "GlassService"
private const val CH = "glasscast"
private const val NOTE = 7

class CastService : Service() {

    private val faults = CoroutineExceptionHandler { _, e ->
        Log.e(TAG, "fault", e)
        Live.note("сбой: ${e.message ?: e.javaClass.simpleName}")
    }
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate + faults)

    private var projection: MediaProjection? = null
    private var client: CastClient? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(
            NotificationChannel(CH, "GlassCast", NotificationManager.IMPORTANCE_LOW)
        )
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopSelf()
            return START_NOT_STICKY
        }
        client?.stop()
        client = null
        runCatching { projection?.stop() }
        projection = null

        val host = intent?.getStringExtra(EXTRA_HOST) ?: return START_NOT_STICKY
        val port = intent.getIntExtra(EXTRA_PORT, 47810)
        val w = intent.getIntExtra(EXTRA_W, 1280)
        val h = intent.getIntExtra(EXTRA_H, 720)
        val fps = intent.getIntExtra(EXTRA_FPS, 60)
        val bitrate = intent.getIntExtra(EXTRA_BITRATE, 8_000_000)
        val extraCode = intent.getIntExtra(EXTRA_CODE, 0)
        val extraData = if (Build.VERSION.SDK_INT >= 33) {
            intent.getParcelableExtra(EXTRA_DATA, Intent::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableExtra(EXTRA_DATA)
        }
        val code = if (extraCode != 0) extraCode else lastCode
        val data = extraData ?: lastData
        if (code == 0 || data == null) {
            Live.note("нет разрешения на захват экрана")
            stopSelf()
            return START_NOT_STICKY
        }

        val note = notification("подключаюсь к $host")
        if (Build.VERSION.SDK_INT >= 29) {
            startForeground(NOTE, note, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION)
        } else {
            startForeground(NOTE, note)
        }

        val mgr = getSystemService(MediaProjectionManager::class.java)
        val proj = runCatching { mgr.getMediaProjection(code, data) }.getOrNull()
        if (proj == null) {
            Live.note("система не выдала захват экрана")
            stopSelf()
            return START_NOT_STICKY
        }
        projection = proj
        proj.registerCallback(object : MediaProjection.Callback() {
            override fun onStop() {
                Live.note("захват экрана остановлен")
                stopSelf()
            }
        }, Handler(Looper.getMainLooper()))

        val dpi = resources.displayMetrics.densityDpi
        val hello = Hello(
            src = "phone",
            name = Build.MODEL ?: "phone",
            width = w,
            height = h,
            fps = fps,
            codec = "mjpg"
        )
        val c = CastClient(scope, proj, host, port, hello, dpi, bitrate) { text ->
            Live.note(text)
            notify(text)
        }
        client = c
        Live.client = c
        Live.running = true
        c.start()
        scope.launch { Live.tick(c) }
        return START_STICKY
    }

    override fun onDestroy() {
        Live.running = false
        Live.client = null
        client?.stop()
        client = null
        runCatching { projection?.stop() }
        projection = null
        scope.cancel()
        super.onDestroy()
    }

    private fun notification(text: String): Notification {
        val stop = PendingIntent.getService(
            this,
            1,
            Intent(this, CastService::class.java).setAction(ACTION_STOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val open = PendingIntent.getActivity(
            this,
            2,
            Intent(this, SenderActivity::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        return Notification.Builder(this, CH)
            .setContentTitle("GlassCast")
            .setContentText(text)
            .setSmallIcon(android.R.drawable.presence_video_online)
            .setContentIntent(open)
            .addAction(Notification.Action.Builder(null, "стоп", stop).build())
            .setOngoing(true)
            .build()
    }

    private fun notify(text: String) {
        getSystemService(NotificationManager::class.java).notify(NOTE, notification(text))
    }

    companion object {
        const val ACTION_STOP = "dev.glasscast.sender.STOP"
        const val EXTRA_HOST = "host"
        const val EXTRA_PORT = "port"
        const val EXTRA_W = "w"
        const val EXTRA_H = "h"
        const val EXTRA_FPS = "fps"
        const val EXTRA_BITRATE = "bitrate"
        const val EXTRA_CODE = "code"
        const val EXTRA_DATA = "data"

        @Volatile var lastCode: Int = 0
        @Volatile var lastData: Intent? = null

        fun start(
            ctx: Context,
            host: String,
            port: Int,
            w: Int,
            h: Int,
            fps: Int,
            bitrate: Int,
            code: Int,
            data: Intent
        ) {
            val copy = Intent(data)
            lastCode = code
            lastData = copy
            val i = Intent(ctx, CastService::class.java)
                .putExtra(EXTRA_HOST, host)
                .putExtra(EXTRA_PORT, port)
                .putExtra(EXTRA_W, w)
                .putExtra(EXTRA_H, h)
                .putExtra(EXTRA_FPS, fps)
                .putExtra(EXTRA_BITRATE, bitrate)
                .putExtra(EXTRA_CODE, code)
                .putExtra(EXTRA_DATA, copy)
            if (Build.VERSION.SDK_INT >= 26) ctx.startForegroundService(i) else ctx.startService(i)
        }

        fun stop(ctx: Context) {
            ctx.startService(Intent(ctx, CastService::class.java).setAction(ACTION_STOP))
        }
    }
}
