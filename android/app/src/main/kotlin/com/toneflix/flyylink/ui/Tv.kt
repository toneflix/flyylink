package com.toneflix.flyylink.ui

import android.graphics.Bitmap
import android.os.Build
import android.util.Size
import android.widget.VideoView
import androidx.annotation.DrawableRes
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.animateScrollBy
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.onKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.toneflix.flyylink.DeviceState
import com.toneflix.flyylink.DeviceStore
import com.toneflix.flyylink.FlyyDropPlugin
import com.toneflix.flyylink.NotificationPlugin
import com.toneflix.flyylink.R
import com.toneflix.flyylink.Recent
import com.toneflix.flyylink.RecentItem
import com.toneflix.flyylink.ScreenControlService
import com.toneflix.flyylink.TvSettings
import com.toneflix.flyylink.TvViewerActivity
import com.toneflix.flyylink.openIntent
import com.toneflix.flyylink.protocol.PairState
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

private val TvGlow = Brush.radialGradient(
    listOf(Toneflix.gold.copy(alpha = 0.22f), Color.Transparent),
    center = androidx.compose.ui.geometry.Offset(0f, 0f),
    radius = 1400f,
)

/** The TV's home screen, worked with the remote: who is connected, what arrived, and the TV's settings. */
@Composable
fun TvHomeScreen(tvName: String) {
    val devices by DeviceStore.states.collectAsStateWithLifecycle()
    val recent by Recent.items.collectAsStateWithLifecycle()
    val paired = devices.filter { it.pairState == PairState.PAIRED }
    val others = devices.filter { it.pairState != PairState.PAIRED && it.canPair }
    val shown = paired + others
    val firstFocus = remember { FocusRequester() }

    LaunchedEffect(shown.isEmpty(), recent.isEmpty()) {
        runCatching { firstFocus.requestFocus() }
    }

    Box(
        Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
            .background(TvGlow),
    ) {
        Row(Modifier.fillMaxSize().padding(horizontal = 58.dp, vertical = 32.dp), horizontalArrangement = Arrangement.spacedBy(48.dp)) {
            TvIntro(tvName, paired, Modifier.width(300.dp).fillMaxHeight())

            Column(Modifier.weight(1f).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(22.dp)) {
                if (shown.isNotEmpty()) {
                    TvRow(stringResource(R.string.tv_devices)) {
                        LazyRow(horizontalArrangement = Arrangement.spacedBy(20.dp), contentPadding = PaddingValues(8.dp)) {
                            itemsIndexed(shown, key = { _, device -> device.id }) { index, device ->
                                TvDeviceCard(device, if (index == 0) Modifier.focusRequester(firstFocus) else Modifier)
                            }
                        }
                    }
                }

                if (recent.isNotEmpty()) {
                    TvRow(stringResource(R.string.tv_received)) {
                        LazyRow(horizontalArrangement = Arrangement.spacedBy(20.dp), contentPadding = PaddingValues(8.dp)) {
                            itemsIndexed(recent, key = { _, item -> item.id }) { index, item ->
                                val focus = if (index == 0 && shown.isEmpty()) Modifier.focusRequester(firstFocus) else Modifier
                                TvRecentCard(item, focus)
                            }
                        }
                    }
                }

                TvRow(stringResource(R.string.tv_settings)) {
                    TvSettingsRow(if (shown.isEmpty() && recent.isEmpty()) Modifier.focusRequester(firstFocus) else Modifier)
                }
            }
        }

        TvPairDialog()
        TvFlyyDropDialog()
    }
}

