package com.toneflix.flyylink.ui

import android.text.format.DateUtils
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.annotation.DrawableRes
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Slider
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.repeatOnLifecycle
import com.toneflix.flyylink.CastItem
import com.toneflix.flyylink.PlayOnTv
import com.toneflix.flyylink.R
import com.toneflix.flyylink.ShareSender
import com.toneflix.flyylink.TvSession
import com.toneflix.flyylink.protocol.CastKind
import com.toneflix.flyylink.protocol.CastState
import com.toneflix.flyylink.protocol.TvRenderer

/** The home card: what plays on a TV with its controls, or a way to pick something to play. Hidden while no TV is around. */
@Composable
fun PlayOnTvCard() {
    val context = LocalContext.current
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    val session by PlayOnTv.session.collectAsStateWithLifecycle()
    val tvs by PlayOnTv.tvs.collectAsStateWithLifecycle()
    var picked by remember { mutableStateOf<CastItem?>(null) }
    val pick = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        picked = uri?.let { ShareSender.describe(context, listOf(it)).firstOrNull() }?.let { CastItem.of(context, it) }
    }

    LaunchedEffect(lifecycle) { lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) { PlayOnTv.keepSearching() } }

    val current = session
    if (current != null) {
        NowPlayingOnTv(current)
    } else if (tvs.isNotEmpty()) {
        Row(
            Modifier
                .fillMaxWidth()
                .background(MaterialTheme.colorScheme.surface, MaterialTheme.shapes.medium)
                .clickable { pick.launch(arrayOf("image/*", "video/*", "audio/*")) }
                .padding(16.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Badge(R.drawable.lucide_tv)
            Column(Modifier.weight(1f)) {
                Text(stringResource(R.string.play_on_tv), fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
                Text(stringResource(R.string.play_on_tv_hint), fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }

    picked?.let { item -> PlayOnTvSheet(item, onClose = { picked = null }) }
}

/** Picks a TV for [item] and starts playing it there. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PlayOnTvSheet(item: CastItem, onClose: () -> Unit) {
    val state = rememberModalBottomSheetState(skipPartiallyExpanded = true)

    ModalBottomSheet(onDismissRequest = onClose, sheetState = state, containerColor = MaterialTheme.colorScheme.surface) {
        Column(
            Modifier
                .fillMaxWidth()
                .navigationBarsPadding()
                .padding(start = 20.dp, end = 20.dp, bottom = 28.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
                Badge(kindIcon(item.kind), size = 52)
                Text(item.name, fontSize = 17.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }

            PlayOnTvSection(item, onStarted = onClose)
        }
    }
}

/** The "Play on TV" list of TVs nearby, searched for as soon as it shows. */
@Composable
fun PlayOnTvSection(item: CastItem, onStarted: () -> Unit) {
    val context = LocalContext.current
    val tvs by PlayOnTv.tvs.collectAsStateWithLifecycle()
    val searching by PlayOnTv.searching.collectAsStateWithLifecycle()

    LaunchedEffect(Unit) { PlayOnTv.search() }

    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(painterResource(R.drawable.lucide_tv), contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(16.dp))
            Text(
                stringResource(R.string.play_on_tv),
                fontSize = 14.sp,
                fontWeight = FontWeight.SemiBold,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.weight(1f),
            )
            if (searching) CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp)
        }

        tvs.forEach { tv ->
            TvChoice(tv) {
                PlayOnTv.play(context, item, tv)
                onStarted()
            }
        }

        if (tvs.isEmpty()) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    stringResource(if (searching) R.string.play_on_tv_looking else R.string.play_on_tv_none),
                    fontSize = 13.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.weight(1f),
                )
                if (!searching) {
                    TextButton(onClick = PlayOnTv::search) { Text(stringResource(R.string.play_on_tv_search_again), fontWeight = FontWeight.SemiBold) }
                }
            }
        }
    }
}

@Composable
private fun TvChoice(tv: TvRenderer, onClick: () -> Unit) {
    Row(
        Modifier
            .fillMaxWidth()
            .background(MaterialTheme.colorScheme.surfaceContainerLow, RoundedCornerShape(22.dp))
            .clickable(onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 14.dp),
        horizontalArrangement = Arrangement.spacedBy(14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Badge(R.drawable.lucide_tv)
        Column(Modifier.weight(1f)) {
            Text(tv.name, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(stringResource(R.string.play_on_tv_tap), fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun NowPlayingOnTv(session: TvSession) {
    var dragging by remember { mutableStateOf<Float?>(null) }
    val paused = session.state == CastState.PAUSED

    Column(
        Modifier
            .fillMaxWidth()
            .background(MaterialTheme.colorScheme.primaryContainer, MaterialTheme.shapes.medium)
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
            Badge(kindIcon(session.item.kind), background = MaterialTheme.colorScheme.surface)
            Column(Modifier.weight(1f)) {
                Text(session.item.name, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(
                    stringResource(R.string.play_on_tv_on, session.tv.name),
                    fontSize = 12.sp,
                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            if (session.state == CastState.LOADING) {
                CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
            } else if (session.item.kind != CastKind.PHOTO) {
                FilledIconButton(onClick = PlayOnTv::togglePause) {
                    Icon(
                        painterResource(if (paused) R.drawable.lucide_play else R.drawable.lucide_pause),
                        contentDescription = stringResource(if (paused) R.string.play_on_tv_play else R.string.play_on_tv_pause),
                        modifier = Modifier.size(18.dp),
                    )
                }
            }
            IconButton(onClick = PlayOnTv::stop) {
                Icon(painterResource(R.drawable.lucide_x), contentDescription = stringResource(R.string.play_on_tv_stop), modifier = Modifier.size(20.dp))
            }
        }

        if (session.duration > 0) {
            val fraction = dragging ?: (session.position.toFloat() / session.duration).coerceIn(0f, 1f)

            Slider(
                value = fraction,
                onValueChange = { dragging = it },
                onValueChangeFinished = {
                    dragging?.let { PlayOnTv.seek((it * session.duration).toLong()) }
                    dragging = null
                },
            )
            Row {
                Text(time((fraction * session.duration).toLong()), fontSize = 12.sp, color = MaterialTheme.colorScheme.onPrimaryContainer, modifier = Modifier.weight(1f))
                Text(time(session.duration), fontSize = 12.sp, color = MaterialTheme.colorScheme.onPrimaryContainer)
            }
        }
    }
}

@Composable
private fun Badge(@DrawableRes icon: Int, size: Int = 40, background: androidx.compose.ui.graphics.Color = MaterialTheme.colorScheme.primaryContainer) {
    Box(Modifier.size(size.dp).background(background, CircleShape), contentAlignment = Alignment.Center) {
        Icon(painterResource(icon), contentDescription = null, tint = MaterialTheme.colorScheme.onPrimaryContainer, modifier = Modifier.size((size / 2).dp))
    }
}

@DrawableRes
private fun kindIcon(kind: CastKind): Int = when (kind) {
    CastKind.PHOTO -> R.drawable.lucide_image
    CastKind.VIDEO -> R.drawable.lucide_film
    CastKind.MUSIC -> R.drawable.lucide_music
}

private fun time(ms: Long): String = DateUtils.formatElapsedTime(ms / 1000)
