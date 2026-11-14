package com.toneflix.flyylink

import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.GestureDescription
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.graphics.Path
import android.os.Build
import android.provider.Settings
import android.view.WindowManager
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo
import android.view.View
import com.toneflix.flyylink.protocol.ScreenInput
import com.toneflix.flyylink.protocol.TvRemote

/**
 * Plays the Mac's clicks, drags and typing on the phone while its screen is mirrored, and a phone's
 * remote presses on a TV. Android only allows this through an accessibility service the user turns on.
 */
class ScreenControlService : AccessibilityService() {
    /** Whether the user turned the service on, and whether Android is actually running it. */
    enum class Status { OFF, STOPPED, ON }

    private val pointer by lazy { TvPointer(this, ::screenSize) }

    override fun onServiceConnected() {
        instance = this
    }

    override fun onUnbind(intent: Intent?): Boolean {
        instance = null
        pointer.remove()
        return super.onUnbind(intent)
    }

    override fun onDestroy() {
        instance = null
        pointer.remove()
        super.onDestroy()
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {}

    override fun onInterrupt() {}

    private fun play(input: ScreenInput) {
        when (input) {
            is ScreenInput.Tap -> stroke(input.x, input.y, input.x, input.y, TAP_MS)
            is ScreenInput.LongPress -> stroke(input.x, input.y, input.x, input.y, LONG_PRESS_MS)
            is ScreenInput.Swipe -> stroke(input.x, input.y, input.toX, input.toY, input.durationMs)
            is ScreenInput.Text -> focusedField()?.type(input.text)
            is ScreenInput.Key -> key(input)
        }
    }

    /** One finger from one point to another, as fractions of the screen; a tap is a stroke that doesn't move. */
    private fun stroke(x: Double, y: Double, toX: Double, toY: Double, durationMs: Long) {
        val (width, height) = screenSize()

        strokePixels((x * width).toFloat(), (y * height).toFloat(), (toX * width).toFloat(), (toY * height).toFloat(), durationMs)
    }

    private fun strokePixels(x: Float, y: Float, toX: Float, toY: Float, durationMs: Long) {
        val path = Path().apply {
            moveTo(x, y)
            lineTo(toX, toY)
        }
        val gesture = GestureDescription.Builder()
            .addStroke(GestureDescription.StrokeDescription(path, 0, durationMs.coerceAtLeast(1)))
            .build()

        dispatchGesture(gesture, null, null)
    }

    private fun key(input: ScreenInput.Key) {
        when (input.key) {
            ScreenInput.BACK -> performGlobalAction(GLOBAL_ACTION_BACK)
            ScreenInput.HOME -> performGlobalAction(GLOBAL_ACTION_HOME)
            ScreenInput.RECENTS -> performGlobalAction(GLOBAL_ACTION_RECENTS)
            ScreenInput.NOTIFICATIONS -> performGlobalAction(GLOBAL_ACTION_NOTIFICATIONS)
            else -> focusedField()?.press(input.key, input.select)
        }
    }

    private fun play(press: TvRemote) {
        when (press) {
            is TvRemote.Move -> pointer.move(press.dx, press.dy)
            is TvRemote.Click -> click()
            is TvRemote.Scroll -> scroll(press.dx, press.dy)
            is TvRemote.Text -> focusedField()?.type(press.text)
            is TvRemote.Key -> when (press.key) {
                TvRemote.UP -> move(GLOBAL_ACTION_DPAD_UP, View.FOCUS_UP)
                TvRemote.DOWN -> move(GLOBAL_ACTION_DPAD_DOWN, View.FOCUS_DOWN)
                TvRemote.LEFT -> move(GLOBAL_ACTION_DPAD_LEFT, View.FOCUS_LEFT)
                TvRemote.RIGHT -> move(GLOBAL_ACTION_DPAD_RIGHT, View.FOCUS_RIGHT)
                TvRemote.SELECT -> if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                    performGlobalAction(GLOBAL_ACTION_DPAD_CENTER)
                } else {
                    focused()?.performAction(AccessibilityNodeInfo.ACTION_CLICK)
                }
                TvRemote.MENU -> focused()?.performAction(AccessibilityNodeInfo.ACTION_LONG_CLICK)
                TvRemote.BACK -> performGlobalAction(GLOBAL_ACTION_BACK)
                TvRemote.HOME -> performGlobalAction(GLOBAL_ACTION_HOME)
                TvRemote.DELETE -> focusedField()?.press(ScreenInput.DELETE, select = false)
                TvRemote.ENTER -> focusedField()?.press(ScreenInput.ENTER, select = false)
            }
        }
    }

