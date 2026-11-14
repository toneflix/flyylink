package com.toneflix.flyylink.ui

import androidx.annotation.DrawableRes
import android.app.Activity
import android.content.ClipboardManager
import android.content.Context
import android.net.Uri
import android.text.format.DateUtils
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.snapshotFlow
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.layout.layout
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.toneflix.flyylink.DeviceState
import com.toneflix.flyylink.DeviceStore
import com.toneflix.flyylink.CallPlugin
import com.toneflix.flyylink.ClipboardPlugin
import com.toneflix.flyylink.SmsPlugin
import com.toneflix.flyylink.LinkService
import com.toneflix.flyylink.LocationPlugin
import com.toneflix.flyylink.NearbyBeacon
import com.toneflix.flyylink.PhotosPlugin
import com.toneflix.flyylink.ScreenControlService
import com.toneflix.flyylink.ScreenPlugin
import com.toneflix.flyylink.NotificationPlugin
import com.toneflix.flyylink.R
import com.toneflix.flyylink.Recent
import com.toneflix.flyylink.RecentItem
import com.toneflix.flyylink.TransferText
import com.toneflix.flyylink.ShareActivity
import com.toneflix.flyylink.SwitcherStyle
import com.toneflix.flyylink.openIntent
import com.toneflix.flyylink.RemoteActivity
import com.toneflix.flyylink.protocol.DeviceType
import com.toneflix.flyylink.protocol.PairState
import kotlinx.coroutines.launch
import kotlin.concurrent.thread

/** Runs a device action off the main thread, since it writes to the network. */
fun withDevice(id: String, action: (com.toneflix.flyylink.protocol.Device) -> Unit) {
    thread(isDaemon = true) { DeviceStore.find(id)?.let(action) }
}

@Composable
fun HomeScreen(phoneName: String) {
    val devices by DeviceStore.states.collectAsStateWithLifecycle()
    val focusId by DeviceStore.focusId.collectAsStateWithLifecycle()
    val style by DeviceStore.switcherStyle.collectAsStateWithLifecycle()
    val recent by Recent.items.collectAsStateWithLifecycle()
    val macs = devices.filter { it.pairState == PairState.PAIRED }
    val mac = DeviceStore.focused(devices, focusId)
    val others = devices.filter { it.pairState != PairState.PAIRED && (it.canPair || it.canFlyyDrop) }

    Box(Modifier.fillMaxSize().background(MaterialTheme.colorScheme.background)) {
        Column(
            Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .statusBarsPadding()
                .navigationBarsPadding()
                .padding(horizontal = 16.dp)
                .padding(bottom = 24.dp)
                .animateContentSize(),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Header(macs, style)

            val carousel = macs.size > 1 && style == SwitcherStyle.CAROUSEL
            if (macs.size > 1 && !carousel) MacSwitcher(macs, mac?.id)

            if (mac != null) {
                if (carousel) MacCarousel(macs, mac.id) else ConnectedHero(mac, sharedWithOthers = macs.size > 1)
                QuickTiles(mac, macCount = macs.size)
            } else {
                SearchingHero(hasNearby = others.any { it.canPair })
            }

            SetupCards()

            FlyyDropCard()

            PlayOnTvCard()

            if (others.isNotEmpty()) Nearby(others)

            if (recent.isNotEmpty()) RecentSection(recent)

            Text(
                stringResource(R.string.home_this_phone, phoneName),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 4.dp),
            )
        }

        PairSheet()
        FlyyDropSheet()
    }
}

