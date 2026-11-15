package com.toneflix.flyylink.ui

import android.Manifest
import android.content.Context
import android.os.Build
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.animateColorAsState
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.Stable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.toneflix.flyylink.Access
import com.toneflix.flyylink.Disclosure
import com.toneflix.flyylink.Feature
import com.toneflix.flyylink.Features
import com.toneflix.flyylink.FilesPlugin
import com.toneflix.flyylink.LinkService
import com.toneflix.flyylink.LocationPlugin
import com.toneflix.flyylink.R
import com.toneflix.flyylink.ScreenControlService

/**
 * Turns features on: shows the disclosure a permission needs, asks Android for it, and turns the
 * feature on once it is allowed. Settings screens are checked again when the app comes back.
 */
@Stable
class FeatureRequester(private val context: Context) {
    /** The feature whose disclosure sheet is showing. */
    var disclosure by mutableStateOf<Feature?>(null)
        private set

    /** Bumped whenever Android's answer may have changed, so switches read it again. */
    var version by mutableIntStateOf(0)
        private set

    private var waiting: Feature? = null
    private var inSettings = false

    internal var askRuntime: (Array<String>) -> Unit = {}
    internal var askBackgroundLocation: () -> Unit = {}

    fun isOn(feature: Feature, choices: Set<Feature>): Boolean =
        feature.access.isGranted(context) && (feature.isConnection || feature in choices)

    fun toggle(feature: Feature, on: Boolean) = if (on) turnOn(feature) else turnOff(feature)

    fun turnOn(feature: Feature) {
        if (feature.access.isGranted(context)) {
            finish(feature)
            return
        }

        if (feature.disclosure != null) disclosure = feature else ask(feature)
    }

    /** Phone features stop at once; a connection helper is turned off with its permission, in system settings. */
    private fun turnOff(feature: Feature) {
        if (feature.isConnection) {
            context.startActivity(Features.appSettingsIntent(context))
            return
        }

        Features.set(context, feature, false)
    }

    internal fun accept() {
        val feature = disclosure ?: return

        disclosure = null
        ask(feature)
    }

    internal fun decline() {
        disclosure = null
    }

    private fun ask(feature: Feature) {
        waiting = feature
        inSettings = false

        when (val access = feature.access) {
            Access.None -> answered()
            is Access.Runtime -> askRuntime(access.permissions())
            is Access.Special -> openSettings { access.open(context) }
            Access.Location -> askLocation()
            Access.Files -> if (FilesPlugin.needsSettings) {
                openSettings { context.startActivity(FilesPlugin.accessSettingsIntent(context)) }
            } else {
                askRuntime(FilesPlugin.legacyPermissions)
            }
        }
    }

    private fun openSettings(open: () -> Unit) {
        inSettings = true
        open()
    }

    /** Android asks for "Allow all the time" separately, after location while in use. */
    private fun askLocation() {
        if (LocationPlugin.hasPermission(context) && Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            askBackgroundLocation()
        } else {
            askRuntime(LocationPlugin.permissions)
        }
    }

    internal fun runtimeAnswered() {
        val needsBackground = waiting == Feature.LOCATION &&
            LocationPlugin.hasPermission(context) &&
            !LocationPlugin.hasBackgroundPermission(context) &&
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q
        if (needsBackground) {
            askBackgroundLocation()
            return
        }

        answered()
    }

    internal fun resumed() {
        if (inSettings) answered()

        Features.refresh(context)
        version++
    }

    private fun answered() {
        val feature = waiting ?: return

        waiting = null
        inSettings = false
        if (feature.access.isGranted(context)) finish(feature)
        version++
    }

    private fun finish(feature: Feature) {
        if (feature == Feature.NEARBY) LinkService.start(context)

        Features.set(context, feature, true)
    }
}

/** Remembers a [FeatureRequester] for this screen and shows its disclosure sheets. */
@Composable
fun rememberFeatureRequester(): FeatureRequester {
    val context = LocalContext.current
    val requester = remember { FeatureRequester(context) }
    val runtime = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { requester.runtimeAnswered() }
    val background = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { requester.runtimeAnswered() }

    requester.askRuntime = { runtime.launch(it) }
    requester.askBackgroundLocation = {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) background.launch(Manifest.permission.ACCESS_BACKGROUND_LOCATION)
    }

    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { requester.resumed() }

    when (requester.disclosure?.disclosure) {
        Disclosure.PHONE -> PhoneDisclosureSheet(onAccept = requester::accept, onDecline = requester::decline)
        Disclosure.LOCATION -> LocationDisclosureSheet(onAccept = requester::accept, onDecline = requester::decline)
        Disclosure.FILES -> FilesDisclosureSheet(onAccept = requester::accept, onDecline = requester::decline)
        Disclosure.CONTROL -> ControlDisclosureSheet(onAccept = requester::accept, onDecline = requester::decline)
        null -> Unit
    }

    return requester
}

