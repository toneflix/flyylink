package com.toneflix.flyylink

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import android.widget.Toast

/** "Copy to computer" in the menu that shows over selected text, in any app. Puts that text on every paired computer's clipboard. */
class CopyToComputerActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val text = intent.getCharSequenceExtra(Intent.EXTRA_PROCESS_TEXT)?.toString()
        if (text.isNullOrEmpty()) {
            finish()
            return
        }

        val sent = ClipboardPlugin.sendText(text)
        if (sent == 0) LinkService.start(this)

        val message = when {
            sent == 0 -> getString(R.string.share_not_connected)
            sent > 1 -> getString(R.string.clipboard_sent_many, sent)
            else -> getString(R.string.clipboard_sent)
        }

        Toast.makeText(this, message, Toast.LENGTH_SHORT).show()
        finish()
    }
}
