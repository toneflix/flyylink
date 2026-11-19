package com.toneflix.flyylink.ui

import androidx.activity.compose.BackHandler
import androidx.annotation.DrawableRes
import androidx.annotation.StringRes
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.systemGestureExclusion
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
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
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.toneflix.flyylink.ComputerRemote
import com.toneflix.flyylink.ComputerRemotePlugin
import com.toneflix.flyylink.DeviceStore
import com.toneflix.flyylink.R
import com.toneflix.flyylink.RemoteMediaPlugin
import com.toneflix.flyylink.protocol.PairState
import com.toneflix.flyylink.protocol.RemoteInput
import com.toneflix.flyylink.protocol.RemoteInput.MouseButton
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

enum class ComputerRemoteMode(@StringRes val label: Int) {
    TOUCHPAD(R.string.remote_touchpad),
    KEYBOARD(R.string.computer_remote_keyboard),
    SLIDES(R.string.computer_remote_slides),
}

/** The phone as a computer's touchpad, keyboard and slide clicker, with its volume and what it plays. */
@Composable
fun ComputerRemoteScreen(
    deviceId: String,
    remote: ComputerRemote,
    mode: ComputerRemoteMode,
    onModeChange: (ComputerRemoteMode) -> Unit,
    onFullScreen: () -> Unit,
    onClose: () -> Unit,
) {
    val devices by DeviceStore.states.collectAsStateWithLifecycle()
    val playing by RemoteMediaPlugin.playing.collectAsStateWithLifecycle()
    val answers by ComputerRemotePlugin.answers.collectAsStateWithLifecycle()
    val computer = devices.firstOrNull { it.id == deviceId && it.pairState == PairState.PAIRED }
    val answer = answers[deviceId]
    val isMac = answer?.platform == RemoteInput.MACOS

    LaunchedEffect(computer != null) {
        if (computer != null) remote.begin()
    }

    Column(
        Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
            .statusBarsPadding()
            .navigationBarsPadding()
            .imePadding()
            .padding(horizontal = 20.dp)
            .padding(bottom = 20.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(18.dp),
    ) {
        RemoteHeader(computer?.name ?: stringResource(R.string.computer_remote_title), connected = computer != null, onClose = onClose)

        if (answer?.ready == false) Blocked(isMac)

        playing[deviceId]?.let { NowPlaying(deviceId, it) }

        ModeSwitch(ComputerRemoteMode.entries.map { it to it.label }, mode, onModeChange)

        when (mode) {
            ComputerRemoteMode.TOUCHPAD -> {
                PointerPad(remote, Modifier.weight(1f), onFullScreen)
                MediaKeys(remote)
            }
            ComputerRemoteMode.KEYBOARD -> {
                ComputerKeyboard(remote, isMac)
                PointerPad(remote, Modifier.weight(1f))
            }
            ComputerRemoteMode.SLIDES -> SlideControls(remote, Modifier.weight(1f))
        }
    }
}

/** What to turn on at the computer before it takes the phone's input. */
@Composable
private fun Blocked(isMac: Boolean) {
    val colors = FlyyLink.colors

    Row(
        Modifier
            .fillMaxWidth()
            .background(colors.setup, MaterialTheme.shapes.medium)
            .padding(horizontal = 16.dp, vertical = 14.dp),
        horizontalArrangement = Arrangement.spacedBy(14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(40.dp).background(colors.setupChip, RoundedCornerShape(14.dp)), contentAlignment = Alignment.Center) {
            Icon(painterResource(R.drawable.lucide_circle_alert), contentDescription = null, tint = colors.onSetup, modifier = Modifier.size(20.dp))
        }
        Text(
            stringResource(if (isMac) R.string.computer_remote_blocked_mac else R.string.computer_remote_blocked_pc),
            color = colors.onSetup,
            fontSize = 14.sp,
            modifier = Modifier.weight(1f),
        )
    }
}

/** The touchpad with click and right-click buttons along the bottom, which can be held while another finger moves. */
@Composable
private fun PointerPad(remote: ComputerRemote, modifier: Modifier, onFullScreen: (() -> Unit)? = null) {
    val colors = MaterialTheme.colorScheme
    var dragging by remember { mutableStateOf(false) }
    val shape = RoundedCornerShape(28.dp)

    Column(
        modifier
            .fillMaxWidth()
            .clip(shape)
            .background(colors.surface)
            .border(1.dp, if (dragging) colors.primary else colors.outlineVariant, shape),
    ) {
        Box(Modifier.weight(1f).fillMaxWidth()) {
            TouchpadSurface(remote, Modifier.fillMaxSize(), onDragging = { dragging = it }) {
                TouchpadHint(R.string.computer_remote_touchpad_hint)
            }

            onFullScreen?.let {
                CornerButton(R.drawable.lucide_maximize_2, R.string.computer_remote_full_screen, Modifier.align(Alignment.TopEnd), it)
            }
        }

        Box(Modifier.fillMaxWidth().height(1.dp).background(colors.outlineVariant))
        Row(Modifier.fillMaxWidth().height(56.dp)) {
            MouseButtonArea(remote, MouseButton.LEFT, R.string.computer_remote_click, Modifier.weight(1f))
            Box(Modifier.width(1.dp).fillMaxHeight().background(colors.outlineVariant))
            MouseButtonArea(remote, MouseButton.RIGHT, R.string.computer_remote_right_click, Modifier.weight(1f))
        }
    }
}

/**
 * The whole screen as a touchpad, to use without looking. The hint fades after a moment, and the corner button or Back
 * leaves.
 */
@Composable
fun FullScreenTouchpad(remote: ComputerRemote, onExit: () -> Unit) {
    val colors = MaterialTheme.colorScheme
    var dragging by remember { mutableStateOf(false) }
    var hinting by remember { mutableStateOf(true) }
    val hintAlpha by animateFloatAsState(if (hinting) 1f else 0f, tween(600), label = "hint")

    BackHandler(onBack = onExit)
    LaunchedEffect(Unit) {
        delay(HINT_MS)
        hinting = false
    }

    Box(
        Modifier
            .fillMaxSize()
            .background(colors.background)
            .border(2.dp, if (dragging) colors.primary else Color.Transparent),
    ) {
        TouchpadSurface(remote, Modifier.fillMaxSize().systemGestureExclusion(), onDragging = { dragging = it }) {
            Box(Modifier.graphicsLayer { alpha = hintAlpha }) {
                TouchpadHint(R.string.computer_remote_full_screen_hint)
            }
        }

        CornerButton(
            R.drawable.lucide_minimize_2,
            R.string.computer_remote_exit_full_screen,
            Modifier.align(Alignment.TopEnd).safeDrawingPadding(),
            onExit,
        )
    }
}

@Composable
private fun TouchpadHint(@StringRes text: Int) {
    val colors = MaterialTheme.colorScheme

    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Icon(
            painterResource(R.drawable.lucide_mouse_pointer_2),
            contentDescription = null,
            tint = colors.onSurfaceVariant.copy(alpha = 0.5f),
            modifier = Modifier.size(28.dp),
        )
        Text(
            stringResource(text),
            fontSize = 13.sp,
            color = colors.onSurfaceVariant,
            textAlign = TextAlign.Center,
            modifier = Modifier.padding(horizontal = 24.dp),
        )
    }
}

