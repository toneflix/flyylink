package com.toneflix.flyylink

import androidx.annotation.DrawableRes
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.format.Formatter
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.toneflix.flyylink.protocol.FlyyDrop
import com.toneflix.flyylink.protocol.PairState
import com.toneflix.flyylink.protocol.TransferProgress
import com.toneflix.flyylink.ui.FlyyLinkTheme
import com.toneflix.flyylink.ui.DeviceArt
import com.toneflix.flyylink.ui.DeviceBadge
import com.toneflix.flyylink.ui.PlayOnTvSection
import com.toneflix.flyylink.ui.Radar
import kotlinx.coroutines.delay

/** The share sheet target: sends files, links or text from any app to a paired Mac, or FlyyDrops them to a device nearby. */
class ShareActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val outgoing = outgoing(intent)
        if (outgoing == null) {
            finish()
            return
        }

        // Shared from another app while FlyyLink wasn't running: connect now.
        LinkService.start(this)

        setContent {
            FlyyLinkTheme { ShareSheet(outgoing, target = intent.getStringExtra(EXTRA_TARGET), onClose = ::finish) }
        }
    }

    private fun outgoing(intent: Intent): Outgoing? {
        val uris = when (intent.action) {
            Intent.ACTION_SEND -> listOfNotNull(intent.stream())
            Intent.ACTION_SEND_MULTIPLE -> intent.streams()
            else -> return null
        }

        if (uris.isNotEmpty()) {
            val items = ShareSender.describe(this, uris)
            return if (items.isEmpty()) null else Outgoing.Files(items)
        }

        val text = intent.getStringExtra(Intent.EXTRA_TEXT)?.trim()
        return if (text.isNullOrEmpty()) null else Outgoing.Text(text)
    }

    @Suppress("DEPRECATION")
    private fun Intent.stream(): Uri? =
        if (Build.VERSION.SDK_INT >= 33) getParcelableExtra(Intent.EXTRA_STREAM, Uri::class.java) else getParcelableExtra(Intent.EXTRA_STREAM)

    @Suppress("DEPRECATION")
    private fun Intent.streams(): List<Uri> =
        (if (Build.VERSION.SDK_INT >= 33) getParcelableArrayListExtra(Intent.EXTRA_STREAM, Uri::class.java) else getParcelableArrayListExtra(Intent.EXTRA_STREAM))
            .orEmpty()

    companion object {
        private const val EXTRA_TARGET = "com.toneflix.flyylink.TARGET"

        /** Opens the sheet to send files picked inside FlyyLink, starting right away when a [target] device is given. */
        fun sendFilesIntent(context: Context, uris: List<Uri>, target: String? = null): Intent =
            Intent(context, ShareActivity::class.java)
                .setAction(Intent.ACTION_SEND_MULTIPLE)
                .putParcelableArrayListExtra(Intent.EXTRA_STREAM, ArrayList(uris))
                .putExtra(EXTRA_TARGET, target)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
    }
}

sealed interface Outgoing {
    data class Files(val items: List<ShareSender.Item>) : Outgoing {
        val totalSize: Long get() = items.sumOf { it.size }
        val allImages: Boolean get() = items.all { it.name.substringAfterLast('.').lowercase() in IMAGE_EXTENSIONS }
    }

    data class Text(val text: String) : Outgoing

    companion object {
        private val IMAGE_EXTENSIONS = setOf("jpg", "jpeg", "png", "gif", "webp", "heic", "heif")
    }
}

/** How far a send to one Mac has got. */
data class SendProgress(val reading: TransferProgress.Snapshot, val done: Boolean = false, val failed: Boolean = false) {
    val fraction: Float get() = reading.fraction.toFloat()