@Composable
private fun Header(macs: List<DeviceState>, style: SwitcherStyle) {
    val context = LocalContext.current
    var menu by remember { mutableStateOf(false) }

    Row(Modifier.padding(top = 18.dp, start = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        Image(painterResource(R.drawable.ic_flyylink_logo), contentDescription = null, modifier = Modifier.size(30.dp))
        Spacer(Modifier.width(8.dp))
        Text("FlyyLink", fontSize = 28.sp, fontWeight = FontWeight.Bold, letterSpacing = (-0.6).sp, modifier = Modifier.weight(1f))
        Box {
            IconButton(onClick = { menu = true }) {
                Icon(painterResource(R.drawable.lucide_settings), contentDescription = stringResource(R.string.home_settings), tint = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                DropdownMenuItem(
                    text = { Text(stringResource(R.string.home_notification_access)) },
                    onClick = {
                        menu = false
                        context.startActivity(NotificationPlugin.accessSettingsIntent())
                    },
                )
                if (macs.size > 1) {
                    HorizontalDivider()
                    listOf(SwitcherStyle.CAROUSEL to R.string.home_switch_carousel, SwitcherStyle.ROW to R.string.home_switch_row).forEach { (option, label) ->
                        DropdownMenuItem(
                            text = { Text(stringResource(label)) },
                            trailingIcon = {
                                if (option == style) Icon(painterResource(R.drawable.lucide_circle_check), contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                            },
                            onClick = {
                                menu = false
                                DeviceStore.useSwitcher(context, option)
                            },
                        )
                    }
                    HorizontalDivider()
                }
                macs.forEach { mac ->
                    DropdownMenuItem(
                        text = { Text(stringResource(R.string.home_unpair, mac.name)) },
                        onClick = {
                            menu = false
                            withDevice(mac.id) { it.unpair() }
                        },
                    )
                }
                DropdownMenuItem(
                    text = { Text(stringResource(R.string.home_stop)) },
                    onClick = {
                        menu = false
                        LinkService.stop(context)
                        (context as? Activity)?.finish()
                    },
                )
            }
        }
    }
}

/** The paired Macs, all connected at once. Tapping one shows it in the hero and points the tiles at it. */
@Composable
private fun MacSwitcher(macs: List<DeviceState>, focusedId: String?) {
    val context = LocalContext.current

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            SectionTitle(stringResource(R.string.home_your_macs), Modifier.weight(1f))
            Text(
                stringResource(R.string.home_macs_connected, macs.size),
                fontSize = 13.sp,
                fontWeight = FontWeight.SemiBold,
                color = FlyyLink.colors.ok,
                modifier = Modifier.padding(horizontal = 4.dp),
            )
        }

        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            macs.forEach { mac ->
                MacChip(mac, isFocused = mac.id == focusedId) { DeviceStore.focus(context, mac.id) }
            }
        }
    }
}

/**
 * The shown Mac's card with the next one peeking at the screen edge. Swiping or tapping the peek
 * moves on, and the pages wrap round so the Macs cycle forever.
 */
@Composable
private fun MacCarousel(macs: List<DeviceState>, focusedId: String) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val ordered = macs.sortedWith(compareBy({ it.name }, { it.id }))

    key(ordered.map { it.id }) {
        val middle = Int.MAX_VALUE / 2
        val start = middle - middle % ordered.size + ordered.indexOfFirst { it.id == focusedId }.coerceAtLeast(0)
        val pager = rememberPagerState(initialPage = start) { Int.MAX_VALUE }

        LaunchedEffect(pager) {
            snapshotFlow { pager.settledPage }.collect { page -> DeviceStore.focus(context, ordered[page % ordered.size].id) }
        }

        HorizontalPager(
            state = pager,
            contentPadding = PaddingValues(end = CAROUSEL_BLEED + CAROUSEL_PEEK),
            pageSpacing = 10.dp,
            beyondViewportPageCount = 1,
            modifier = Modifier.bleedToEnd(CAROUSEL_BLEED),
        ) { page ->
            val mac = ordered[page % ordered.size]
            val isShown = page == pager.currentPage
            val showLabel = stringResource(R.string.home_show_mac, mac.name)

            ConnectedHero(
                mac,
                sharedWithOthers = true,
                modifier = Modifier
                    .alpha(if (isShown) 1f else 0.82f)
                    .then(
                        if (isShown) Modifier else Modifier
                            .semantics { contentDescription = showLabel }
                            .clickable { scope.launch { pager.animateScrollToPage(page) } },
                    ),
            )
        }
    }
}