    /** Taps where the pointer is. A click while it's hidden only brings it back. */
    private fun click() {
        val (x, y) = pointer.position ?: return pointer.show()

        pointer.show()
        strokePixels(x, y, x, y, TAP_MS)
    }

    /** Swipes from the pointer the way the two fingers went, so pages follow them. */
    private fun scroll(dx: Double, dy: Double) {
        val (width, height) = screenSize()
        val (x, y) = pointer.position ?: (width / 2f to height / 2f)
        val scale = resources.displayMetrics.density * SCROLL_SPEED

        val toX = (x + dx * scale).toFloat().coerceIn(1f, width - 2f)
        val toY = (y + dy * scale).toFloat().coerceIn(1f, height - 2f)
        if (toX == x && toY == y) return

        strokePixels(x, y, toX, toY, SCROLL_MS)
    }

    /** Moves focus like the TV remote's arrows. Before Android 13 there is no D-pad action, so it searches for the next item. */
    private fun move(action: Int, direction: Int) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            performGlobalAction(action)
            return
        }

        val next = focused()?.focusSearch(direction) ?: firstFocusable(rootInActiveWindow)
        next?.performAction(AccessibilityNodeInfo.ACTION_FOCUS)
    }

    private fun focused(): AccessibilityNodeInfo? = rootInActiveWindow?.findFocus(AccessibilityNodeInfo.FOCUS_INPUT)

    private fun firstFocusable(node: AccessibilityNodeInfo?): AccessibilityNodeInfo? {
        node ?: return null
        if (node.isFocusable && node.isVisibleToUser) return node

        return (0 until node.childCount).firstNotNullOfOrNull { firstFocusable(node.getChild(it)) }
    }

    private fun focusedField() = rootInActiveWindow?.findFocus(AccessibilityNodeInfo.FOCUS_INPUT)?.let(::FocusedField)

    private fun screenSize(): Pair<Int, Int> {
        val windows = getSystemService(WindowManager::class.java)

        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            windows.maximumWindowMetrics.bounds.let { it.width() to it.height() }
        } else {
            val metrics = android.util.DisplayMetrics()
            @Suppress("DEPRECATION")
            windows.defaultDisplay.getRealMetrics(metrics)
            metrics.widthPixels to metrics.heightPixels
        }
    }

    companion object {
        private const val TAP_MS = 40L
        private const val LONG_PRESS_MS = 700L
        private const val SCROLL_MS = 120L
        private const val SCROLL_SPEED = 2.0

        @Volatile private var instance: ScreenControlService? = null

        fun perform(input: ScreenInput) {
            val service = instance ?: return
            android.os.Handler(android.os.Looper.getMainLooper()).post { service.play(input) }
        }

        fun remote(press: TvRemote) {
            val service = instance ?: return
            android.os.Handler(android.os.Looper.getMainLooper()).post { service.play(press) }
        }

        /**
         * Android stops the service when the app is updated or killed and, on many phones, keeps it switched on
         * but "Not working" until the user turns it off and on again. That shows here as [Status.STOPPED].
         */
        fun status(context: Context): Status = when {
            instance != null -> Status.ON
            isEnabled(context) -> Status.STOPPED
            else -> Status.OFF
        }

        private fun isEnabled(context: Context): Boolean {
            val enabled = Settings.Secure.getString(context.contentResolver, Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES).orEmpty()
            val me = ComponentName(context, ScreenControlService::class.java).flattenToString()
            return enabled.split(':').any { it.equals(me, ignoreCase = true) }
        }

        /**
         * Opens Accessibility settings. Some TVs, such as Hisense, don't answer the standard link, so the Android TV
         * settings app's Accessibility screen comes first, and plain Settings is the last resort.
         */
        fun openSettings(context: Context): Boolean = settingsScreens.any { screen ->
            screen.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            screen.resolveActivity(context.packageManager) != null && runCatching { context.startActivity(screen) }.isSuccess
        }

        private val settingsScreens: List<Intent>
            get() = listOf(
                Intent().setComponent(ComponentName("com.android.tv.settings", "com.android.tv.settings.accessibility.AccessibilityActivity")),
                Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS),
                Intent(Settings.ACTION_SETTINGS),
            )
    }
}