/** A switch for each feature. Turning one on asks only for what that feature needs. */
@Composable
fun FeaturesScreen(firstRun: Boolean, onDone: () -> Unit) {
    val requester = rememberFeatureRequester()
    val choices by Features.choices.collectAsStateWithLifecycle()
    val working by Features.current.collectAsStateWithLifecycle()

    BackHandler(onBack = onDone)

    Box(Modifier.fillMaxSize().background(MaterialTheme.colorScheme.background)) {
        Column(
            Modifier
                .fillMaxSize()
                .statusBarsPadding()
                .navigationBarsPadding(),
        ) {
            Column(
                Modifier
                    .weight(1f)
                    .verticalScroll(rememberScrollState())
                    .padding(horizontal = 16.dp)
                    .padding(bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(14.dp),
            ) {
                if (firstRun) Welcome() else TopBar(onDone)

                Text(
                    stringResource(R.string.feature_count, working.reported?.size ?: 0),
                    fontSize = 13.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = FlyyLink.colors.ok,
                    modifier = Modifier.padding(horizontal = 4.dp),
                )

                FeatureGroup(stringResource(R.string.feature_group_devices), Feature.phoneFeatures, requester, choices)
                FeatureGroup(stringResource(R.string.feature_group_connection), Feature.connection, requester, choices)
            }

            if (firstRun) {
                Button(
                    onClick = onDone,
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 12.dp)
                        .height(56.dp),
                ) { Text(stringResource(R.string.feature_done), fontSize = 16.sp, fontWeight = FontWeight.SemiBold) }
            }
        }
    }
}

@Composable
private fun Welcome() {
    Column(
        Modifier.fillMaxWidth().padding(top = 36.dp, bottom = 4.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Image(painterResource(R.drawable.ic_flyylink_logo), contentDescription = null, modifier = Modifier.size(64.dp))
        Text(stringResource(R.string.feature_pick), fontSize = 26.sp, fontWeight = FontWeight.Bold, letterSpacing = (-0.6).sp)
    }
}

@Composable
private fun TopBar(onBack: () -> Unit) {
    Row(Modifier.padding(top = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        IconButton(onClick = onBack) {
            Icon(painterResource(R.drawable.lucide_arrow_left), contentDescription = stringResource(R.string.back))
        }
        Spacer(Modifier.width(4.dp))
        Text(stringResource(R.string.feature_screen_title), fontSize = 26.sp, fontWeight = FontWeight.Bold, letterSpacing = (-0.6).sp)
    }
}

@Composable
private fun FeatureGroup(title: String, features: List<Feature>, requester: FeatureRequester, choices: Set<Feature>) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        SectionTitle(title)

        Column(Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surface, MaterialTheme.shapes.large)) {
            features.forEachIndexed { index, feature ->
                if (index > 0) HorizontalDivider(Modifier.padding(start = 72.dp), color = MaterialTheme.colorScheme.outlineVariant)
                FeatureRow(feature, requester, choices)
            }
        }
    }
}

@Composable
private fun FeatureRow(feature: Feature, requester: FeatureRequester, choices: Set<Feature>) {
    val context = LocalContext.current
    val colors = MaterialTheme.colorScheme
    val version = requester.version
    val on = remember(version, choices) { requester.isOn(feature, choices) }
    val stopped = remember(version, choices) {
        feature == Feature.CONTROL && feature in choices && ScreenControlService.status(context) == ScreenControlService.Status.STOPPED
    }
    val chip by animateColorAsState(if (on) colors.primaryContainer else colors.surfaceVariant, label = "chip")
    val tint by animateColorAsState(if (on) colors.onPrimaryContainer else colors.onSurfaceVariant, label = "tint")

    Row(
        Modifier
            .fillMaxWidth()
            .clickable { requester.toggle(feature, !on) }
            .padding(horizontal = 16.dp, vertical = 14.dp),
        horizontalArrangement = Arrangement.spacedBy(14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(42.dp).background(chip, RoundedCornerShape(14.dp)), contentAlignment = Alignment.Center) {
            Icon(painterResource(feature.icon), contentDescription = null, tint = tint, modifier = Modifier.size(20.dp))
        }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(stringResource(feature.title), fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
            Text(
                stringResource(if (stopped) R.string.setup_control_stopped_hint else feature.hint),
                fontSize = 13.sp,
                color = if (stopped) colors.error else colors.onSurfaceVariant,
            )
        }
        Switch(checked = on, onCheckedChange = { requester.toggle(feature, it) })
    }
}
