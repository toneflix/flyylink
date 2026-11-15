package com.toneflix.flyylink.ui

import android.widget.Toast
import androidx.annotation.DrawableRes
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
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
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.toneflix.flyylink.DeviceState
import com.toneflix.flyylink.DeviceStore
import com.toneflix.flyylink.FilesPlugin
import com.toneflix.flyylink.R
import com.toneflix.flyylink.ShareSender

/** Slides up when a Mac asks to pair, showing the code to compare. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PairSheet() {
    val request by DeviceStore.pairRequest.collectAsStateWithLifecycle()
    val device = request ?: return
    val state = rememberModalBottomSheetState(skipPartiallyExpanded = true)

    ModalBottomSheet(
        onDismissRequest = { withDevice(device.id) { it.reject() } },
        sheetState = state,
        containerColor = MaterialTheme.colorScheme.surface,
    ) {
        Column(
            Modifier
                .fillMaxWidth()
                .navigationBarsPadding()
                .padding(start = 24.dp, end = 24.dp, bottom = 24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(22.dp),
        ) {
            DeviceArt(device.type, 150.dp, Modifier.padding(top = 8.dp))

            Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(
                    stringResource(R.string.pair_title, device.name),
                    fontSize = 24.sp,
                    fontWeight = FontWeight.Bold,
                    letterSpacing = (-0.5).sp,
                    textAlign = TextAlign.Center,
                )
                Text(
                    stringResource(R.string.pair_hint),
                    fontSize = 15.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                )
            }

            device.code?.let { CodeDigits(it) }

            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Button(
                    onClick = { withDevice(device.id) { it.accept() } },
                    modifier = Modifier.fillMaxWidth().height(56.dp),
                ) { Text(stringResource(R.string.pair), fontSize = 16.sp, fontWeight = FontWeight.SemiBold) }
                OutlinedButton(
                    onClick = { withDevice(device.id) { it.reject() } },
                    modifier = Modifier.fillMaxWidth().height(56.dp),
                    border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
                ) {
                    Text(stringResource(R.string.not_now), fontSize = 16.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface)
                }
            }
        }
    }
}

/** Sends a link (or any text) to the Mac. A web link opens there in the browser. */
@Composable
fun LinkDialog(mac: DeviceState, initial: String, onDismiss: () -> Unit) {
    val context = LocalContext.current
    var text by remember { mutableStateOf(initial) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.link_title, mac.name)) },
        text = {
            OutlinedTextField(
                value = text,
                onValueChange = { text = it },
                placeholder = { Text("https://") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
        },
        confirmButton = {
            TextButton(
                enabled = text.isNotBlank(),
                onClick = {
                    if (!ShareSender.sendText(context, mac.id, text)) {
                        Toast.makeText(context, R.string.share_not_connected, Toast.LENGTH_SHORT).show()
                    }
                    onDismiss()
                },
            ) { Text(stringResource(R.string.send)) }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.cancel)) } },
    )
}

/**
 * Google Play's prominent disclosure for the accessibility service: says what it does and what it reads
 * before Android's Accessibility settings open. Only [onAccept] leads there.
 */
@Composable
fun ControlDisclosureSheet(onAccept: () -> Unit, onDecline: () -> Unit) {
    DisclosureSheet(
        icon = R.drawable.lucide_pointer,
        title = stringResource(R.string.control_disclosure_title),
        intro = stringResource(R.string.control_disclosure_intro),
        footnote = stringResource(R.string.control_disclosure_settings),
        onAccept = onAccept,
        onDecline = onDecline,
    ) {
        DisclosureGroup(stringResource(R.string.control_disclosure_does)) {
            DisclosurePoint(R.drawable.lucide_pointer, stringResource(R.string.control_disclosure_taps))
            DisclosurePoint(R.drawable.lucide_keyboard, stringResource(R.string.control_disclosure_typing))
            DisclosurePoint(R.drawable.lucide_cast, stringResource(R.string.control_disclosure_only))
        }
        DisclosureGroup(stringResource(R.string.control_disclosure_data)) {
            DisclosurePoint(R.drawable.lucide_eye_off, stringResource(R.string.control_disclosure_private))
            DisclosurePoint(R.drawable.lucide_lock, stringResource(R.string.control_disclosure_local))
        }
    }
}

/**
 * Google Play's prominent disclosure for location in the background: explains Find my phone before
 * Android's location prompt. Only [onAccept] leads to the prompt.
 */
@Composable
fun LocationDisclosureSheet(onAccept: () -> Unit, onDecline: () -> Unit) {
    DisclosureSheet(
        icon = R.drawable.lucide_map_pin,
        title = stringResource(R.string.location_disclosure_title),
        intro = stringResource(R.string.location_disclosure_intro),
        footnote = stringResource(R.string.location_disclosure_prompt),
        onAccept = onAccept,
        onDecline = onDecline,
    ) {
        DisclosureGroup(stringResource(R.string.control_disclosure_does)) {
            DisclosurePoint(R.drawable.lucide_map_pin, stringResource(R.string.location_disclosure_find))
            DisclosurePoint(R.drawable.lucide_moon, stringResource(R.string.location_disclosure_background))
        }
        DisclosureGroup(stringResource(R.string.control_disclosure_data)) {
            DisclosurePoint(R.drawable.lucide_eye_off, stringResource(R.string.location_disclosure_private))
            DisclosurePoint(R.drawable.lucide_lock, stringResource(R.string.location_disclosure_local))
        }
    }
}