@Composable
private fun TvIntro(tvName: String, paired: List<DeviceState>, modifier: Modifier) {
    Column(modifier, verticalArrangement = Arrangement.spacedBy(18.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Image(painterResource(R.drawable.ic_flyylink_logo), contentDescription = null, modifier = Modifier.size(40.dp))
            Text("FLYYLINK", fontSize = 15.sp, fontWeight = FontWeight.Medium, letterSpacing = 6.sp, color = Toneflix.gold)
        }

        Spacer(Modifier.height(24.dp))

        TvArt(220.dp) {
            Box(Modifier.fillMaxSize().background(Toneflix.goldGlow), contentAlignment = Alignment.Center) {
                Image(painterResource(R.drawable.ic_flyylink_logo), contentDescription = null, modifier = Modifier.size(56.dp))
            }
        }

        Text(tvName, fontSize = 34.sp, fontWeight = FontWeight.Bold, letterSpacing = (-0.5).sp, maxLines = 2, overflow = TextOverflow.Ellipsis)

        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Box(Modifier.size(10.dp).background(if (paired.isEmpty()) Toneflix.gold else FlyyLink.colors.ok, CircleShape))
            Text(
                if (paired.isEmpty()) stringResource(R.string.tv_ready) else stringResource(R.string.tv_connected_to, paired.joinToString { it.name }),
                fontSize = 16.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }

        if (paired.isEmpty()) {
            Text(stringResource(R.string.tv_hint), fontSize = 16.sp, lineHeight = 23.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun TvRow(title: String, content: @Composable () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Text(title, fontSize = 20.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 8.dp))
        content()
    }
}

/** A card that grows and gets a gold edge while the remote's focus is on it. */
@Composable
private fun TvCard(
    modifier: Modifier = Modifier,
    width: Dp,
    height: Dp,
    shape: Shape = RoundedCornerShape(24.dp),
    onClick: () -> Unit,
    content: @Composable () -> Unit,
) {
    val interaction = remember { MutableInteractionSource() }
    val focused by interaction.collectIsFocusedAsState()
    val scale by animateFloatAsState(if (focused) 1.06f else 1f, label = "focus")
    val colors = MaterialTheme.colorScheme

    Box(
        modifier
            .size(width, height)
            .scale(scale)
            .clip(shape)
            .background(if (focused) colors.surfaceVariant else colors.surface)
            .border(if (focused) 3.dp else 1.dp, if (focused) Toneflix.gold else colors.outlineVariant, shape)
            .clickable(interactionSource = interaction, indication = null, onClick = onClick),
    ) { content() }
}

@Composable
private fun TvDeviceCard(device: DeviceState, modifier: Modifier) {
    var unpair by remember { mutableStateOf(false) }
    val status = when (device.pairState) {
        PairState.PAIRED -> stringResource(R.string.home_mac_live)
        PairState.REQUESTED -> stringResource(R.string.status_waiting)
        PairState.REQUESTED_BY_PEER -> stringResource(R.string.status_wants_pair)
        else -> stringResource(R.string.pair)
    }

    TvCard(modifier, 200.dp, 160.dp, onClick = {
        when (device.pairState) {
            PairState.PAIRED -> {
                unpair = true
            }
            PairState.NOT_PAIRED -> withDevice(device.id) { it.requestPair() }
            else -> Unit
        }
    }) {
        Column(Modifier.fillMaxSize().padding(16.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.SpaceBetween) {
            Box(Modifier.height(76.dp), contentAlignment = Alignment.Center) { DeviceArt(device.type, 92.dp) }
            Text(device.name, fontSize = 17.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (device.pairState == PairState.PAIRED) Box(Modifier.size(8.dp).background(FlyyLink.colors.ok, CircleShape))
                Text(status, fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }

    if (unpair) {
        TvChoiceDialog(
            title = stringResource(R.string.home_unpair, device.name),
            confirm = stringResource(R.string.tv_unpair),
            dismiss = stringResource(R.string.cancel),
            onConfirm = {
                unpair = false
                withDevice(device.id) { it.unpair() }
            },
            onDismiss = { unpair = false },
        )
    }
}

@Composable
private fun TvRecentCard(item: RecentItem, modifier: Modifier) {
    val context = LocalContext.current
    val thumbnail by produceState<Bitmap?>(null, item.uri, item.kind) {
        val uri = item.uri
        val media = TvViewerActivity.Kind.of(item.mimeType)
        if (uri != null && item.kind == RecentItem.Kind.RECEIVED && Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q &&
            (media == TvViewerActivity.Kind.PICTURE || media == TvViewerActivity.Kind.VIDEO)
        ) {
            value = withContext(Dispatchers.IO) { runCatching { context.contentResolver.loadThumbnail(uri, Size(560, 320), null) }.getOrNull() }
        }
    }

    TvCard(modifier, 240.dp, 160.dp, onClick = { item.openIntent(context)?.let { runCatching { context.startActivity(it) } } }) {
        Column(Modifier.fillMaxSize()) {
            Box(Modifier.fillMaxWidth().height(96.dp).background(MaterialTheme.colorScheme.primaryContainer), contentAlignment = Alignment.Center) {
                val picture = thumbnail
                if (picture != null) {
                    Image(picture.asImageBitmap(), contentDescription = null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
                } else {
                    Icon(painterResource(tvRecentIcon(item)), contentDescription = null, tint = MaterialTheme.colorScheme.onPrimaryContainer, modifier = Modifier.size(34.dp))
                }
            }
            Column(Modifier.padding(horizontal = 14.dp, vertical = 10.dp)) {
                Text(item.title, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(item.detail, fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

@DrawableRes
private fun tvRecentIcon(item: RecentItem): Int = when {
    item.kind == RecentItem.Kind.LINK -> R.drawable.lucide_link
    item.kind == RecentItem.Kind.TEXT -> R.drawable.lucide_message_square
    item.kind == RecentItem.Kind.FAILED -> R.drawable.lucide_circle_alert
    else -> when (TvViewerActivity.Kind.of(item.mimeType)) {
        TvViewerActivity.Kind.PICTURE -> R.drawable.lucide_image
        TvViewerActivity.Kind.VIDEO -> R.drawable.lucide_film
        TvViewerActivity.Kind.MUSIC -> R.drawable.lucide_music
        null -> R.drawable.lucide_file_text
    }
}

/** Remote control, pop-ups and the other choices a TV has, as cards that show where each one stands. */
@Composable
private fun TvSettingsRow(firstModifier: Modifier) {
    val context = LocalContext.current
    val popups by TvSettings.popups.collectAsStateWithLifecycle()
    var control by remember { mutableStateOf(ScreenControlService.status(context)) }
    var overApps by remember { mutableStateOf(TvSettings.canShowOverApps(context)) }
    var nowPlaying by remember { mutableStateOf(NotificationPlugin.hasAccess(context)) }
    var disclosure by remember { mutableStateOf(false) }
    val overAppsIntent = remember { TvSettings.overAppsIntent(context) }
    val nowPlayingIntent = remember { NotificationPlugin.accessSettingsIntent() }

    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) {
        control = ScreenControlService.status(context)
        overApps = TvSettings.canShowOverApps(context)
        nowPlaying = NotificationPlugin.hasAccess(context)
    }

    val on = stringResource(R.string.tv_on)
    val off = stringResource(R.string.tv_off)

    LazyRow(horizontalArrangement = Arrangement.spacedBy(20.dp), contentPadding = PaddingValues(8.dp)) {
        item {
            val status = when (control) {
                ScreenControlService.Status.ON -> on
                ScreenControlService.Status.STOPPED -> stringResource(R.string.tv_remote_stopped)
                ScreenControlService.Status.OFF -> stringResource(R.string.tv_turn_on)
            }
            TvSettingCard(R.drawable.lucide_tv, stringResource(R.string.tv_remote), status, control == ScreenControlService.Status.ON, firstModifier) {
                if (control == ScreenControlService.Status.ON) context.startActivity(ScreenControlService.settingsIntent()) else disclosure = true
            }
        }
        item {
            TvSettingCard(R.drawable.lucide_bell, stringResource(R.string.tv_popups), if (popups) on else off, popups) {
                TvSettings.setPopups(context, !popups)
            }
        }
        if (TvSettings.canOpen(context, overAppsIntent)) {
            item {
                TvSettingCard(R.drawable.lucide_download, stringResource(R.string.tv_over_apps), if (overApps) on else stringResource(R.string.tv_turn_on), overApps) {
                    runCatching { context.startActivity(overAppsIntent) }
                }
            }
        }
        if (TvSettings.canOpen(context, nowPlayingIntent)) {
            item {
                TvSettingCard(R.drawable.lucide_music, stringResource(R.string.tv_now_playing), if (nowPlaying) on else stringResource(R.string.tv_turn_on), nowPlaying) {
                    runCatching { context.startActivity(nowPlayingIntent) }
                }
            }
        }
    }

    if (disclosure) {
        TvControlDisclosure(
            onAccept = {
                disclosure = false
                runCatching { context.startActivity(ScreenControlService.settingsIntent()) }
            },
            onDecline = { disclosure = false },
        )
    }
}

@Composable
private fun TvSettingCard(@DrawableRes icon: Int, title: String, status: String, isOn: Boolean, modifier: Modifier = Modifier, onClick: () -> Unit) {
    TvCard(modifier, 200.dp, 112.dp, onClick = onClick) {
        Column(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.SpaceBetween) {
            Box(
                Modifier.size(40.dp).background(if (isOn) Toneflix.gold else MaterialTheme.colorScheme.primaryContainer, RoundedCornerShape(13.dp)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painterResource(icon),
                    contentDescription = null,
                    tint = if (isOn) FlyyLink.colors.onHero else MaterialTheme.colorScheme.onPrimaryContainer,
                    modifier = Modifier.size(22.dp),
                )
            }
            Column {
                Text(title, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(status, fontSize = 13.sp, color = if (isOn) FlyyLink.colors.ok else MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1)
            }
        }
    }
}

/** A focusable button for TV dialogs. */
@Composable
private fun TvButton(label: String, primary: Boolean, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val interaction = remember { MutableInteractionSource() }
    val focused by interaction.collectIsFocusedAsState()
    val scale by animateFloatAsState(if (focused) 1.06f else 1f, label = "focus")
    val background = when {
        focused -> Toneflix.gold
        primary -> MaterialTheme.colorScheme.primaryContainer
        else -> MaterialTheme.colorScheme.surfaceVariant
    }

    Box(
        modifier
            .scale(scale)
            .clip(RoundedCornerShape(50))
            .background(background)
            .clickable(interactionSource = interaction, indication = null, onClick = onClick)
            .padding(horizontal = 34.dp, vertical = 14.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            label,
            fontSize = 17.sp,
            fontWeight = FontWeight.SemiBold,
            color = if (focused) FlyyLink.colors.onHero else MaterialTheme.colorScheme.onSurface,
        )
    }
}

@Composable
private fun TvDialogFrame(
    width: Dp = 620.dp,
    horizontalAlignment: Alignment.Horizontal = Alignment.CenterHorizontally,
    content: @Composable ColumnScope.() -> Unit,
) {
    val scroll = rememberScrollState()
    val scope = rememberCoroutineScope()

    // Kept within the screen and scrolled with the remote's up and down, since the dialog's buttons sit in one row.
    Column(
        Modifier
            .width(width)
            .heightIn(max = LocalConfiguration.current.screenHeightDp.dp - 48.dp)
            .background(MaterialTheme.colorScheme.surface, RoundedCornerShape(32.dp))
            .border(1.dp, MaterialTheme.colorScheme.outlineVariant, RoundedCornerShape(32.dp))
            .onKeyEvent { event ->
                val step = when (event.key) {
                    Key.DirectionUp -> -240f
                    Key.DirectionDown -> 240f
                    else -> return@onKeyEvent false
                }
                if (event.type == KeyEventType.KeyDown) scope.launch { scroll.animateScrollBy(step) }
                true
            }
            .verticalScroll(scroll)
            .padding(40.dp),
        horizontalAlignment = horizontalAlignment,
        verticalArrangement = Arrangement.spacedBy(24.dp),
        content = content,
    )
}

@Composable
private fun TvDialogButtons(confirm: String, dismiss: String, onConfirm: () -> Unit, onDismiss: () -> Unit) {
    val focus = remember { FocusRequester() }

    Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
        TvButton(dismiss, primary = false, onClick = onDismiss)
        TvButton(confirm, primary = true, modifier = Modifier.focusRequester(focus), onClick = onConfirm)
    }

    LaunchedEffect(Unit) { runCatching { focus.requestFocus() } }
}

@Composable
private fun TvChoiceDialog(title: String, confirm: String, dismiss: String, onConfirm: () -> Unit, onDismiss: () -> Unit) {
    Dialog(onDismissRequest = onDismiss) {
        TvDialogFrame {
            Text(title, fontSize = 26.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center)
            TvDialogButtons(confirm, dismiss, onConfirm, onDismiss)
        }
    }
}

@Composable
private fun TvPairDialog() {
    val request by DeviceStore.pairRequest.collectAsStateWithLifecycle()
    val device = request ?: return

    Dialog(onDismissRequest = { withDevice(device.id) { it.reject() } }) {
        TvDialogFrame {
            DeviceArt(device.type, 120.dp)
            Text(stringResource(R.string.pair_title, device.name), fontSize = 28.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center)
            Text(stringResource(R.string.tv_pair_hint), fontSize = 17.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            device.code?.let { CodeDigits(it) }
            TvDialogButtons(
                confirm = stringResource(R.string.pair),
                dismiss = stringResource(R.string.not_now),
                onConfirm = { withDevice(device.id) { it.accept() } },
                onDismiss = { withDevice(device.id) { it.reject() } },
            )
        }
    }
}

@Composable
private fun TvFlyyDropDialog() {
    val asks by FlyyDropPlugin.asks.collectAsStateWithLifecycle()
    val ask = asks.firstOrNull() ?: return
    val context = LocalContext.current

    Dialog(onDismissRequest = { FlyyDropPlugin.decline(ask.offer.id) }) {
        TvDialogFrame {
            Radar(size = 150.dp) { DeviceArt(ask.deviceType, 60.dp) }
            Text(stringResource(R.string.flyydrop_ask_title, ask.deviceName), fontSize = 28.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center)
            Text(FlyyDropPlugin.summary(context, ask.offer), fontSize = 17.sp, color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Center)
            TvDialogButtons(
                confirm = stringResource(R.string.flyydrop_accept),
                dismiss = stringResource(R.string.flyydrop_decline),
                onConfirm = { FlyyDropPlugin.accept(ask.offer.id) },
                onDismiss = { FlyyDropPlugin.decline(ask.offer.id) },
            )
        }
    }
}

/** What the "FlyyLink remote" accessibility service does, shown before the TV's Accessibility settings. */
@Composable
private fun TvControlDisclosure(onAccept: () -> Unit, onDecline: () -> Unit) {
    // Wide and in two columns, so it fits a TV screen with the buttons in view.
    Dialog(onDismissRequest = onDecline, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        TvDialogFrame(width = 900.dp, horizontalAlignment = Alignment.End) {
            Row(horizontalArrangement = Arrangement.spacedBy(40.dp)) {
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                    Text(stringResource(R.string.tv_control_title), fontSize = 28.sp, lineHeight = 34.sp, fontWeight = FontWeight.Bold)
                    Text(stringResource(R.string.tv_control_intro), fontSize = 16.sp, lineHeight = 23.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(stringResource(R.string.tv_control_settings), fontSize = 15.sp, lineHeight = 21.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                Column(Modifier.weight(1.2f), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    listOf(R.string.tv_control_does, R.string.tv_control_typing, R.string.tv_control_only, R.string.tv_control_private, R.string.control_disclosure_local).forEach {
                        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                            Icon(painterResource(R.drawable.lucide_check), contentDescription = null, tint = Toneflix.gold, modifier = Modifier.size(20.dp))
                            Text(stringResource(it), fontSize = 15.sp, lineHeight = 21.sp)
                        }
                    }
                }
            }
            TvDialogButtons(
                confirm = stringResource(R.string.control_disclosure_accept),
                dismiss = stringResource(R.string.control_disclosure_decline),
                onConfirm = onAccept,
                onDismiss = onDecline,
            )
        }
    }
}

/** A received photo, video or song, full screen, with who sent it shown for a few seconds. */
@Composable
fun TvViewer(
    kind: TvViewerActivity.Kind,
    title: String,
    detail: String,
    loadPicture: () -> Bitmap?,
    onPlayer: (VideoView) -> Unit,
) {
    var caption by remember { mutableStateOf(true) }
    val picture by produceState<Bitmap?>(null) {
        if (kind == TvViewerActivity.Kind.PICTURE) value = withContext(Dispatchers.IO) { loadPicture() }
    }

    LaunchedEffect(Unit) {
        delay(4_000)
        caption = false
    }

    Box(Modifier.fillMaxSize().background(Color.Black), contentAlignment = Alignment.Center) {
        when (kind) {
            TvViewerActivity.Kind.PICTURE -> picture?.let {
                Image(it.asImageBitmap(), contentDescription = title, contentScale = ContentScale.Fit, modifier = Modifier.fillMaxSize())
            }
            TvViewerActivity.Kind.VIDEO -> AndroidView(factory = { VideoView(it).also(onPlayer) }, modifier = Modifier.fillMaxSize())
            TvViewerActivity.Kind.MUSIC -> {
                AndroidView(factory = { VideoView(it).also(onPlayer) }, modifier = Modifier.size(1.dp))
                Column(
                    Modifier.fillMaxSize().background(Toneflix.goldGlow),
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(24.dp, Alignment.CenterVertically),
                ) {
                    Box(Modifier.size(220.dp).background(Color.Black.copy(alpha = 0.18f), RoundedCornerShape(44.dp)), contentAlignment = Alignment.Center) {
                        Icon(painterResource(R.drawable.lucide_music), contentDescription = null, tint = Color.White, modifier = Modifier.size(96.dp))
                    }
                    Text(title, fontSize = 34.sp, fontWeight = FontWeight.Bold, color = Color.White, maxLines = 2, textAlign = TextAlign.Center)
                    Text(detail, fontSize = 18.sp, color = Color.White.copy(alpha = 0.8f))
                }
            }
        }

        if (kind != TvViewerActivity.Kind.MUSIC) {
            AnimatedVisibility(caption, Modifier.align(Alignment.BottomStart), enter = fadeIn(), exit = fadeOut()) {
                Column(
                    Modifier
                        .fillMaxWidth()
                        .background(Brush.verticalGradient(listOf(Color.Transparent, Color.Black.copy(alpha = 0.7f))))
                        .padding(horizontal = 58.dp, vertical = 40.dp),
                ) {
                    Text(title, fontSize = 26.sp, fontWeight = FontWeight.Bold, color = Color.White, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(detail, fontSize = 17.sp, color = Color.White.copy(alpha = 0.8f))
                }
            }
        }
    }
}