private val CAROUSEL_BLEED = 16.dp
private val CAROUSEL_PEEK = 28.dp

/** Lets the content run past the screen padding to the edge. */
private fun Modifier.bleedToEnd(extra: androidx.compose.ui.unit.Dp) = layout { measurable, constraints ->
    val px = extra.roundToPx()
    val placeable = measurable.measure(constraints.copy(minWidth = constraints.minWidth + px, maxWidth = constraints.maxWidth + px))

    layout(constraints.maxWidth, placeable.height) { placeable.place(0, 0) }
}

@Composable
private fun MacChip(mac: DeviceState, isFocused: Boolean, onClick: () -> Unit) {
    val shape = RoundedCornerShape(18.dp)
    val colors = MaterialTheme.colorScheme

    Row(
        Modifier
            .background(if (isFocused) colors.primaryContainer else colors.surface, shape)
            .then(if (isFocused) Modifier.border(2.dp, colors.primary, shape) else Modifier)
            .clickable(onClick = onClick)
            .padding(start = 12.dp, end = 16.dp, top = 10.dp, bottom = 10.dp),
        horizontalArrangement = Arrangement.spacedBy(10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(contentAlignment = Alignment.BottomEnd) {
            DeviceArt(mac.type, 36.dp)
            Box(Modifier.size(10.dp).background(colors.surface, CircleShape).padding(2.dp).background(FlyyLink.colors.ok, CircleShape))
        }
        Column {
            Text(mac.name, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(
                stringResource(if (isFocused) R.string.home_mac_shown else R.string.home_mac_live),
                fontSize = 12.sp,
                color = if (isFocused) colors.onPrimaryContainer else colors.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun ConnectedHero(mac: DeviceState, sharedWithOthers: Boolean, modifier: Modifier = Modifier) {
    val colors = FlyyLink.colors

    Column(
        modifier
            .fillMaxWidth()
            .background(colors.hero, MaterialTheme.shapes.large)
            .padding(22.dp),
        verticalArrangement = Arrangement.spacedBy(18.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Pill {
                Box(Modifier.size(8.dp).background(colors.ok, CircleShape))
                Text(stringResource(R.string.home_connected), color = colors.onHero, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
            }
            Spacer(Modifier.weight(1f))
            Icon(
                painterResource(R.drawable.lucide_wifi),
                contentDescription = stringResource(R.string.home_wifi),
                tint = colors.onHero.copy(alpha = 0.8f),
                modifier = Modifier.size(18.dp),
            )
        }

        if (mac.type == DeviceType.TV) {
            TvArt(190.dp, Modifier.align(Alignment.CenterHorizontally).padding(vertical = 4.dp)) {
                // FlyyLink's home screen, open on the TV.
                Box(Modifier.fillMaxSize().background(Toneflix.goldGlow), contentAlignment = Alignment.Center) {
                    Image(painterResource(R.drawable.ic_flyylink_logo), contentDescription = null, modifier = Modifier.size(40.dp))
                }
            }
        } else {
            LaptopArt(180.dp, Modifier.align(Alignment.CenterHorizontally).padding(vertical = 4.dp)) {
                // The FlyyLink menu, open on the Mac's screen.
                Box(Modifier.fillMaxSize().padding(8.dp), contentAlignment = Alignment.TopEnd) {
                    Box(Modifier.size(54.dp, 22.dp).background(Toneflix.cyan, RoundedCornerShape(6.dp)))
                }
            }
        }

        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(mac.name, color = colors.onHero, fontSize = 24.sp, fontWeight = FontWeight.Bold, letterSpacing = (-0.5).sp)
            Text(stringResource(if (sharedWithOthers) R.string.home_flowing_many else R.string.home_flowing), color = colors.onHero.copy(alpha = 0.82f), fontSize = 14.sp)
        }
    }
}

@Composable
private fun Pill(content: @Composable () -> Unit) {
    Row(
        Modifier
            .background(FlyyLink.colors.onHero.copy(alpha = 0.1f), CircleShape)
            .padding(start = 10.dp, end = 12.dp, top = 6.dp, bottom = 6.dp),
        horizontalArrangement = Arrangement.spacedBy(7.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) { content() }
}

@Composable
private fun SearchingHero(hasNearby: Boolean) {
    val pulse = rememberInfiniteTransition(label = "pulse")
    val wave by pulse.animateFloat(0f, 1f, infiniteRepeatable(tween(2000), RepeatMode.Restart), label = "wave")

    Column(
        Modifier
            .fillMaxWidth()
            .background(MaterialTheme.colorScheme.surface, MaterialTheme.shapes.large)
            .padding(vertical = 30.dp, horizontal = 22.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(18.dp),
    ) {
        Box(Modifier.size(170.dp, 120.dp), contentAlignment = Alignment.Center) {
            Box(
                Modifier
                    .size(110.dp)
                    .scale(1f + wave * 0.6f)
                    .alpha(1f - wave)
                    .background(MaterialTheme.colorScheme.primaryContainer, CircleShape),
            )
            LaptopArt(120.dp)
        }
        Text(
            stringResource(if (hasNearby) R.string.home_pick_mac else R.string.home_looking),
            fontSize = 20.sp,
            fontWeight = FontWeight.Bold,
        )
        if (!hasNearby) {
            Text(
                stringResource(R.string.home_looking_hint),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
            )
        }
    }
}

@Composable
private fun QuickTiles(mac: DeviceState, macCount: Int) {
    val context = LocalContext.current
    var linkDialog by remember { mutableStateOf(false) }
    val pick = rememberLauncherForActivityResult(ActivityResultContracts.GetMultipleContents()) { uris ->
        if (uris.isNotEmpty()) context.startActivity(ShareActivity.sendFilesIntent(context, uris))
    }

    if (mac.type == DeviceType.TV) {
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Tile(R.drawable.lucide_tv, stringResource(R.string.home_remote), Modifier.weight(1f)) {
                context.startActivity(RemoteActivity.intent(context, mac.id))
            }
            Tile(R.drawable.lucide_file_up, stringResource(R.string.home_send_files), Modifier.weight(1f)) { pick.launch("*/*") }
            Tile(R.drawable.lucide_link, stringResource(R.string.home_open_link), Modifier.weight(1f)) { linkDialog = true }
        }
    } else {
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Tile(R.drawable.lucide_file_up, stringResource(R.string.home_send_files), Modifier.weight(1f)) { pick.launch("*/*") }
            Tile(R.drawable.lucide_clipboard, stringResource(R.string.home_clipboard), Modifier.weight(1f)) {
                val message = when {
                    !ClipboardPlugin.send(context) -> context.getString(R.string.clipboard_empty)
                    macCount > 1 -> context.getString(R.string.clipboard_sent_many, macCount)
                    else -> context.getString(R.string.clipboard_sent)
                }
                Toast.makeText(context, message, Toast.LENGTH_SHORT).show()
            }
            Tile(R.drawable.lucide_link, stringResource(R.string.home_open_link), Modifier.weight(1f)) { linkDialog = true }
            Tile(R.drawable.lucide_cast, stringResource(R.string.home_mirror), Modifier.weight(1f)) { ScreenPlugin.mirror(context, mac.id) }
        }
    }

    if (linkDialog) {
        LinkDialog(mac, initial = clipboardText(context), onDismiss = { linkDialog = false })
    }
}

@Composable
private fun Tile(@DrawableRes icon: Int, label: String, modifier: Modifier, onClick: () -> Unit) {
    Column(
        modifier
            .background(MaterialTheme.colorScheme.surface, MaterialTheme.shapes.medium)
            .clickable(onClick = onClick)
            .padding(vertical = 16.dp, horizontal = 8.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Box(
            Modifier.size(48.dp).background(MaterialTheme.colorScheme.primaryContainer, RoundedCornerShape(16.dp)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(painterResource(icon), contentDescription = null, tint = MaterialTheme.colorScheme.onPrimaryContainer)
        }
        Text(label, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, maxLines = 1)
    }
}

/** Cyan cards for the permissions FlyyLink still needs. Each disappears once granted. */
@Composable
private fun SetupCards() {
    val context = LocalContext.current
    var notifications by remember { mutableStateOf(NotificationPlugin.hasAccess(context)) }
    var background by remember { mutableStateOf(LinkService.isUnrestricted(context)) }
    var phone by remember { mutableStateOf(CallPlugin.hasPermissions(context)) }
    var phoneDisclosure by remember { mutableStateOf(false) }
    val askPhone = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) {
        phone = CallPlugin.hasPermissions(context)
        SmsPlugin.start(context)
        CallPlugin.start(context)
    }

    var location by remember { mutableStateOf(LocationPlugin.hasPermission(context) && LocationPlugin.hasBackgroundPermission(context)) }
    var locationDisclosure by remember { mutableStateOf(false) }
    // Android asks for "all the time" location separately, after the normal location permission.
    val askBackgroundLocation = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        location = LocationPlugin.hasPermission(context) && LocationPlugin.hasBackgroundPermission(context)
    }
    val askLocation = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) {
        if (LocationPlugin.hasPermission(context) && android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.Q) {
            askBackgroundLocation.launch(android.Manifest.permission.ACCESS_BACKGROUND_LOCATION)
        }
    }

    var photos by remember { mutableStateOf(PhotosPlugin.hasPermission(context)) }
    val askPhotos = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) {
        photos = PhotosPlugin.hasPermission(context)
    }

    var control by remember { mutableStateOf(ScreenControlService.status(context)) }
    var controlDisclosure by remember { mutableStateOf(false) }
    var nearby by remember { mutableStateOf(NearbyBeacon.hasPermission(context)) }
    val askNearby = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) {
        nearby = NearbyBeacon.hasPermission(context)
        if (nearby) LinkService.start(context)
    }

    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) {
        control = ScreenControlService.status(context)
        nearby = NearbyBeacon.hasPermission(context)
        photos = PhotosPlugin.hasPermission(context)
        location = LocationPlugin.hasPermission(context) && LocationPlugin.hasBackgroundPermission(context)
        notifications = NotificationPlugin.hasAccess(context)
        background = LinkService.isUnrestricted(context)
        phone = CallPlugin.hasPermissions(context)
    }

    AnimatedVisibility(!notifications) {
        SetupCard(
            R.drawable.lucide_bell,
            stringResource(R.string.setup_notifications),
        ) { context.startActivity(NotificationPlugin.accessSettingsIntent()) }
    }
    AnimatedVisibility(!background) {
        SetupCard(
            R.drawable.lucide_battery_charging,
            stringResource(R.string.setup_background),
        ) { context.startActivity(LinkService.unrestrictIntent(context)) }
    }
    AnimatedVisibility(!phone) {
        SetupCard(
            R.drawable.lucide_message_square,
            stringResource(R.string.setup_phone),
        ) { phoneDisclosure = true }
    }
    AnimatedVisibility(!location) {
        SetupCard(
            R.drawable.lucide_map_pin,
            stringResource(R.string.setup_location),
            stringResource(R.string.setup_location_hint),
        ) { locationDisclosure = true }
    }
    AnimatedVisibility(!photos) {
        SetupCard(
            R.drawable.lucide_image,
            stringResource(R.string.setup_photos),
        ) { askPhotos.launch(PhotosPlugin.permissions) }
    }
    AnimatedVisibility(!nearby) {
        SetupCard(
            R.drawable.lucide_bluetooth,
            stringResource(R.string.setup_nearby),
        ) { askNearby.launch(NearbyBeacon.permissions) }
    }
    AnimatedVisibility(control != ScreenControlService.Status.ON) {
        val stopped = control == ScreenControlService.Status.STOPPED
        SetupCard(
            R.drawable.lucide_pointer,
            stringResource(if (stopped) R.string.setup_control_stopped else R.string.setup_control),
            if (stopped) stringResource(R.string.setup_control_stopped_hint) else null,
        ) { controlDisclosure = true }
    }

    if (phoneDisclosure) {
        PhoneDisclosureSheet(
            onAccept = {
                phoneDisclosure = false
                askPhone.launch(CallPlugin.permissions)
            },
            onDecline = { phoneDisclosure = false },
        )
    }

    if (locationDisclosure) {
        LocationDisclosureSheet(
            onAccept = {
                locationDisclosure = false
                if (LocationPlugin.hasPermission(context) && android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.Q) {
                    askBackgroundLocation.launch(android.Manifest.permission.ACCESS_BACKGROUND_LOCATION)
                } else {
                    askLocation.launch(LocationPlugin.permissions)
                }
            },
            onDecline = { locationDisclosure = false },
        )
    }

    if (controlDisclosure) {
        ControlDisclosureSheet(
            onAccept = {
                controlDisclosure = false
                ScreenControlService.openSettings(context)
            },
            onDecline = { controlDisclosure = false },
        )
    }
}

@Composable
private fun SetupCard(@DrawableRes icon: Int, title: String, hint: String? = null, onAllow: () -> Unit) {
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
            Icon(painterResource(icon), contentDescription = null, tint = colors.onSetup, modifier = Modifier.size(20.dp))
        }
        Column(Modifier.weight(1f)) {
            Text(title, color = colors.onSetup, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
            hint?.let { Text(it, color = colors.onSetupVariant, fontSize = 12.sp) }
        }
        Button(
            onClick = onAllow,
            colors = ButtonDefaults.buttonColors(containerColor = colors.onSetup, contentColor = colors.setup),
            contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 16.dp),
            modifier = Modifier.height(40.dp),
        ) { Text(stringResource(R.string.setup_allow), fontSize = 13.sp, fontWeight = FontWeight.SemiBold) }
    }
}

