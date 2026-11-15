package com.toneflix.flyylink.ui

import androidx.annotation.DrawableRes
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.positionChange
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.onClick
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.toneflix.flyylink.DeviceStore
import com.toneflix.flyylink.R
import com.toneflix.flyylink.RemoteMediaPlugin
import com.toneflix.flyylink.protocol.MediaAction
import com.toneflix.flyylink.protocol.MediaPlayer
import com.toneflix.flyylink.protocol.PairState
import com.toneflix.flyylink.protocol.TvRemote
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/** A TV remote: what's playing, the arrows and OK, Back, Home, volume and a field that types on the TV. */
@Composable
fun RemoteScreen(deviceId: String, onPress: (TvRemote) -> Unit, onClose: () -> Unit) {
    val devices by DeviceStore.states.collectAsStateWithLifecycle()
    val playing by RemoteMediaPlugin.playing.collectAsStateWithLifecycle()
    val tv = devices.firstOrNull { it.id == deviceId && it.pairState == PairState.PAIRED }
    val key = { name: String -> onPress(TvRemote.Key(name)) }

    Column(
        Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
            .statusBarsPadding()
            .navigationBarsPadding()
            .imePadding()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp)
            .padding(bottom = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(22.dp),
    ) {
        RemoteHeader(tv?.name ?: stringResource(R.string.remote_title), connected = tv != null, onClose = onClose)

        playing[deviceId]?.let { NowPlaying(deviceId, it) }

        var touchpad by rememberSaveable { mutableStateOf(false) }
        ModeSwitch(listOf(false to R.string.remote_buttons, true to R.string.remote_touchpad), touchpad, onChange = { touchpad = it })

        if (touchpad) Touchpad(onPress) else DPad(onKey = key)

        Row(horizontalArrangement = Arrangement.spacedBy(28.dp)) {
            RoundKey(R.drawable.lucide_arrow_left, stringResource(R.string.remote_back)) { key(TvRemote.BACK) }
            RoundKey(R.drawable.lucide_house, stringResource(R.string.remote_home)) { key(TvRemote.HOME) }
            RoundKey(R.drawable.lucide_ellipsis, stringResource(R.string.remote_menu)) { key(TvRemote.MENU) }
        }

        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            VolumeBar(onDown = { key(TvRemote.VOLUME_DOWN) }, onMute = { key(TvRemote.MUTE) }, onUp = { key(TvRemote.VOLUME_UP) })
            RoundKey(R.drawable.lucide_play, stringResource(R.string.remote_play_pause), solid = true) { key(TvRemote.PLAY_PAUSE) }
        }

        LiveTypeField(
            stringResource(R.string.remote_type),
            onText = { onPress(TvRemote.Text(it)) },
            onDelete = { onPress(TvRemote.Key(TvRemote.DELETE)) },
            onEnter = { onPress(TvRemote.Key(TvRemote.ENTER)) },
        )
    }
}