/** A small, quiet icon button tucked into a corner of the touchpad. */
@Composable
private fun CornerButton(@DrawableRes icon: Int, @StringRes label: Int, modifier: Modifier, onClick: () -> Unit) {
    IconButton(onClick = onClick, modifier = modifier.padding(6.dp)) {
        Icon(
            painterResource(icon),
            contentDescription = stringResource(label),
            tint = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.7f),
            modifier = Modifier.size(20.dp),
        )
    }
}

/**
 * A laptop touchpad: slide to move, tap to click, two fingers to scroll, two-finger tap to right-click, and hold
 * still to start dragging. Faster slides move further, like a real touchpad.
 */
@Composable
private fun TouchpadSurface(
    remote: ComputerRemote,
    modifier: Modifier,
    onDragging: (Boolean) -> Unit,
    content: @Composable () -> Unit,
) {
    val haptics = LocalHapticFeedback.current
    val scope = rememberCoroutineScope()

    Box(
        modifier.pointerInput(remote) {
            awaitEachGesture {
                val down = awaitFirstDown(requireUnconsumed = false)
                var fingers = 1
                var travelled = 0f
                var pending = Offset.Zero
                var sentAt = down.uptimeMillis
                var now = down.uptimeMillis
                var dragging = false
                var hold: Job? = scope.launch {
                    delay(HOLD_MS)
                    haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                    remote.button(MouseButton.LEFT, true)
                    dragging = true
                    onDragging(true)
                }

                fun send(force: Boolean) {
                    val gap = if (fingers > 1) SCROLL_EVERY_MS else MOVE_EVERY_MS
                    val elapsed = now - sentAt
                    if (pending == Offset.Zero || (!force && elapsed < gap)) return

                    val dx = (pending.x / density).toDouble()
                    val dy = (pending.y / density).toDouble()
                    if (fingers > 1) {
                        remote.scroll(-dx * SCROLL_GAIN, dy * SCROLL_GAIN)
                    } else {
                        val gain = pointerGain(dx, dy, elapsed)
                        remote.move(dx * gain, dy * gain)
                    }
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
                    if (fingers > 1 || travelled > viewConfiguration.touchSlop) {
                        hold?.cancel()
                        hold = null
                    }
                    event.changes.forEach { it.consume() }
                    send(force = false)
                } while (pressed.isNotEmpty())

                hold?.cancel()
                send(force = true)

                val tapped = travelled < viewConfiguration.touchSlop && now - down.uptimeMillis < TAP_MS
                when {
                    dragging -> {
                        remote.button(MouseButton.LEFT, false)
                        onDragging(false)
                    }
                    tapped -> {
                        haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                        remote.click(if (fingers > 1) MouseButton.RIGHT else MouseButton.LEFT)
                    }
                }
            }
        },
        contentAlignment = Alignment.Center,
    ) { content() }
}