@Composable
private fun Nearby(devices: List<DeviceState>) {
    val context = LocalContext.current
    var target by remember { mutableStateOf<String?>(null) }
    val pick = rememberLauncherForActivityResult(ActivityResultContracts.GetMultipleContents()) { uris ->
        val id = target
        if (uris.isNotEmpty() && id != null) context.startActivity(ShareActivity.sendFilesIntent(context, uris, target = id))
    }

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        SectionTitle(stringResource(R.string.home_nearby))

        devices.forEach { device ->
            Row(
                Modifier
                    .fillMaxWidth()
                    .background(MaterialTheme.colorScheme.surface, MaterialTheme.shapes.medium)
                    .padding(horizontal = 16.dp, vertical = 12.dp),
                horizontalArrangement = Arrangement.spacedBy(14.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                DeviceArt(device.type, 44.dp)
                Column(Modifier.weight(1f)) {
                    Text(device.name, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    if (device.canPair) Text(nearbyStatus(device), fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                if (device.canFlyyDrop && device.pairState == PairState.NOT_PAIRED) {
                    IconButton(onClick = {
                        target = device.id
                        pick.launch("*/*")
                    }) {
                        Icon(
                            painterResource(R.drawable.lucide_radar),
                            contentDescription = stringResource(R.string.flyydrop_send_files, device.name),
                            tint = MaterialTheme.colorScheme.primary,
                            modifier = Modifier.size(20.dp),
                        )
                    }
                }
                when (device.pairState) {
                    PairState.NOT_PAIRED -> if (device.canPair) TextButton(onClick = { withDevice(device.id) { it.requestPair() } }) {
                        Text(stringResource(R.string.pair), fontWeight = FontWeight.SemiBold)
                    }
                    PairState.REQUESTED -> CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                    else -> Unit
                }
            }
            if (device.pairState == PairState.REQUESTED && device.code != null) {
                Column(
                    Modifier.fillMaxWidth().padding(vertical = 4.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Text(stringResource(R.string.pair_check_mac), fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    CodeDigits(device.code)
                }
            }
        }
    }
}

@Composable
private fun nearbyStatus(device: DeviceState): String = when (device.pairState) {
    PairState.NOT_PAIRED -> stringResource(R.string.status_not_paired)
    PairState.REQUESTED -> stringResource(R.string.status_waiting)
    PairState.REQUESTED_BY_PEER -> stringResource(R.string.status_wants_pair)
    PairState.PAIRED -> stringResource(R.string.status_paired)
}

@Composable
private fun RecentSection(items: List<RecentItem>) {
    val context = LocalContext.current

    Column {
        Row(verticalAlignment = Alignment.CenterVertically) {
            SectionTitle(stringResource(R.string.home_recent), Modifier.weight(1f))
            TextButton(onClick = { Recent.clear() }) { Text(stringResource(R.string.home_clear)) }
        }

        Surface(color = MaterialTheme.colorScheme.surface, shape = MaterialTheme.shapes.medium) {
            Column {
                items.take(6).forEachIndexed { index, item ->
                    if (index > 0) HorizontalDivider(Modifier.padding(start = 74.dp), color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f))
                    RecentRow(item) {
                        item.openIntent(context)?.let { runCatching { context.startActivity(it) } }
                    }
                }
            }
        }
    }
}

@Composable
private fun RecentRow(item: RecentItem, onOpen: () -> Unit) {
    val failed = item.kind == RecentItem.Kind.FAILED

    Row(
        Modifier
            .fillMaxWidth()
            .clickable(enabled = item.uri != null, onClick = onOpen)
            .padding(horizontal = 16.dp, vertical = 12.dp),
        horizontalArrangement = Arrangement.spacedBy(14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            Modifier.size(44.dp).background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(14.dp)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painterResource(recentIcon(item)),
                contentDescription = null,
                tint = if (failed) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.size(20.dp),
            )
        }
        val reading = item.progress
        Column(Modifier.weight(1f)) {
            Text(item.title, fontSize = 15.sp, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
            if (reading != null) {
                LinearProgressIndicator(
                    progress = { reading.fraction.toFloat() },
                    modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp).height(4.dp),
                    trackColor = MaterialTheme.colorScheme.primary.copy(alpha = 0.2f),
                )
            }
            Text(
                if (reading != null) TransferText.describe(LocalContext.current, reading) else item.detail,
                fontSize = 13.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        if (reading != null) {
            Text(
                "${(reading.fraction * 100).toInt()}%",
                fontSize = 13.sp,
                fontWeight = FontWeight.SemiBold,
                color = MaterialTheme.colorScheme.primary,
            )
        } else if (item.inProgress) {
            CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
        } else {
            Text(
                DateUtils.getRelativeTimeSpanString(item.time, System.currentTimeMillis(), DateUtils.MINUTE_IN_MILLIS, DateUtils.FORMAT_ABBREV_RELATIVE).toString(),
                fontSize = 12.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@DrawableRes
private fun recentIcon(item: RecentItem): Int = when {
    item.kind == RecentItem.Kind.FAILED -> R.drawable.lucide_circle_alert
    item.kind == RecentItem.Kind.LINK -> R.drawable.lucide_link
    item.kind == RecentItem.Kind.TEXT -> R.drawable.lucide_clipboard
    item.mimeType?.startsWith("image/") == true -> R.drawable.lucide_image
    else -> R.drawable.lucide_file_text
}

@Composable
fun SectionTitle(text: String, modifier: Modifier = Modifier) {
    Text(
        text,
        modifier.padding(horizontal = 4.dp),
        fontSize = 14.sp,
        fontWeight = FontWeight.SemiBold,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
}

private fun clipboardText(context: Context): String {
    val clip = context.getSystemService(ClipboardManager::class.java).primaryClip ?: return ""
    val text = clip.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(context)?.toString().orEmpty().trim()

    return if (Uri.parse(text).scheme?.lowercase() in setOf("http", "https")) text else ""
}