/** The close button, the device's name and whether it is connected. */
@Composable
internal fun RemoteHeader(name: String, connected: Boolean, onClose: () -> Unit) {
    Row(Modifier.fillMaxWidth().padding(top = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        IconButton(onClick = onClose) {
            Icon(painterResource(R.drawable.lucide_x), contentDescription = stringResource(R.string.cancel))
        }
        Column(Modifier.weight(1f)) {
            Text(name, fontSize = 20.sp, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Box(Modifier.size(8.dp).background(if (connected) FlyyLink.colors.ok else MaterialTheme.colorScheme.outline, CircleShape))
                Text(
                    stringResource(if (connected) R.string.home_connected else R.string.share_not_connected),
                    fontSize = 13.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
internal fun NowPlaying(deviceId: String, player: MediaPlayer) {
    Row(
        Modifier
            .fillMaxWidth()
            .background(FlyyLink.colors.hero, MaterialTheme.shapes.medium)
            .padding(horizontal = 16.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Box(Modifier.size(44.dp).background(Color.Black.copy(alpha = 0.12f), RoundedCornerShape(14.dp)), contentAlignment = Alignment.Center) {
            Icon(painterResource(R.drawable.lucide_music), contentDescription = null, tint = FlyyLink.colors.onHero, modifier = Modifier.size(22.dp))
        }
        Column(Modifier.weight(1f)) {
            Text(
                player.title.ifBlank { stringResource(if (player.isPlaying) R.string.remote_playing else R.string.remote_paused) },
                color = FlyyLink.colors.onHero,
                fontSize = 15.sp,
                fontWeight = FontWeight.SemiBold,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                player.artist.ifBlank { player.name },
                color = FlyyLink.colors.onHero.copy(alpha = 0.75f),
                fontSize = 13.sp,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        val tint = FlyyLink.colors.onHero
        if (player.canGoPrevious) MediaButton(R.drawable.lucide_skip_back, tint) { RemoteMediaPlugin.press(deviceId, MediaAction.PREVIOUS) }
        MediaButton(if (player.isPlaying) R.drawable.lucide_pause else R.drawable.lucide_play, tint) { RemoteMediaPlugin.press(deviceId, MediaAction.PLAY_PAUSE) }
        if (player.canGoNext) MediaButton(R.drawable.lucide_skip_forward, tint) { RemoteMediaPlugin.press(deviceId, MediaAction.NEXT) }
    }
}

@Composable
private fun MediaButton(@DrawableRes icon: Int, tint: Color, onClick: () -> Unit) {
    IconButton(onClick = onClick, modifier = Modifier.size(40.dp)) {
        Icon(painterResource(icon), contentDescription = null, tint = tint, modifier = Modifier.size(22.dp))
    }
}

/** Picks one of the remote's modes, such as the arrows or the touchpad. */
@Composable
internal fun <T> ModeSwitch(modes: List<Pair<T, Int>>, current: T, onChange: (T) -> Unit) {
    val colors = MaterialTheme.colorScheme

    Row(
        Modifier
            .background(colors.surface, CircleShape)
            .border(1.dp, colors.outlineVariant, CircleShape)
            .padding(4.dp),
    ) {
        modes.forEach { (mode, label) ->
            val selected = mode == current
            Text(
                stringResource(label),
                fontSize = 14.sp,
                fontWeight = FontWeight.SemiBold,
                color = if (selected) colors.onPrimaryContainer else colors.onSurfaceVariant,
                modifier = Modifier
                    .clip(CircleShape)
                    .background(if (selected) colors.primaryContainer else Color.Transparent)
                    .selectable(selected = selected, role = Role.Tab, onClick = { onChange(mode) })
                    .padding(horizontal = if (modes.size > 2) 18.dp else 22.dp, vertical = 10.dp),
            )
        }
    }
}

/**
 * Moves the TV's pointer like a laptop touchpad: slide to move, tap to click, two fingers to scroll.
 * Moves go out about 60 times a second and scrolls about 10, since each scroll is a swipe on the TV.
 */
@Composable
private fun Touchpad(onPress: (TvRemote) -> Unit) {
    val colors = MaterialTheme.colorScheme
    val haptics = LocalHapticFeedback.current

    Box(
        Modifier
            .fillMaxWidth()
            .height(300.dp)
            .background(colors.surface, RoundedCornerShape(28.dp))
            .border(1.dp, colors.outlineVariant, RoundedCornerShape(28.dp))
            .pointerInput(Unit) {
                awaitEachGesture {
                    val down = awaitFirstDown(requireUnconsumed = false)
                    var fingers = 1
                    var travelled = 0f
                    var pending = Offset.Zero
                    var sentAt = down.uptimeMillis
                    var now = down.uptimeMillis

                    fun send(force: Boolean) {
                        val gap = if (fingers > 1) SCROLL_EVERY_MS else MOVE_EVERY_MS
                        if (pending == Offset.Zero || (!force && now - sentAt < gap)) return

                        val dx = (pending.x / density).toDouble()
                        val dy = (pending.y / density).toDouble()
                        onPress(if (fingers > 1) TvRemote.Scroll(dx, dy) else TvRemote.Move(dx, dy))
                        pending = Offset.Zero
                        sentAt = now
                    }

                    do {
                        val event = awaitPointerEvent()
                        val pressed = event.changes.filter { it.pressed }
                        now = event.changes.maxOf { it.uptimeMillis }

                        if (pressed.size > fingers) pending = Offset.Zero
                        fingers = maxOf(fingers, pressed.size)

                        if (pressed.isNotEmpty()) {
                            val step = pressed.fold(Offset.Zero) { sum, change -> sum + change.positionChange() } / pressed.size.toFloat()
                            travelled += step.getDistance()
                            pending += step
                        }
                        event.changes.forEach { it.consume() }
                        send(force = false)
                    } while (pressed.isNotEmpty())

                    send(force = true)

                    if (fingers == 1 && travelled < viewConfiguration.touchSlop && now - down.uptimeMillis < TAP_MS) {
                        haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                        onPress(TvRemote.Click)
                    }
                }
            },
        contentAlignment = Alignment.Center,
    ) {
        Text(
            stringResource(R.string.remote_touchpad_hint),
            fontSize = 13.sp,
            color = colors.onSurfaceVariant,
            textAlign = TextAlign.Center,
            modifier = Modifier.padding(24.dp),
        )
    }
}

internal const val MOVE_EVERY_MS = 16L
private const val SCROLL_EVERY_MS = 100L
internal const val TAP_MS = 300L

/** A key that presses once on tap and keeps pressing while held, like a real remote. */
internal fun Modifier.repeatingPress(scope: CoroutineScope, onPress: () -> Unit, onDown: (Boolean) -> Unit = {}) =
    pointerInput(Unit) {
        detectTapGestures(onPress = {
            onDown(true)
            onPress()
            val repeat = scope.launch {
                delay(450)
                while (true) {
                    onPress()
                    delay(110)
                }
            }
            tryAwaitRelease()
            repeat.cancel()
            onDown(false)
        })
    }

/** The ring of arrows around a gold OK button. */
@Composable
private fun DPad(onKey: (String) -> Unit, size: Dp = 270.dp) {
    val colors = MaterialTheme.colorScheme

    Box(
        Modifier
            .size(size)
            .background(colors.surface, CircleShape)
            .border(1.dp, colors.outlineVariant, CircleShape),
        contentAlignment = Alignment.Center,
    ) {
        ArrowKey(R.drawable.lucide_chevron_up, stringResource(R.string.remote_up), Modifier.align(Alignment.TopCenter)) { onKey(TvRemote.UP) }
        ArrowKey(R.drawable.lucide_chevron_down, stringResource(R.string.remote_down), Modifier.align(Alignment.BottomCenter)) { onKey(TvRemote.DOWN) }
        ArrowKey(R.drawable.lucide_chevron_left, stringResource(R.string.remote_left), Modifier.align(Alignment.CenterStart)) { onKey(TvRemote.LEFT) }
        ArrowKey(R.drawable.lucide_chevron_right, stringResource(R.string.remote_right), Modifier.align(Alignment.CenterEnd)) { onKey(TvRemote.RIGHT) }

        val haptics = LocalHapticFeedback.current
        var down by remember { mutableStateOf(false) }
        val scope = rememberCoroutineScope()
        val label = stringResource(R.string.remote_ok)
        Box(
            Modifier
                .size(size * 0.38f)
                .scale(if (down) 0.94f else 1f)
                .clip(CircleShape)
                .background(Toneflix.goldGradient)
                .semantics {
                    role = Role.Button
                    contentDescription = label
                    onClick { onKey(TvRemote.SELECT); true }
                }
                .repeatingPress(scope, onPress = {
                    haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                    onKey(TvRemote.SELECT)
                }, onDown = { down = it }),
            contentAlignment = Alignment.Center,
        ) {
            Text(label, color = FlyyLink.colors.onHero, fontSize = 20.sp, fontWeight = FontWeight.Bold)
        }
    }
}

@Composable
private fun ArrowKey(@DrawableRes icon: Int, label: String, modifier: Modifier, onPress: () -> Unit) {
    val haptics = LocalHapticFeedback.current
    var down by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    Box(
        modifier
            .size(84.dp)
            .clip(CircleShape)
            .background(if (down) MaterialTheme.colorScheme.primaryContainer else Color.Transparent)
            .semantics {
                role = Role.Button
                contentDescription = label
                onClick { onPress(); true }
            }
            .repeatingPress(scope, onPress = {
                haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                onPress()
            }, onDown = { down = it }),
        contentAlignment = Alignment.Center,
    ) {
        Icon(painterResource(icon), contentDescription = null, modifier = Modifier.size(34.dp))
    }
}

@Composable
internal fun RoundKey(@DrawableRes icon: Int, label: String, solid: Boolean = false, onPress: () -> Unit) {
    val colors = MaterialTheme.colorScheme
    val haptics = LocalHapticFeedback.current

    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
        IconButton(
            onClick = {
                haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                onPress()
            },
            modifier = Modifier
                .size(62.dp)
                .background(if (solid) colors.primaryContainer else colors.surface, CircleShape)
                .border(1.dp, colors.outlineVariant, CircleShape),
        ) {
            Icon(painterResource(icon), contentDescription = label, tint = if (solid) colors.onPrimaryContainer else colors.onSurface)
        }
        Text(label, fontSize = 12.sp, color = colors.onSurfaceVariant)
    }
}

/** Volume down, mute and up in one pill. */
@Composable
internal fun VolumeBar(onDown: () -> Unit, onMute: () -> Unit, onUp: () -> Unit) {
    val colors = MaterialTheme.colorScheme
    val haptics = LocalHapticFeedback.current
    val scope = rememberCoroutineScope()

    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Row(
            Modifier
                .height(62.dp)
                .background(colors.surface, CircleShape)
                .border(1.dp, colors.outlineVariant, CircleShape),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            listOf(
                Triple(R.drawable.lucide_volume_1, R.string.remote_volume_down, onDown),
                Triple(R.drawable.lucide_volume_x, R.string.remote_mute, onMute),
                Triple(R.drawable.lucide_volume_2, R.string.remote_volume_up, onUp),
            ).forEach { (icon, label, press) ->
                val description = stringResource(label)
                Box(
                    Modifier
                        .size(62.dp)
                        .clip(CircleShape)
                        .semantics {
                            role = Role.Button
                            contentDescription = description
                            onClick { press(); true }
                        }
                        .repeatingPress(scope, onPress = {
                            haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                            press()
                        }),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(painterResource(icon), contentDescription = null, modifier = Modifier.size(24.dp))
                }
            }
        }
        Text(stringResource(R.string.remote_volume), fontSize = 12.sp, color = colors.onSurfaceVariant)
    }
}

/**
 * Typing here types on the other device as you go: new letters go out as [onText], and each deleted one as [onDelete].
 * [intercept] may take new letters first, such as a shortcut while a modifier is held, and then the field clears.
 */
@Composable
internal fun LiveTypeField(
    placeholder: String,
    onText: (String) -> Unit,
    onDelete: () -> Unit,
    onEnter: () -> Unit,
    modifier: Modifier = Modifier,
    intercept: (String) -> Boolean = { false },
) {
    var typed by remember { mutableStateOf("") }
    val enter = {
        onEnter()
        typed = ""
    }

    OutlinedTextField(
        value = typed,
        onValueChange = { new ->
            val kept = typed.commonPrefixWith(new).length
            val added = new.substring(kept)

            if (added.isNotEmpty() && intercept(added)) {
                typed = ""
            } else {
                repeat(typed.length - kept) { onDelete() }
                if (added.isNotEmpty()) onText(added)
                typed = new
            }
        },
        placeholder = { Text(placeholder) },
        leadingIcon = { Icon(painterResource(R.drawable.lucide_keyboard), contentDescription = null) },
        trailingIcon = {
            IconButton(onClick = enter) {
                Icon(painterResource(R.drawable.lucide_corner_down_left), contentDescription = stringResource(R.string.remote_enter))
            }
        },
        singleLine = true,
        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Go),
        keyboardActions = KeyboardActions(onGo = { enter() }),
        shape = RoundedCornerShape(20.dp),
        modifier = modifier.fillMaxWidth(),
    )
}