/** Speeds up quick slides so the pointer crosses a big screen in one swipe, and keeps slow ones precise. */
private fun pointerGain(dx: Double, dy: Double, elapsedMs: Long): Double {
    val speed = kotlin.math.hypot(dx, dy) / maxOf(elapsedMs, 1L)

    return 1.2 + minOf(speed, 2.0) * 1.4
}

/** One half of the touchpad's button bar: held down while pressed. */
@Composable
private fun MouseButtonArea(remote: ComputerRemote, button: MouseButton, @StringRes label: Int, modifier: Modifier) {
    val haptics = LocalHapticFeedback.current
    val description = stringResource(label)
    var down by remember { mutableStateOf(false) }

    Box(
        modifier
            .fillMaxHeight()
            .background(if (down) MaterialTheme.colorScheme.primaryContainer else Color.Transparent)
            .semantics {
                role = Role.Button
                contentDescription = description
                onClick { remote.click(button); true }
            }
            .pointerInput(remote) {
                detectTapGestures(onPress = {
                    haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                    down = true
                    remote.button(button, true)
                    tryAwaitRelease()
                    remote.button(button, false)
                    down = false
                })
            },
    )
}

/** Volume, and the previous, play and next keys, which work in any app playing on the computer. */
@Composable
private fun MediaKeys(remote: ComputerRemote) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        VolumeBar(
            onDown = { remote.press(ComputerRemote.VOLUME_DOWN) },
            onMute = { remote.press(ComputerRemote.MUTE) },
            onUp = { remote.press(ComputerRemote.VOLUME_UP) },
        )
        RoundKey(R.drawable.lucide_skip_back, stringResource(R.string.computer_remote_previous_track)) { remote.press(ComputerRemote.PREVIOUS_TRACK) }
        RoundKey(R.drawable.lucide_play, stringResource(R.string.remote_play_pause), solid = true) { remote.press(ComputerRemote.PLAY_PAUSE) }
        RoundKey(R.drawable.lucide_skip_forward, stringResource(R.string.computer_remote_next_track)) { remote.press(ComputerRemote.NEXT_TRACK) }
    }
}

/**
 * A field that types on the computer, the keys a phone keyboard lacks, and modifiers that stay held for the next key,
 * so Ctrl then C copies. Labels follow the computer: Command and Option on a Mac, Ctrl and Win on a PC.
 */
@Composable
private fun ComputerKeyboard(remote: ComputerRemote, isMac: Boolean) {
    val held = remember { mutableStateListOf<String>() }
    val modifiers = if (isMac) {
        listOf(ComputerRemote.META to "⌘", ComputerRemote.ALT to "⌥", ComputerRemote.SHIFT to "⇧", ComputerRemote.CONTROL to "⌃")
    } else {
        listOf(ComputerRemote.CONTROL to "Ctrl", ComputerRemote.ALT to "Alt", ComputerRemote.SHIFT to "Shift", ComputerRemote.META to "Win")
    }
    val press = { code: String ->
        remote.press(code, held.toList())
        held.clear()
    }

    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        LiveTypeField(
            stringResource(R.string.computer_remote_type),
            onText = remote::type,
            onDelete = { remote.press("Backspace") },
            onEnter = { press("Enter") },
            intercept = { added ->
                val shortcut = held.isNotEmpty()

                if (shortcut) {
                    added.forEach { remote.press(it, held.toList()) }
                    held.clear()
                }
                shortcut
            },
        )

        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            modifiers.forEach { (code, label) ->
                val on = code in held
                KeyCap(label, Modifier.weight(1f), on = on) { if (on) held.remove(code) else held.add(code) }
            }
        }

        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            KeyCap("Esc", Modifier.weight(1f)) { press("Escape") }
            KeyCap("Tab", Modifier.weight(1f)) { press("Tab") }
            KeyCap(R.drawable.lucide_chevron_left, stringResource(R.string.remote_left), Modifier.weight(1f)) { press("ArrowLeft") }
            KeyCap(R.drawable.lucide_chevron_up, stringResource(R.string.remote_up), Modifier.weight(1f)) { press("ArrowUp") }
            KeyCap(R.drawable.lucide_chevron_down, stringResource(R.string.remote_down), Modifier.weight(1f)) { press("ArrowDown") }
            KeyCap(R.drawable.lucide_chevron_right, stringResource(R.string.remote_right), Modifier.weight(1f)) { press("ArrowRight") }
            KeyCap(R.drawable.lucide_delete, stringResource(R.string.computer_remote_backspace), Modifier.weight(1f)) { press("Backspace") }
        }
    }
}