    companion object {
        fun finished(total: Long, failed: Boolean = false) =
            SendProgress(TransferProgress.Snapshot(total, total, null, null), done = true, failed = failed)
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ShareSheet(outgoing: Outgoing, target: String?, onClose: () -> Unit) {
    val context = LocalContext.current
    val devices by DeviceStore.states.collectAsStateWithLifecycle()
    val focusId by DeviceStore.focusId.collectAsStateWithLifecycle()
    val flyyDrops by FlyyDropPlugin.sends.collectAsStateWithLifecycle()
    val macs = devices.filter { it.pairState == PairState.PAIRED }.sortedByDescending { it.id == focusId }
    val nearby = devices.filter { it.pairState != PairState.PAIRED && it.canFlyyDrop }
    val progress = remember { mutableStateMapOf<String, SendProgress>() }
    val state = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    val castItem = remember(outgoing) { (outgoing as? Outgoing.Files)?.items?.singleOrNull()?.let { CastItem.of(context, it) } }
    val tvs by PlayOnTv.tvs.collectAsStateWithLifecycle()

    val flyyDrop = { device: DeviceState ->
        if (flyyDrops[device.id]?.isFinished != false) {
            when (outgoing) {
                is Outgoing.Files -> FlyyDropPlugin.send(device.id, outgoing.items)
                is Outgoing.Text -> FlyyDropPlugin.send(device.id, emptyList(), outgoing.text)
            }
        }
    }

    val send = { mac: DeviceState ->
        if (mac.id !in progress) {
            when (outgoing) {
                is Outgoing.Files -> startSending(context, mac, outgoing, progress)
                is Outgoing.Text -> {
                    ShareSender.sendText(context, mac.id, outgoing.text)
                    progress[mac.id] = SendProgress.finished(1)
                }
            }
        }
    }

    LaunchedEffect(Unit) { FlyyDropPlugin.clearFinished() }

    LaunchedEffect(castItem) { if (castItem != null) PlayOnTv.search() }

    // With one device to pick, or one picked already, there is nothing to choose, so start right away.
    LaunchedEffect(macs.size, nearby.size) {
        if (progress.isNotEmpty() || flyyDrops.values.any { !it.isFinished }) return@LaunchedEffect

        when {
            target != null -> macs.firstOrNull { it.id == target }?.let(send) ?: nearby.firstOrNull { it.id == target }?.let(flyyDrop)
            macs.size == 1 && nearby.isEmpty() && (castItem == null || tvs.isEmpty()) -> send(macs.first())
        }
    }

    // Close shortly after everything finished.
    val started = progress.values.map { it.done } + flyyDrops.values.map { it.isFinished }
    val allDone = started.isNotEmpty() && started.all { it }
    LaunchedEffect(allDone) {
        if (!allDone) return@LaunchedEffect

        val failed = progress.values.any { it.failed } || flyyDrops.values.any { it.stage != FlyyDropSend.Stage.SENT }
        Toast.makeText(context, if (failed) R.string.share_send_failed_toast else R.string.share_sent_toast, Toast.LENGTH_SHORT).show()
        delay(900)
        onClose()
    }

    ModalBottomSheet(onDismissRequest = onClose, sheetState = state, containerColor = MaterialTheme.colorScheme.surface) {
        Column(
            Modifier
                .fillMaxWidth()
                .navigationBarsPadding()
                .padding(start = 20.dp, end = 20.dp, bottom = 28.dp),
            verticalArrangement = Arrangement.spacedBy(18.dp),
        ) {
            Summary(outgoing)

            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    stringResource(R.string.share_send_to),
                    fontSize = 14.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.weight(1f),
                )
                if (macs.size > 1 && macs.any { it.id !in progress }) {
                    TextButton(onClick = { macs.forEach(send) }) {
                        Text(stringResource(R.string.share_send_all), fontWeight = FontWeight.SemiBold)
                    }
                }
            }

            if (macs.isEmpty() && nearby.isEmpty()) {
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                    CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                    Text(stringResource(R.string.share_looking), fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }

            macs.forEach { mac -> MacRow(mac, progress[mac.id], outgoing) { send(mac) } }

            if (nearby.isNotEmpty()) {
                NearbyTitle()
                nearby.forEach { device ->
                    NearbyRow(device, flyyDrops[device.id], onClick = { flyyDrop(device) }, onCancel = { FlyyDropPlugin.cancelSend(device.id) })
                }
            }

            if (tvs.isNotEmpty()) castItem?.let { PlayOnTvSection(it, onStarted = onClose) }

            OutlinedButton(
                onClick = onClose,
                modifier = Modifier.fillMaxWidth().height(52.dp),
                border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
            ) {
                val label = if (progress.values.any { !it.done } || flyyDrops.values.any { it.stage == FlyyDropSend.Stage.SENDING }) R.string.share_hide else R.string.cancel
                Text(stringResource(label), fontSize = 15.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface)
            }
        }
    }
}

private fun startSending(context: Context, mac: DeviceState, files: Outgoing.Files, progress: MutableMap<String, SendProgress>) {
    val main = Handler(Looper.getMainLooper())

    progress[mac.id] = SendProgress(TransferProgress.Snapshot(0, files.totalSize, null, null))

    ShareSender.sendFiles(
        context,
        mac.id,
        files.items,
        onProgress = { reading -> main.post { progress[mac.id] = SendProgress(reading) } },
        onDone = { error -> main.post { progress[mac.id] = SendProgress.finished(files.totalSize, failed = error != null) } },
    )
}

@Composable
private fun Summary(outgoing: Outgoing) {
    val context = LocalContext.current
    val (icon, title, subtitle) = when (outgoing) {
        is Outgoing.Files -> Triple(
            if (outgoing.allImages) R.drawable.lucide_image else R.drawable.lucide_file_text,
            when {
                outgoing.items.size == 1 -> outgoing.items.first().name
                outgoing.allImages -> pluralStringResource(R.plurals.share_photos, outgoing.items.size, outgoing.items.size)
                else -> pluralStringResource(R.plurals.share_files, outgoing.items.size, outgoing.items.size)
            },
            Formatter.formatShortFileSize(context, outgoing.totalSize),
        )
        is Outgoing.Text -> Triple(R.drawable.lucide_link, outgoing.text.lineSequence().first(), stringResource(R.string.share_text_summary))
    }

    Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(
            Modifier.size(52.dp).background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(14.dp)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(painterResource(icon), contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Column(Modifier.weight(1f)) {
            Text(title, fontSize = 17.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(subtitle, fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun MacRow(mac: DeviceState, progress: SendProgress?, outgoing: Outgoing, onClick: () -> Unit) {
    val active = progress != null
    val shape = RoundedCornerShape(22.dp)

    Row(
        Modifier
            .fillMaxWidth()
            .background(if (active) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceContainerLow, shape)
            .then(if (active) Modifier.border(2.dp, MaterialTheme.colorScheme.primary, shape) else Modifier)
            .clickable(enabled = !active, onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 14.dp),
        horizontalArrangement = Arrangement.spacedBy(14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        DeviceArt(mac.type, 44.dp)

        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(mac.name, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                when {
                    progress == null -> Unit
                    progress.failed -> StatusIcon(R.drawable.lucide_circle_alert, MaterialTheme.colorScheme.error)
                    progress.done -> StatusIcon(R.drawable.lucide_circle_check, MaterialTheme.colorScheme.primary)
                    else -> Text("${(progress.fraction * 100).toInt()}%", fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onPrimaryContainer)
                }
            }

            if (progress != null && !progress.done) {
                LinearProgressIndicator(
                    progress = { progress.fraction },
                    modifier = Modifier.fillMaxWidth().height(6.dp),
                    trackColor = MaterialTheme.colorScheme.primary.copy(alpha = 0.2f),
                )
                if (outgoing is Outgoing.Files) Text(progressDetail(progress, outgoing), fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else if (progress == null) {
                Text(stringResource(R.string.share_tap_to_send), fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

/** The "FlyyDrop to nearby" heading. */
@Composable
private fun NearbyTitle() {
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
        Icon(painterResource(R.drawable.lucide_radar), contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(16.dp))
        Text(stringResource(R.string.flyydrop_nearby), fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

/** A device that isn't paired, which takes files once its owner accepts. */
@Composable
private fun NearbyRow(device: DeviceState, send: FlyyDropSend?, onClick: () -> Unit, onCancel: () -> Unit) {
    val active = send != null && !send.isFinished
    val shape = RoundedCornerShape(22.dp)

    Row(
        Modifier
            .fillMaxWidth()
            .background(if (active) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceContainerLow, shape)
            .then(if (active) Modifier.border(2.dp, MaterialTheme.colorScheme.primary, shape) else Modifier)
            .clickable(enabled = send?.isFinished != false, onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 14.dp),
        horizontalArrangement = Arrangement.spacedBy(14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(52.dp), contentAlignment = Alignment.Center) {
            if (send?.stage == FlyyDropSend.Stage.WAITING) Radar(52.dp) { DeviceArt(device.type, 18.dp) } else DeviceBadge(device.type)
        }

        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(device.name, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                when (send?.stage) {
                    FlyyDropSend.Stage.SENT -> StatusIcon(R.drawable.lucide_circle_check, MaterialTheme.colorScheme.primary)
                    FlyyDropSend.Stage.DECLINED, FlyyDropSend.Stage.NO_ANSWER, FlyyDropSend.Stage.FAILED ->
                        StatusIcon(R.drawable.lucide_circle_alert, MaterialTheme.colorScheme.error)
                    FlyyDropSend.Stage.SENDING -> send?.progress?.let {
                        Text("${(it.fraction * 100).toInt()}%", fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onPrimaryContainer)
                    }
                    FlyyDropSend.Stage.WAITING -> TextButton(onClick = onCancel) { Text(stringResource(R.string.cancel), fontWeight = FontWeight.SemiBold) }
                    null -> Unit
                }
            }

            val reading = send?.progress
            if (send?.stage == FlyyDropSend.Stage.SENDING && reading != null) {
                LinearProgressIndicator(
                    progress = { reading.fraction.toFloat() },
                    modifier = Modifier.fillMaxWidth().height(6.dp),
                    trackColor = MaterialTheme.colorScheme.primary.copy(alpha = 0.2f),
                )
            } else {
                Text(nearbyStatus(send), fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

@Composable
private fun nearbyStatus(send: FlyyDropSend?): String = when (send?.stage) {
    null -> stringResource(R.string.flyydrop_tap)
    FlyyDropSend.Stage.WAITING -> stringResource(R.string.flyydrop_waiting)
    FlyyDropSend.Stage.SENDING -> stringResource(R.string.share_starting)
    FlyyDropSend.Stage.SENT -> stringResource(R.string.flyydrop_sent)
    FlyyDropSend.Stage.NO_ANSWER -> stringResource(R.string.flyydrop_no_answer)
    FlyyDropSend.Stage.FAILED -> stringResource(R.string.flyydrop_failed)
    FlyyDropSend.Stage.DECLINED -> when (send?.reason) {
        FlyyDrop.Reason.UNAVAILABLE -> stringResource(R.string.flyydrop_unavailable)
        FlyyDrop.Reason.BUSY -> stringResource(R.string.flyydrop_busy)
        else -> stringResource(R.string.flyydrop_declined)
    }
}

@Composable
private fun StatusIcon(@DrawableRes icon: Int, tint: androidx.compose.ui.graphics.Color) {
    Icon(painterResource(icon), contentDescription = null, tint = tint, modifier = Modifier.size(18.dp))
}

/** "2 of 3 · 41 MB/s · about 1 second left" */
@Composable
private fun progressDetail(progress: SendProgress, files: Outgoing.Files): String {
    val context = LocalContext.current
    val parts = mutableListOf<String>()

    if (files.items.size > 1) {
        var total = 0L
        val current = files.items.indexOfFirst { total += it.size; progress.reading.done < total }.let { if (it < 0) files.items.size else it + 1 }
        parts += stringResource(R.string.share_n_of_m, current, files.items.size)
    }

    parts += TransferText.speedAndTimeLeft(context, progress.reading)

    return parts.joinToString(" · ").ifEmpty { stringResource(R.string.share_starting) }
}