/**
 * Disclosure for the SMS, call log, phone and contacts permissions: explains texts and calls on the computer
 * before Android asks for them. Only [onAccept] leads to the prompts.
 */
@Composable
fun PhoneDisclosureSheet(onAccept: () -> Unit, onDecline: () -> Unit) {
    DisclosureSheet(
        icon = R.drawable.lucide_message_square,
        title = stringResource(R.string.phone_disclosure_title),
        intro = stringResource(R.string.phone_disclosure_intro),
        footnote = stringResource(R.string.phone_disclosure_prompt),
        onAccept = onAccept,
        onDecline = onDecline,
    ) {
        DisclosureGroup(stringResource(R.string.control_disclosure_does)) {
            DisclosurePoint(R.drawable.lucide_message_square, stringResource(R.string.phone_disclosure_texts))
            DisclosurePoint(R.drawable.lucide_phone, stringResource(R.string.phone_disclosure_calls))
            DisclosurePoint(R.drawable.lucide_smartphone, stringResource(R.string.phone_disclosure_contacts))
        }
        DisclosureGroup(stringResource(R.string.control_disclosure_data)) {
            DisclosurePoint(R.drawable.lucide_lock, stringResource(R.string.phone_disclosure_local))
        }
    }
}

/**
 * Disclosure for All files access (storage permissions before Android 11): explains browsing the phone's files from
 * a computer before Android's settings or prompt. Only [onAccept] leads there.
 */
@Composable
fun FilesDisclosureSheet(onAccept: () -> Unit, onDecline: () -> Unit) {
    DisclosureSheet(
        icon = R.drawable.lucide_folder_open,
        title = stringResource(R.string.files_disclosure_title),
        intro = stringResource(R.string.files_disclosure_intro),
        footnote = stringResource(if (FilesPlugin.needsSettings) R.string.files_disclosure_settings else R.string.files_disclosure_prompt),
        onAccept = onAccept,
        onDecline = onDecline,
    ) {
        DisclosureGroup(stringResource(R.string.control_disclosure_does)) {
            DisclosurePoint(R.drawable.lucide_folder_open, stringResource(R.string.files_disclosure_browse))
            DisclosurePoint(R.drawable.lucide_file_up, stringResource(R.string.files_disclosure_change))
        }
        DisclosureGroup(stringResource(R.string.control_disclosure_data)) {
            DisclosurePoint(R.drawable.lucide_lock, stringResource(R.string.files_disclosure_local))
        }
    }
}

/** Explains a sensitive permission in plain language, with an explicit choice, before Android asks for it. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun DisclosureSheet(
    @DrawableRes icon: Int,
    title: String,
    intro: String,
    footnote: String,
    onAccept: () -> Unit,
    onDecline: () -> Unit,
    sections: @Composable () -> Unit,
) {
    val state = rememberModalBottomSheetState(skipPartiallyExpanded = true)

    ModalBottomSheet(
        onDismissRequest = onDecline,
        sheetState = state,
        containerColor = MaterialTheme.colorScheme.surface,
    ) {
        Column(
            Modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .navigationBarsPadding()
                .padding(start = 24.dp, end = 24.dp, bottom = 24.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp),
        ) {
            Column(
                Modifier.fillMaxWidth(),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Box(
                    Modifier.size(64.dp).background(MaterialTheme.colorScheme.primaryContainer, RoundedCornerShape(20.dp)),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(
                        painterResource(icon),
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.size(30.dp),
                    )
                }
                Text(
                    title,
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    letterSpacing = (-0.4).sp,
                    textAlign = TextAlign.Center,
                )
                Text(
                    intro,
                    fontSize = 14.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                )
            }

            sections()

            Text(footnote, fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)

            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Button(
                    onClick = onAccept,
                    modifier = Modifier.fillMaxWidth().height(56.dp),
                ) { Text(stringResource(R.string.control_disclosure_accept), fontSize = 16.sp, fontWeight = FontWeight.SemiBold) }
                OutlinedButton(
                    onClick = onDecline,
                    modifier = Modifier.fillMaxWidth().height(56.dp),
                    border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
                ) {
                    Text(
                        stringResource(R.string.control_disclosure_decline),
                        fontSize = 16.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = MaterialTheme.colorScheme.onSurface,
                    )
                }
            }
        }
    }
}

@Composable
private fun DisclosureGroup(title: String, points: @Composable () -> Unit) {
    Column(
        Modifier
            .fillMaxWidth()
            .background(MaterialTheme.colorScheme.surfaceVariant, MaterialTheme.shapes.medium)
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text(title, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurfaceVariant)
        points()
    }
}

@Composable
private fun DisclosurePoint(@DrawableRes icon: Int, text: String) {
    Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        Icon(
            painterResource(icon),
            contentDescription = null,
            tint = MaterialTheme.colorScheme.primary,
            modifier = Modifier.padding(top = 1.dp).size(18.dp),
        )
        Text(text, fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurface)
    }
}