@Composable
private fun KeyCap(label: String, modifier: Modifier, on: Boolean = false, onPress: () -> Unit) {
    KeyCapFrame(label, modifier, on, onPress) {
        Text(label, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, maxLines = 1)
    }
}

@Composable
private fun KeyCap(@DrawableRes icon: Int, label: String, modifier: Modifier, onPress: () -> Unit) {
    KeyCapFrame(label, modifier, on = false, onPress) {
        Icon(painterResource(icon), contentDescription = null, modifier = Modifier.size(20.dp))
    }
}

/** A raised key that repeats while held. Modifiers light up while they are held for the next key. */
@Composable
private fun KeyCapFrame(label: String, modifier: Modifier, on: Boolean, onPress: () -> Unit, content: @Composable () -> Unit) {
    val colors = MaterialTheme.colorScheme
    val haptics = LocalHapticFeedback.current
    val scope = rememberCoroutineScope()
    var down by remember { mutableStateOf(false) }
    val shape = RoundedCornerShape(12.dp)

    Box(
        modifier
            .height(46.dp)
            .scale(if (down) 0.94f else 1f)
            .clip(shape)
            .background(if (on) colors.primaryContainer else colors.surface)
            .border(1.dp, if (on) colors.primary else colors.outlineVariant, shape)
            .semantics {
                role = Role.Button
                contentDescription = label
                selected = on
                onClick { onPress(); true }
            }
            .repeatingPress(scope, onPress = {
                haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                onPress()
            }, onDown = { down = it }),
        contentAlignment = Alignment.Center,
    ) { content() }
}

/** A big Next, a smaller Back, and keys to start, black out and end the show. */
@Composable
private fun SlideControls(remote: ComputerRemote, modifier: Modifier) {
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(16.dp), horizontalAlignment = Alignment.CenterHorizontally) {
        Row(Modifier.weight(1f).fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            SlideKey(R.drawable.lucide_chevron_left, R.string.computer_remote_previous_slide, gold = false, modifier = Modifier.weight(1f)) { remote.previousSlide() }
            SlideKey(R.drawable.lucide_chevron_right, R.string.computer_remote_next_slide, gold = true, modifier = Modifier.weight(2f)) { remote.nextSlide() }
        }

        Row(horizontalArrangement = Arrangement.spacedBy(28.dp)) {
            RoundKey(R.drawable.lucide_presentation, stringResource(R.string.computer_remote_start_slides)) { remote.startSlides() }
            RoundKey(R.drawable.lucide_monitor_off, stringResource(R.string.computer_remote_black_screen)) { remote.blackScreen() }
            RoundKey(R.drawable.lucide_x, stringResource(R.string.computer_remote_end_slides)) { remote.endSlides() }
        }

        Text(stringResource(R.string.computer_remote_slides_hint), fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun SlideKey(@DrawableRes icon: Int, @StringRes label: Int, gold: Boolean, modifier: Modifier, onPress: () -> Unit) {
    val colors = MaterialTheme.colorScheme
    val haptics = LocalHapticFeedback.current
    val description = stringResource(label)
    var down by remember { mutableStateOf(false) }
    val shape = RoundedCornerShape(32.dp)
    val tint = if (gold) FlyyLink.colors.onHero else colors.onSurface

    Column(
        modifier
            .fillMaxHeight()
            .scale(if (down) 0.97f else 1f)
            .clip(shape)
            .then(if (gold) Modifier.background(Toneflix.goldGradient) else Modifier.background(colors.surface).border(1.dp, colors.outlineVariant, shape))
            .semantics {
                role = Role.Button
                contentDescription = description
                onClick { onPress(); true }
            }
            .pointerInput(Unit) {
                detectTapGestures(onPress = {
                    down = true
                    haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                    onPress()
                    tryAwaitRelease()
                    down = false
                })
            },
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Icon(painterResource(icon), contentDescription = null, tint = tint, modifier = Modifier.size(if (gold) 56.dp else 40.dp))
        Text(description, color = tint, fontSize = 16.sp, fontWeight = FontWeight.Bold)
    }
}

private const val HOLD_MS = 450L
private const val SCROLL_EVERY_MS = 50L
private const val SCROLL_GAIN = 2.0
private const val HINT_MS = 2500L
