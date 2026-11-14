package com.toneflix.flyylink

import android.content.Context
import android.graphics.PixelFormat
import android.os.Handler
import android.os.Looper
import android.view.Gravity
import android.view.WindowManager
import android.widget.ImageView
import kotlin.math.hypot
import kotlin.math.min

/**
 * The pointer a phone's touchpad moves on a TV. [ScreenControlService] draws it over every app, since
 * accessibility overlays need no other permission, and it hides after a few seconds without moving.
 */
class TvPointer(private val context: Context, private val screen: () -> Pair<Int, Int>) {
    private val windows = context.getSystemService(WindowManager::class.java)
    private val main = Handler(Looper.getMainLooper())
    private val density = context.resources.displayMetrics.density
    private val hide = Runnable { remove() }
    private var view: ImageView? = null
    private var x = -1f
    private var y = -1f

    private val params = WindowManager.LayoutParams(
        WindowManager.LayoutParams.WRAP_CONTENT,
        WindowManager.LayoutParams.WRAP_CONTENT,
        WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
        WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE or WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
        PixelFormat.TRANSLUCENT,
    ).apply { gravity = Gravity.TOP or Gravity.START }

    /** Where the pointer's tip is on the screen, in pixels, or null while it's hidden. */
    val position: Pair<Float, Float>? get() = if (view != null) x to y else null

    /** Moves by a distance in the phone's dp, faster the quicker the finger goes. */
    fun move(dx: Double, dy: Double) {
        val (width, height) = screen()
        val speed = density * SPEED * (1 + min(hypot(dx, dy) / 15, 2.0))

        show()
        x = (x + dx * speed).toFloat().coerceIn(0f, width - 1f)
        y = (y + dy * speed).toFloat().coerceIn(0f, height - 1f)
        place()
    }

    /** Shows the pointer where it was, or in the middle of the screen the first time. */
    fun show() {
        if (x < 0 || y < 0) {
            val (width, height) = screen()
            x = width / 2f
            y = height / 2f
        }

        if (view == null) {
            val created = ImageView(context).apply { setImageResource(R.drawable.tv_pointer) }
            runCatching { windows.addView(created, params.apply { this.x = this@TvPointer.x.toInt(); this.y = this@TvPointer.y.toInt() }) }
                .onSuccess { view = created }
        }

        main.removeCallbacks(hide)
        main.postDelayed(hide, HIDE_MS)
    }

    fun remove() {
        main.removeCallbacks(hide)
        view?.let { runCatching { windows.removeView(it) } }
        view = null
    }

    private fun place() {
        val shown = view ?: return

        params.x = x.toInt()
        params.y = y.toInt()
        runCatching { windows.updateViewLayout(shown, params) }
    }

    private companion object {
        /** TV pixels per phone dp at a slow finger, before the TV's own density. */
        const val SPEED = 1.4
        const val HIDE_MS = 8_000L
    }
}
