using System.Security.Cryptography.X509Certificates;
using System.Collections.ObjectModel;
using FlyyLink.Core;
using FlyyLink.Plugins;
using FlyyLink.Shell;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications.Builder;

namespace FlyyLink.Model;

/// The app's state and actions: the network link, the connected devices, the features and what the panel shows.
/// Properties change on the UI thread.
public sealed class AppModel : Observable, IDeviceDelegate
{
    private const int RecentLimit = 20;

    private readonly DispatcherQueue ui;
    private readonly Settings settings = new(AppPaths.Settings);
    private readonly FileTrustStore trustStore = new(AppPaths.TrustedDevices);
    private readonly DeviceRegistry connected = new();
    private readonly ToastCenter toasts = new();
    private readonly PluginRouter plugins;
    private readonly BatteryPlugin battery;
    private readonly ConnectivityPlugin connectivity = new();
    private readonly ClipboardPlugin clipboard;
    private readonly NotificationPlugin notifications;
    private readonly SharePlugin share;
    private readonly MediaPlugin media = new();
    private readonly SmsPlugin sms = new();
    private readonly ContactsPlugin contacts = new();
    private readonly CallPlugin calls = new();
    private readonly LocationPlugin locations = new();
    private readonly PhotosPlugin photos = new();
    private readonly FilesPlugin phoneFiles = new();
    private readonly PhoneDrives drives;
    private readonly ScreenPlugin screen = new();
    private readonly FindPlugin find;
    private readonly MediaHostPlugin mediaHost;
    private readonly NotificationSharePlugin notificationShare;
    private readonly InputSharePlugin inputShare;
    private readonly ScreenHostPlugin screenHost = new();
    private readonly PhoneRelayPlugin relay;
    private readonly FlyyDropPlugin flyyDrop;
    private readonly Dictionary<string, FlyyDropSend> flyyDropSends = [];
    private IReadOnlyList<FlyyDropAsk> flyyDropAsks = [];
    private LanLink? link;
    private bool linking;
    private DispatcherQueueTimer? timer;
    private DateOnly notificationsDay = DateOnly.FromDateTime(DateTime.Now);

    private IReadOnlyList<DeviceState> devices = [];
    private DeviceState? focused;
    private IReadOnlyList<DeviceState> others = [];
    private string? error;
    private DateTimeOffset? clipboardSyncedAt;
    private int notificationsToday;
    private ActiveCall? call;
    private string? finding;
    private string? messagesDevice;
    private readonly List<string> waitingFiles = [];
    private string? waitingNumber;
    private string? waitingText;
    private PhoneStatus status = PhoneStatus.Empty;
    private DateTimeOffset waitingSince;
    private readonly PhoneHotspot hotspot;
    private readonly Updater updater;
    private readonly PhoneFolder folder = new(AppPaths.PhonePhotos, AppPaths.PhonePhotosRecord);
    private Device? folderPhone;
    private DateTimeOffset drivesRefreshed = DateTimeOffset.MinValue;
    private NearbyFinder? nearby;
    private DateTimeOffset lastNearbySearch = DateTimeOffset.MinValue;
    private bool phoneNearby;
    private string? hotspotStatus;

    public AppModel(DispatcherQueue ui)
    {
        this.ui = ui;

        Identity = new DeviceIdentity
        {
            DeviceId = settings.DeviceId,
            DeviceName = DeviceIdentity.SanitizeName(System.Net.Dns.GetHostName()),
            DeviceType = Windows.System.Power.PowerManager.BatteryStatus == Windows.System.Power.BatteryStatus.NotPresent ? DeviceType.Desktop : DeviceType.Laptop,
        };

        battery = new BatteryPlugin(toasts, connected) { OnChange = () => ui.TryEnqueue(Publish) };
        connectivity.OnChange = () => ui.TryEnqueue(Publish);
        clipboard = new ClipboardPlugin(connected, ui) { OnSync = () => ClipboardSyncedAt = DateTimeOffset.Now };
        notifications = new NotificationPlugin(toasts, connected) { IsEnabled = settings.MirrorsNotifications };
        notifications.OnShown = () => ui.TryEnqueue(CountNotification);
        share = new SharePlugin(AppPaths.Downloads, new TransferStore(AppPaths.Transfers), toasts, clipboard);
        share.OnActivity = activity => ui.TryEnqueue(() => Record(activity));
        media.OnChange = () => ui.TryEnqueue(Publish);
        locations.OnChange = () => ui.TryEnqueue(Publish);
        sms.OnMessages = (messages, deviceId) => ui.TryEnqueue(() => Messages.Ingest(messages, deviceId));
        contacts.OnContacts = (list, deviceId) => ui.TryEnqueue(() => Messages.Ingest(list, deviceId));
        calls.OnState = (state, device) =>
        {
            var deviceId = device.Id;
            var deviceName = device.Name;
            ui.TryEnqueue(() => HandleCall(state, deviceId, deviceName));
        };
        photos.OnPage = (page, deviceId) => ui.TryEnqueue(() => Photos.Ingest(page, deviceId));
        photos.OnPreview = (id, file, deviceId) =>
        {
            if (deviceId == folderPhone?.Id) folder.Delivered(id, file);
            ui.TryEnqueue(() => Photos.IngestPreview(id, file, deviceId));
        };
        folder.Request = id => folderPhone is Device phone && phone.Send(Core.Photos.DownloadPacket(id, preview: true));
        drives = new PhoneDrives(phoneFiles, connected, AppPaths.PhoneDrives, Path.Combine(AppPaths.Cache, "Phone files"));
        drives.Prune(trustStore.TrustedIds);
        Photos.Arrived += arrived =>
        {
            if (!ShowsPhotosInExplorer) return;

            Task.Run(() => folder.Add(arrived));
            FillPhoneFolder();
        };
        Messages.Send = packet => Primary is Device phone && phone.Send(packet);
        Messages.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MessagesStore.UnreadCount)) Raise(nameof(MessagesSubtitle));
        };
        Photos.Send = packet => Primary is Device phone && phone.Send(packet);
        screen.OnStarted = (device, info) =>
        {
            if (info.Codec == Screen.Jpeg)
            {
                var (computerId, computerName) = (device.Id, device.Name);
                ui.TryEnqueue(() =>
                {
                    Desktop.Started(computerId, computerName, info.Width, info.Height, info.Platform);
                    DesktopRequested?.Invoke();
                });
                return;
            }

            // Reset now, on the network thread, so the stream's first frames aren't lost.
            Video.Reset();
            if (info.HasAudio) Audio.Start(info.AudioRate, info.AudioChannels);
            else Audio.Stop();

            var (id, name) = (device.Id, device.Name);
            ui.TryEnqueue(() =>
            {
                Mirror.Started(id, name, info.Width, info.Height);
                ScreenStarted?.Invoke(Video.CreateSource((uint)Math.Max(info.Width, 0), (uint)Math.Max(info.Height, 0)));
            });
        };
        screen.OnFrame = frame =>
        {
            if (frame.IsAudio) Audio.Play(frame);
            else Video.Push(frame);
        };
        screen.OnPicture = DesktopFeed.Push;
        screen.OnEnded = id =>
        {
            if (Mirror.DeviceId == id) Audio.Stop();
            ui.TryEnqueue(() =>
            {
                Mirror.Ended(id);
                Desktop.Ended(id);
            });
        };
        DesktopFeed.OnPicture = bitmap =>
        {
            if (!ui.TryEnqueue(() => DesktopPicture?.Invoke(bitmap))) bitmap.Dispose();
        };
        Desktop.Send = (id, packet) => connected[id]?.Send(packet) == true;
        Mirror.IsMuted = Audio.IsMuted = settings.MirrorMuted;
        Mirror.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(ScreenMirror.IsMuted)) return;

            Audio.IsMuted = settings.MirrorMuted = Mirror.IsMuted;
        };
        Mirror.Send = (id, packet) => connected[id]?.Send(packet) == true;
        find = new FindPlugin(toasts);
        mediaHost = new MediaHostPlugin(connected);
        notificationShare = new NotificationSharePlugin(connected) { IsEnabled = settings.SharesNotifications };
        inputShare = new InputSharePlugin(connected, settings) { OnChange = () => ui.TryEnqueue(InputChanged) };
        relay = new PhoneRelayPlugin(connected, settings);
        relay.OnRelayed = (message, via) =>
        {
            var name = $"{message.PhoneName} via {via}";
            if (message.Packet.Type == PacketType.Notification)
            {
                notifications.PresentRelayed(message.Packet, message.PhoneId, name);
                return;
            }

            if (Core.Call.StateOf(message.Packet) is Core.Call.State state) ui.TryEnqueue(() => HandleCall(state, message.PhoneId, name));
        };
        notifications.Send = SendToPhone;
        screenHost.Allows = () => inputShare.AllowsControl;
        screenHost.OnChange = _ => ui.TryEnqueue(ScreenViewerChanged);
        flyyDrop = new FlyyDropPlugin(share, connected)
        {
            OnAsk = ask => ui.TryEnqueue(() => FlyyDropAsked(ask)),
            OnAskEnded = id => ui.TryEnqueue(() => FlyyDropAskEnded(id)),
            OnSend = (deviceId, send) => ui.TryEnqueue(() => FlyyDropMoved(deviceId, send)),
            OnActivity = activity => ui.TryEnqueue(() => Record(activity)),
        };
        var (receiving, until) = settings.FlyyDropReceiving;
        flyyDrop.Drop.Restore(receiving, until);
        plugins = new PluginRouter([notifications, battery, connectivity, clipboard, share, media, sms, contacts, calls, locations, photos, phoneFiles, screen, find, mediaHost, notificationShare, inputShare, screenHost, relay, flyyDrop]);

        updater = new Updater { OnInstalling = () => ui.TryEnqueue(() => UpdateInstalling?.Invoke()) };
        Licence = new LicenceModel(ui, toasts);
        Licence.LockChanged += () =>
        {
            if (Licence.IsLocked) StopLink();
            else StartLink();
        };
        hotspot = new PhoneHotspot(() => settings.PhoneHotspots, settings.RememberHotspot);

        share.Restore();
    }

    public DeviceIdentity Identity { get; }

    public MessagesStore Messages { get; } = new();

    public PhotosStore Photos { get; } = new();

    public PlayOnTvState PlayOnTv { get; } = new();

    public ScreenMirror Mirror { get; } = new();

    internal ScreenVideo Video { get; } = new();

    internal ScreenAudio Audio { get; } = new();

    /// Another computer's screen, watched here.
    public RemoteDesktop Desktop { get; } = new();

    internal RemoteDesktopFeed DesktopFeed { get; } = new();

    /// The call the call card shows.
    public ActiveCall? Call
    {
        get => call;
        private set => Set(ref call, value);
    }

    /// The phone being looked for: its map shows in the panel until closed.
    public string? Finding
    {
        get => finding;
        private set
        {
            if (Set(ref finding, value)) Raise(nameof(Found));
        }
    }

    /// The focused phone's location while it is being looked for.
    public PhoneLocation? Found => Focused is { } phone && phone.Id == Finding ? phone.Location : null;

    public string MessagesSubtitle => Messages.UnreadCount == 0 ? "Your texts" : $"{Messages.UnreadCount} unread";

    public bool NotificationsAvailable => toasts.IsAvailable;

    public IReadOnlyList<DeviceState> Devices
    {
        get => devices;
        private set
        {
            if (!Set(ref devices, value)) return;

            Raise(nameof(HasOtherPhones));
            Raise(nameof(NextPhone));
            Raise(nameof(ShowsCarousel));
            Raise(nameof(ShowsDeviceList));
        }
    }

    /// The device the big cards are about: one asking to pair first, then the chosen or first paired phone.
    public DeviceState? Focused
    {
        get => focused;
        private set
        {
            if (!Set(ref focused, value)) return;

            Raise(nameof(HasOtherPhones));
            Raise(nameof(NextPhone));
            Raise(nameof(ShowsCarousel));
            Raise(nameof(ShowsDeviceList));
            Raise(nameof(ShowsSearching));
            Raise(nameof(ShowsHotspot));
            Raise(nameof(ShowsPairing));
            Raise(nameof(ShowsPhone));
            Raise(nameof(FocusIsPhone));
            Raise(nameof(FocusIsComputer));
            Raise(nameof(FocusIsTv));
            Raise(nameof(InputStatus));
            Raise(nameof(InputActive));
            Raise(nameof(FocusWatchesScreen));
            Raise(nameof(ScreenStatus));
            Raise(nameof(Found));
        }
    }

    /// Unpaired devices on the network. Paired ones are reached through the carousel or the device list.
    public IReadOnlyList<DeviceState> Others
    {
        get => others;
        private set
        {
            if (Set(ref others, value)) Raise(nameof(HasOthers));
        }
    }

    public string? Error
    {
        get => error;
        private set
        {
            if (Set(ref error, value)) Raise(nameof(HasError));
        }
    }

    public DateTimeOffset? ClipboardSyncedAt
    {
        get => clipboardSyncedAt;
        private set
        {
            if (Set(ref clipboardSyncedAt, value)) Raise(nameof(ClipboardSubtitle));
        }
    }

    public int NotificationsToday
    {
        get => notificationsToday;
        private set
        {
            if (Set(ref notificationsToday, value)) Raise(nameof(NotificationsSubtitle));
        }
    }

    public bool MirrorsNotifications
    {
        get => settings.MirrorsNotifications;
        set
        {
            if (settings.MirrorsNotifications == value) return;

            settings.MirrorsNotifications = value;
            notifications.IsEnabled = value;
            Raise();
            Raise(nameof(NotificationsSubtitle));
        }
    }

    /// Whether this PC's own notifications go to paired computers.
    public bool SharesNotifications
    {
        get => settings.SharesNotifications;
        set
        {
            settings.SharesNotifications = value;
            notificationShare.IsEnabled = value;
            Raise();
        }
    }

    // Keyboard and mouse

    /// The computer this PC's keyboard and mouse work now, or null.
    public string? InputTarget => inputShare.Capture.RemoteId;

    /// The computer working this PC now, or null.
    public string? InputController => inputShare.Injector.ControllerId;

    public bool AllowsRemoteInput
    {
        get => inputShare.AllowsControl;
        set
        {
            inputShare.AllowsControl = value;
            if (!value) screenHost.Stop();
        }
    }

    public ScreenEdge? InputEdge(string id) => inputShare.Edge(id);

    public void SetInputEdge(ScreenEdge? edge, string id) => inputShare.SetEdge(edge, id);

    /// What the keyboard and mouse card says about the computer the panel shows.
    public string InputStatus => Focused is not { IsComputer: true } device ? ""
        : InputTarget == device.Id ? $"Using {device.Name}. Press Esc three times to return."
        : InputController == device.Id ? $"{device.Name} is using this PC."
        : InputEdge(device.Id) is ScreenEdge edge ? $"Move the pointer off the {edge.WireName()} edge."
        : $"Which side is {device.Name} on?";

    public bool InputActive => Focused is { } device && (InputTarget == device.Id || InputController == device.Id);

    // Screen

    /// The computer watching this PC's screen now, or null.
    public string? ScreenViewer => screenHost.ViewerId;

    /// Whether the computer the panel shows is watching this PC.
    public bool FocusWatchesScreen => Focused is { } device && ScreenViewer == device.Id;

    /// What the screen card says about the computer the panel shows.
    public string ScreenStatus => Focused is not { IsComputer: true } device ? ""
        : FocusWatchesScreen ? $"{device.Name} is watching"
        : $"View and control {device.Name}";

    /// Stops showing this PC's screen to whoever is watching.
    public void StopScreenShare() => screenHost.Stop();

    private void ScreenViewerChanged()
    {
        Raise(nameof(ScreenViewer));
        Raise(nameof(FocusWatchesScreen));
        Raise(nameof(ScreenStatus));
    }

    private void InputChanged()
    {
        Raise(nameof(InputTarget));
        Raise(nameof(InputController));
        Raise(nameof(InputStatus));
        Raise(nameof(InputActive));
    }

    /// Windows lets only packaged apps read notifications, so sharing them may not be possible.
    public bool CanShareNotifications => notificationShare.IsAvailable;

    public PhoneSwitcherStyle SwitcherStyle
    {
        get => settings.SwitcherStyle;
        set
        {
            if (settings.SwitcherStyle == value) return;

            settings.SwitcherStyle = value;
            Raise();
            Raise(nameof(ShowsCarousel));
            Raise(nameof(ShowsDeviceList));
        }
    }

    /// The paired phone after the one shown, wrapping round, when there is more than one.
    public DeviceState? NextPhone => new DeviceFocus(Focused?.Id).Next(Devices.Where(d => d.IsPaired), d => d.Id, d => d.Name) is { } next && next.Id != Focused?.Id ? next : null;

    public bool ShowsCarousel => SwitcherStyle == PhoneSwitcherStyle.Carousel && NextPhone is not null;

    /// The device's name opens a list of the other paired devices.
    public bool ShowsDeviceList => SwitcherStyle == PhoneSwitcherStyle.Menu && HasOtherPhones;

    public void ShowNextPhone()
    {
        if (NextPhone is DeviceState next) Select(next.Id);
    }

    /// Whether the system-wide keyboard shortcuts are on. The app turns the shortcuts themselves on and off.
    public bool UsesShortcuts
    {
        get => settings.UsesShortcuts;
        set
        {
            settings.UsesShortcuts = value;
            Raise();
        }
    }

    /// Whether the phone's recent photos show as a folder in Explorer.
    public bool ShowsPhotosInExplorer
    {
        get => settings.ShowsPhotosInExplorer;
        set
        {
            settings.ShowsPhotosInExplorer = value;
            Raise();

            if (!value)
            {
                Task.Run(folder.Remove);
                return;
            }

            SyncStores(Primary);
            var loaded = Photos.Loaded.ToList();
            Task.Run(() => folder.Add(loaded));
            FillPhoneFolder();
        }
    }

    public string PhonePhotosFolder => folder.Root;

    /// Whether each paired phone's storage shows as a folder in Explorer.
    public bool ShowsPhoneInExplorer
    {
        get => settings.ShowsPhoneInExplorer;
        set
        {
            settings.ShowsPhoneInExplorer = value;
            Raise();

            if (value) ShowDrives();
            else Task.Run(drives.ForgetAll);
        }
    }

    /// Whether the phone runs a FlyyLink that lets computers browse its storage, with the feature on.
    public bool SharesFiles(string id) =>
        connected[id] is { } device
        && device.Link.Identity.IncomingCapabilities.Contains(PacketType.FilesRequest)
        && device.Features.Contains(PhoneFeature.Files);

    /// Opens the phone's storage in Explorer.
    public void OpenPhoneFiles(string id)
    {
        if (!ShowsPhoneInExplorer) ShowsPhoneInExplorer = true;
        ShowDrives();

        if (drives.RootOf(id) is string root) Explorer.Open(root);
    }

    public bool StartsWithWindows
    {
        get => Shell.Startup.IsEnabled;
        set
        {
            Shell.Startup.IsEnabled = value;
            Raise();
        }
    }

    public ObservableCollection<Activity> Recent { get; } = [];

    public bool ShowsSearching => Focused is null;

    /// A paired phone is in Bluetooth range but not on this network.
    public bool PhoneNearby
    {
        get => phoneNearby;
        private set
        {
            if (Set(ref phoneNearby, value)) Raise(nameof(ShowsHotspot));
        }
    }

    /// What joining the phone's hotspot is doing, or why it failed.
    public string? HotspotStatus
    {
        get => hotspotStatus;
        private set
        {
            if (!Set(ref hotspotStatus, value)) return;

            Raise(nameof(ShowsHotspot));
            Raise(nameof(IsJoiningHotspot));
            Raise(nameof(CanJoinHotspot));
            Raise(nameof(HotspotText));
        }
    }

    public bool ShowsHotspot => Focused is null && (PhoneNearby || HotspotStatus is not null);

    public bool IsJoiningHotspot => HotspotStatus is not null;

    public bool CanJoinHotspot => HotspotStatus is null;

    public string HotspotText => HotspotStatus ?? (PhoneHotspotName is null
        ? "It's not on this Wi-Fi. Join its hotspot to connect."
        : "It's not on this Wi-Fi. Turn on its hotspot to connect.");

    /// The name of the phone's hotspot, once this PC has been on it.
    public string? PhoneHotspotName => HotspotPhoneId is string id ? hotspot.Name(id) : null;

    public string HotspotButton => PhoneHotspotName is string ssid ? $"Use {ssid}" : "Use your phone's hotspot";

    public bool ShowsPairing => Focused is { IsPaired: false };

    public bool ShowsPhone => Focused is { IsPaired: true };

    /// The paired device the panel shows is a phone, so the phone's own actions apply.
    public bool FocusIsPhone => Focused is { IsPaired: true, IsPhone: true };

    /// The paired device the panel shows is a TV.
    public bool FocusIsTv => Focused is { IsPaired: true, IsTv: true };

    /// The paired device the panel shows is another Mac or PC.
    public bool FocusIsComputer => Focused is { IsPaired: true, IsComputer: true };

    public bool HasOthers => Others.Count > 0;

    /// Several phones are paired, so the panel offers to switch between them.
    public bool HasOtherPhones => OtherPhones.Count > 0;

    public IReadOnlyList<DeviceState> OtherPhones => Devices.Where(d => d.IsPaired && d.Id != Focused?.Id).ToList();

    public bool HasError => Error is not null;

    public bool HasRecent => Recent.Count > 0;

    public string ClipboardSubtitle => ClipboardSyncedAt is { } date ? $"Synced {RelativeTime.Text(date)}" : "Send it now";

    public string NotificationsSubtitle => !MirrorsNotifications ? "Paused" : NotificationsToday == 0 ? "On" : $"{NotificationsToday} today";

    public string ThisComputer => $"This PC: {Identity.DeviceName}";

    /// Starts listening for phones. Call once, on the UI thread.
    /// Whether this PC may connect, and the sign-in card.
    public LicenceModel Licence { get; }

    public void Start()
    {
        _ = notificationShare.StartAsync();
        updater.Start();
        Licence.Start();
        if (!Licence.IsLocked) StartLink();
    }

    /// Finds and connects to devices. Runs only while the licence allows it.
    private void StartLink()
    {
        if (link is not null || linking) return;

        linking = true;
        var me = Identity;
        Task.Run(() =>
        {
            try
            {
                // Creating the certificate on first launch is slow, so keep it off the UI thread.
                var certificate = LoadCertificate(me.DeviceId);
                nearby = new NearbyFinder(me.DeviceId, certificate.Certificate.GetRSAPrivateKey()!)
                {
                    OnNearby = found => ui.TryEnqueue(() => PhoneNearby = found && connected.PairedPhones.Count == 0),
                    OnAnswer = answer => ui.TryEnqueue(() => Reach(answer)),
                };
                var lan = new LanLink(() => me, certificate, trustStore, deviceLink =>
                {
                    // Created right away on the network thread so no early packet is missed.
                    var device = new Device(deviceLink, certificate, trustStore, guests: flyyDrop.Drop) { Delegate = this };
                    device.Start();
                    ui.TryEnqueue(() => Add(device));
                }, id => connected[id]?.Link.IsOpen == true);
                lan.Start();

                ui.TryEnqueue(() => Started(lan));
            }
            catch (Exception e)
            {
                ui.TryEnqueue(() =>
                {
                    linking = false;
                    Error = $"Could not start: {e.Message}";
                });
            }
        });
    }

    /// The installer for a new version is running; quit so it can replace the app.
    public event Action? UpdateInstalling;

    public string VersionText => Updater.CurrentText;

    /// Whether any device has been paired with this PC, connected or not.
    public bool HasPaired => trustStore.TrustedIds.Count > 0;

    public void CheckForUpdates() => updater.Check();

    public void Stop()
    {
        StopLink();
        updater.Stop();
        folder.Disconnect();
        drives.Stop();
        Audio.Dispose();
        status.Disconnected().Save();
        toasts.Unregister();
    }

    private void StopLink()
    {
        timer?.Stop();
        link?.Stop();
        link = null;
        foreach (var device in connected.All) device.Link.Close("FlyyLink stopped");
    }

    /// Something was copied on this PC.
    public void ClipboardChanged() => clipboard.Changed();

    public void RequestPair(string id) => connected[id]?.RequestPair();

    public void Accept(string id) => connected[id]?.Accept();

    public void Reject(string id) => connected[id]?.Reject();

    public void Unpair(string id)
    {
        connected[id]?.Unpair();
        Task.Run(() => drives.Forget(id));
    }

    /// Shows this phone in the panel when several are paired.
    public void Select(string id)
    {
        settings.SelectedPhone = id;
        Publish();
    }

    /// Rings the phone and asks where it is, so it can be found by sound or on the map. A computer only plays a sound.
    public void Ring(string id)
    {
        if (connected[id] is not Device device) return;

        device.Send(FindMyPhone.Request());
        if (!device.Link.Identity.DeviceType.IsPhone()) return;

        locations.Request(device);
        Finding = id;
    }

    public void StopFinding() => Finding = null;

    public void Control(string id, string player, MediaAction action) => connected[id]?.Send(MediaRequest.Act(player, action).ToPacket());

    public void Seek(string id, string player, long position) => connected[id]?.Send(MediaRequest.Seek(player, position).ToPacket());

    /// Opens the remote for a paired TV.
    public void ShowRemote(string id)
    {
        if (connected[id] is { IsPaired: true }) RemoteRequested?.Invoke(id);
    }

    public event Action<string>? RemoteRequested;

    /// Sends a remote press to the TV.
    public void Press(string id, TvRemote press) => connected[id]?.Send(press.ToPacket());

    // Calls

    public void AnswerCall()
    {
        if (Call is { } current) SendToPhone(current.DeviceId, Core.Call.Answer());
    }

    public void DeclineCall()
    {
        if (Call is not { } current) return;

        SendToPhone(current.DeviceId, Core.Call.Decline());
        EndCall();
    }

    public void HangUp()
    {
        if (Call is not { } current) return;

        SendToPhone(current.DeviceId, Core.Call.HangUp());
        EndCall();
    }

    /// Mutes the phone's microphone during a call.
    public void ToggleMute()
    {
        if (Call is not { } current) return;

        var muted = !current.IsMuted;
        SendToPhone(current.DeviceId, Core.Call.Mute(muted));
        Call = current with { IsMuted = muted };
    }

    /// Places a call on the phone the panel shows.
    public void Dial(string number, string? name = null)
    {
        var trimmed = number.Trim();
        if (trimmed.Length == 0) return;

        if (Primary is not Device phone || !phone.Send(Core.Call.Dial(trimmed)))
        {
            Record(new Activity(ActivityKind.Failed, $"Couldn't call {trimmed}", "Connect your phone first, then try again"));
            return;
        }

        Call = new ActiveCall(phone.Id, phone.Name, trimmed, name, null, CallPhase.Dialing);
    }

    /// Sends to a phone, directly or through the computer that passed it on.
    private bool SendToPhone(string id, NetworkPacket packet) =>
        connected[id] is { IsPaired: true } device ? device.Send(packet) : relay.Send(packet, id);

    /// Whether this PC passes its phones' notifications and calls to its other computers, and shows theirs.
    public bool RelaysPhones
    {
        get => relay.IsEnabled;
        set
        {
            relay.IsEnabled = value;
            Raise();
        }
    }

    private void HandleCall(Core.Call.State state, string deviceId, string deviceName)
    {
        if (state.Ended)
        {
            EndCall();
            return;
        }

        switch (state.Event)
        {
            case CallEvent.Ringing:
                Call = new ActiveCall(deviceId, deviceName, state.Number, state.ContactName, state.Photo, CallPhase.Ringing);
                break;
            case CallEvent.Talking:
                var current = Call ?? new ActiveCall(deviceId, deviceName, state.Number, state.ContactName, state.Photo, CallPhase.Dialing);
                Call = current.Phase == CallPhase.Talking ? current : current with { Phase = CallPhase.Talking, Since = DateTimeOffset.Now };
                break;
            case CallEvent.Missed:
                EndCall();
                AnnounceMissedCall(state.ContactName ?? state.Number, state.Number, deviceName);
                break;
        }
    }

    private void EndCall()
    {
        if (Call is { IsMuted: true } muted) SendToPhone(muted.DeviceId, Core.Call.Mute(false));

        Call = null;
    }

    private void AnnounceMissedCall(string caller, string number, string deviceName) => toasts.Show(
        key => new AppNotificationBuilder()
            .AddText("Missed call")
            .AddText(caller)
            .SetAttributionText(deviceName)
            .AddButton(ToastCenter.Button("Call back", "call", key))
            .AddButton(ToastCenter.Button("Message", "text", key)),
        (action, _) => ui.TryEnqueue(() =>
        {
            if (action == "call") Dial(number, caller == number ? null : caller);
            else if (action == "text") ShowMessages(number);
        }));

    public async void SendClipboard(string id)
    {
        if (connected[id] is not Device device) return;

        if (!await clipboard.SendAsync(device)) Record(new Activity(ActivityKind.Failed, "Clipboard is empty", "Copy some text first"));
    }

    public void SendFiles(IEnumerable<string> paths, string id)
    {
        if (connected[id] is not Device device) return;

        var files = paths.Where(File.Exists).ToList();
        Task.Run(() => files.ForEach(path => share.Send(path, device)));
    }

    // FlyyDrop

    /// The oldest FlyyDrop waiting for the user to answer.
    public FlyyDropAsk? FlyyDropAsk => flyyDropAsks.FirstOrDefault();

    public bool ShowsFlyyDropAsk => FlyyDropAsk is not null;

    /// Who may FlyyDrop to this PC without being paired. "Everyone for 10 minutes" turns itself off.
    public FlyyDrop.Receiving FlyyDropReceiving
    {
        get => flyyDrop.Drop.ReceivingMode;
        set
        {
            flyyDrop.Drop.ReceivingMode = value;
            settings.FlyyDropReceiving = (value, flyyDrop.Drop.ReceivingUntil ?? 0);
            RaiseFlyyDropReceiving();
        }
    }

    /// The FlyyDrop receiving choices with their titles, in menu order.
    public static readonly (FlyyDrop.Receiving Mode, string Title)[] FlyyDropChoices =
    [
        (FlyyDrop.Receiving.Off, "Paired devices only"),
        (FlyyDrop.Receiving.EveryoneFor10Minutes, "Everyone for 10 minutes"),
        (FlyyDrop.Receiving.Everyone, "Everyone"),
    ];

    public bool ShowsFlyyDropBanner => FlyyDropReceiving != FlyyDrop.Receiving.Off;

    public string FlyyDropBannerText => flyyDrop.Drop.ReceivingUntil is long until
        ? $"Open to everyone · {Math.Max(1, (int)Math.Ceiling((until - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 60_000.0))} min left"
        : "Open to everyone nearby";

    /// Asks the unpaired device `id` to take these files.
    public void FlyyDropFiles(IEnumerable<string> paths, string id)
    {
        if (connected[id] is not Device device) return;

        var files = paths.ToList();
        Task.Run(() => flyyDrop.Send(files, null, device));
    }

    /// Asks the unpaired device `id` to take a link or some text.
    public void FlyyDropText(string text, string id)
    {
        if (connected[id] is not Device device || text.Trim() is not { Length: > 0 } trimmed) return;

        Task.Run(() => flyyDrop.Send([], trimmed, device));
    }

    /// Stops waiting for device `id` to answer.
    public void CancelFlyyDrop(string id)
    {
        if (flyyDropSends.GetValueOrDefault(id) is not { Stage: FlyyDropStage.Waiting } send) return;

        flyyDrop.Cancel(send.Id, id);
        flyyDropSends.Remove(id);
        ShowFlyyDropSends();
    }

    public void AcceptFlyyDrop(string id) => flyyDrop.Accept(id);

    public void DeclineFlyyDrop(string id) => flyyDrop.Decline(id);

    /// Someone nearby asked to FlyyDrop, so the panel should open.
    public event Action? FlyyDropRequested;

    private void FlyyDropAsked(FlyyDropAsk ask)
    {
        flyyDropAsks = [.. flyyDropAsks, ask];
        RaiseFlyyDropAsk();

        toasts.Show(key => new AppNotificationBuilder()
            .AddText(ask.Title)
            .AddText(ask.Summary)
            .AddButton(ToastCenter.Button("Decline", "decline", key))
            .AddButton(ToastCenter.Button("Accept", "accept", key)),
            (action, _) => ui.TryEnqueue(() =>
            {
                if (action == "accept") AcceptFlyyDrop(ask.Id);
                else if (action == "decline") DeclineFlyyDrop(ask.Id);
                else FlyyDropRequested?.Invoke();
            }),
            tag: FlyyDropTag(ask.Id), group: FlyyDropGroup);
        FlyyDropRequested?.Invoke();
    }

    private void FlyyDropAskEnded(string id)
    {
        flyyDropAsks = flyyDropAsks.Where(ask => ask.Id != id).ToList();
        RaiseFlyyDropAsk();
        toasts.Remove(FlyyDropTag(id), FlyyDropGroup);
    }

    private void FlyyDropMoved(string deviceId, FlyyDropSend send)
    {
        // Updates for a request the user has since replaced or cancelled are dropped.
        if (send.Stage != FlyyDropStage.Waiting && flyyDropSends.GetValueOrDefault(deviceId)?.Id != send.Id) return;

        flyyDropSends[deviceId] = send;
        ShowFlyyDropSends();

        if (!send.IsFinished) return;

        // The result stays on the device's row for a few seconds.
        var forget = ui.CreateTimer();
        forget.Interval = TimeSpan.FromSeconds(8);
        forget.IsRepeating = false;
        forget.Tick += (_, _) =>
        {
            if (flyyDropSends.GetValueOrDefault(deviceId) != send) return;

            flyyDropSends.Remove(deviceId);
            ShowFlyyDropSends();
        };
        forget.Start();
    }

    /// Puts the latest FlyyDrop progress on the device rows without rebuilding everything else.
    private void ShowFlyyDropSends()
    {
        Devices = Devices.Select(d => d with { Outgoing = flyyDropSends.GetValueOrDefault(d.Id) }).ToList();
        Focused = Focused is { } focus ? Devices.FirstOrDefault(d => d.Id == focus.Id) : null;
        Others = Devices.Where(d => !d.IsPaired && d.Id != Focused?.Id).ToList();
    }

    private void RaiseFlyyDropAsk()
    {
        Raise(nameof(FlyyDropAsk));
        Raise(nameof(ShowsFlyyDropAsk));
    }

    private void RaiseFlyyDropReceiving()
    {
        Raise(nameof(FlyyDropReceiving));
        Raise(nameof(ShowsFlyyDropBanner));
        Raise(nameof(FlyyDropBannerText));
    }

    private const string FlyyDropGroup = "flyydrop";

    /// A short notification tag for a request; the id comes from the other device, so it isn't used as is.
    private static string FlyyDropTag(string id) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..16];

    /// Sends a web link to open on the phone, or other text to put on its clipboard.
    public void SendText(string text, string id)
    {
        if (connected[id] is not Device device) return;

        var trimmed = text.Trim();
        var isLink = Uri.TryCreate(trimmed, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
        if (!device.Send(isLink ? Share.LinkPacket(url!.AbsoluteUri) : Share.TextPacket(trimmed))) return;

        Record(isLink
            ? new Activity(ActivityKind.Link, url!.Host, $"Opened on {device.Name}") { Url = url.AbsoluteUri }
            : new Activity(ActivityKind.Text, $"“{(trimmed.Length > 60 ? trimmed[..60] : trimmed)}”", $"Copied to {device.Name}"));
    }

    /// Files and tel: links handed over by Windows. Right after FlyyLink starts no phone is connected yet,
    /// so they wait up to a minute for one.
    public void Handover(IReadOnlyList<string> files, string? number, string? text = null)
    {
        waitingFiles.AddRange(files);
        waitingNumber = number ?? waitingNumber;
        waitingText = text ?? waitingText;
        waitingSince = DateTimeOffset.Now;

        SendWaiting();
    }

    private void SendWaiting()
    {
        if (waitingFiles.Count == 0 && waitingNumber is null && waitingText is null) return;

        // Files go to whichever device the panel shows; a number needs a phone.
        if ((waitingNumber is null ? Shown : Primary) is Device phone)
        {
            if (waitingFiles.Count > 0) SendFiles(waitingFiles.ToList(), phone.Id);
            if (waitingNumber is string number) Dial(number);
            if (waitingText is string text) SendText(text, phone.Id);
        }
        else if (DateTimeOffset.Now - waitingSince < TimeSpan.FromMinutes(1))
        {
            return;
        }
        else
        {
            Record(new Activity(ActivityKind.Failed, "Nothing was sent", "No phone connected"));
        }

        waitingFiles.Clear();
        waitingNumber = null;
        waitingText = null;
    }

    public void ClearRecent()
    {
        foreach (var done in Recent.Where(a => !a.IsInProgress).ToList()) Recent.Remove(done);
        share.ForgetUnfinished();

        Raise(nameof(HasRecent));
    }

    private void Record(Activity activity)
    {
        var index = Recent.ToList().FindIndex(a => a.Id == activity.Id);
        if (index >= 0)
        {
            Recent[index] = activity;
            return;
        }

        Recent.Insert(0, activity);
        while (Recent.Count > RecentLimit) Recent.RemoveAt(Recent.Count - 1);

        Raise(nameof(HasRecent));
    }

    private void CountNotification()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today != notificationsDay)
        {
            notificationsDay = today;
            NotificationsToday = 0;
        }

        NotificationsToday++;
    }

    private void Started(LanLink lan)
    {
        linking = false;
        if (Licence.IsLocked)
        {
            lan.Stop();
            return;
        }

        link = lan;
        timer = ui.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(5);
        timer.Tick += (_, _) =>
        {
            Task.Run(() => lan.BroadcastIdentity());
            foreach (var device in connected.All) device.Tick();
            SendWaiting();
            SearchNearbyIfLost();
            FillPhoneFolder();
            RefreshDrives();
            flyyDrop.Tick();
            RaiseFlyyDropReceiving();
            Raise(nameof(ClipboardSubtitle));
        };
        timer.Start();

        Task.Run(() => lan.BroadcastIdentity());
    }

    private void Add(Device device)
    {
        if (connected[device.Id] is Device existing && existing != device && DeviceLink.KeepsExisting(existing.Link, device.Link, Identity.DeviceId))
        {
            device.Link.Close("both sides connected at once; kept the other link");
            return;
        }

        if (connected.Insert(device) is Device old && old != device) old.Link.Close("replaced by a newer link");
        if (device.IsPaired) plugins.OnPaired(device);
        if (device.IsPaired && device.Link.Identity.DeviceType.IsPhone())
        {
            PhoneNearby = false;
            HotspotStatus = null;
            hotspot.Cancel();
            LearnHotspot(device);
        }

        Publish();
    }

    /// Looks for a paired phone over Bluetooth when none is on this network.
    private void SearchNearbyIfLost()
    {
        var lost = connected.PairedPhones.Count == 0 && trustStore.TrustedIds.Count > 0;
        if (!lost || DateTimeOffset.Now - lastNearbySearch < TimeSpan.FromSeconds(45)) return;

        lastNearbySearch = DateTimeOffset.Now;
        nearby?.Search(Nearby.Want.Connect);
    }

    private string? HotspotPhoneId => new[] { settings.SelectedPhone }
        .Concat(trustStore.TrustedIds)
        .FirstOrDefault(id => id is not null && hotspot.Name(id) is not null);

    /// Asks the phone to turn its hotspot on, then joins it when it shows up. This PC leaves its current Wi-Fi.
    public void JoinPhoneHotspot()
    {
        lastNearbySearch = DateTimeOffset.Now;
        nearby?.Search(Nearby.Want.Hotspot);

        if (HotspotPhoneId is not string id)
        {
            HotspotStatus = "Turn on your phone's hotspot and choose it from the Wi-Fi menu once.";
            return;
        }

        HotspotStatus = "Turning on your phone's hotspot…";
        hotspot.Join(id, status => ui.TryEnqueue(() => HotspotChanged(status)));
    }

    public void CancelHotspot()
    {
        hotspot.Cancel();
        HotspotStatus = null;
    }

    private void HotspotChanged(PhoneHotspot.Status? status)
    {
        if (HotspotStatus is null) return;

        HotspotStatus = status switch
        {
            PhoneHotspot.Status.Waiting waiting => $"Tap the notification on your phone to turn on {waiting.Ssid}.",
            PhoneHotspot.Status.Joining joining => $"Joining {joining.Ssid}…",
            PhoneHotspot.Status.Failed failed => failed.Reason,
            _ => "Connecting…",
        };
    }

    /// Remembers the phone's hotspot when this PC is on it.
    private void LearnHotspot(Device device)
    {
        var (id, host) = (device.Id, device.Link.Host);

        Task.Run(() =>
        {
            hotspot.Learn(id, host);
            ui.TryEnqueue(() =>
            {
                Raise(nameof(PhoneHotspotName));
                Raise(nameof(HotspotButton));
                Raise(nameof(HotspotText));
            });
        });
    }

    /// Connects to the addresses the phone gave over Bluetooth.
    private void Reach(Nearby.Answer answer)
    {
        if (link is not LanLink lan) return;

        var peer = new DeviceIdentity { DeviceId = answer.DeviceId, DeviceName = "Phone", DeviceType = DeviceType.Phone, TcpPort = answer.Port };
        foreach (var address in answer.Addresses)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await lan.ConnectAsync(peer, address);
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not reach {address}: {e.Message}");
                }
            });
        }
    }

    private void Remove(Device device)
    {
        if (!connected.Remove(device)) return;

        media.Forget(device.Id);
        inputShare.Forget(device.Id);
        screenHost.Stop(device.Id);
        relay.Forget(device.Id);
        Mirror.Ended(device.Id);
        Desktop.Ended(device.Id);
        if (Call?.DeviceId == device.Id) EndCall();

        Publish();
    }

    private void Publish()
    {
        Devices = connected.All
            .Select(device =>
            {
                var state = device.PairState;
                var showsCode = state is PairState.Requested or PairState.RequestedByPeer;

                return new DeviceState(
                    device.Id,
                    device.Name,
                    device.Link.Identity.DeviceType,
                    device.Link.Host,
                    state,
                    showsCode ? device.VerificationCode : null,
                    battery.Status(device.Id),
                    media.NowPlaying(device.Id),
                    locations.Location(device.Id),
                    device.Link.Identity.IncomingCapabilities.Contains(PacketType.FlyyDropRequest),
                    flyyDropSends.GetValueOrDefault(device.Id),
                    connectivity.Signal(device.Id),
                    device.Features);
            })
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var shown = Shown;
        Focused = Devices.FirstOrDefault(d => d.PairState is PairState.RequestedByPeer or PairState.Requested)
            ?? Devices.FirstOrDefault(d => d.Id == shown?.Id);
        Others = Devices.Where(d => !d.IsPaired && d.Id != Focused?.Id).ToList();

        SyncStores(Primary);
        ShowDrives();
        SendWaiting();
        SaveStatus();
    }

    private void ShowDrives()
    {
        if (!ShowsPhoneInExplorer) return;

        var phones = connected.PairedPhones.Where(phone => SharesFiles(phone.Id)).Select(phone => (phone.Id, phone.Name)).ToList();
        if (phones.Count > 0) Task.Run(() => drives.Show(phones));
    }

    /// Checks the phone folders for changes made on the phone, about once a minute.
    private void RefreshDrives()
    {
        if (!ShowsPhoneInExplorer || DateTimeOffset.Now - drivesRefreshed < TimeSpan.FromMinutes(1)) return;

        drivesRefreshed = DateTimeOffset.Now;
        _ = Task.Run(drives.RefreshAsync);
    }

    private void SaveStatus()
    {
        var phones = Devices.Where(d => d.IsPaired && d.IsPhone)
            .OrderBy(d => d.Id == Focused?.Id ? 0 : 1)
            .Select(d => new PhoneStatus.Phone(d.Id, d.Name, d.Battery?.Charge, d.IsCharging, IsConnected: true))
            .ToList();
        var next = new PhoneStatus(phones);
        if (next.Matches(status)) return;

        status = next;
        status.Save();
    }

    /// The paired phone or computer the panel shows. Every paired device stays connected.
    private Device? Shown => new DeviceFocus(settings.SelectedPhone).Pick(connected.Paired, d => d.Id, d => d.Name);

    /// The paired phone Messages and Photos use: the one shown, or else the first paired phone.
    private Device? Primary => new DeviceFocus(settings.SelectedPhone).Pick(connected.PairedPhones, d => d.Id, d => d.Name);

    /// Texts and photos come from the primary phone, while it has those features on.
    private void SyncStores(Device? phone)
    {
        var photosPhone = phone?.Features.Contains(PhoneFeature.Photos) == true ? phone : null;
        var messagesPhone = phone?.Features.Contains(PhoneFeature.Messages) == true ? phone : null;

        folderPhone = photosPhone;
        if (photosPhone is not null && ShowsPhotosInExplorer)
        {
            folder.Use(photosPhone.Id);
            folder.Connect();
        }

        if (photosPhone is not null) Photos.Attach(photosPhone.Id);
        else if ((phone?.Id ?? messagesDevice) is string photosId) Photos.Detach(photosId);

        if (messagesPhone is not null)
        {
            if (messagesDevice != messagesPhone.Id || !Messages.HasPhone) Messages.Attach(messagesPhone.Id);
            messagesDevice = messagesPhone.Id;
        }
        else if (messagesDevice is string id)
        {
            Messages.Detach(id);
        }
    }

    /// Keeps loading pages until the phone folder has its most recent photos.
    private void FillPhoneFolder()
    {
        if (ShowsPhotosInExplorer && folder.IsConnected && Photos.HasPhone && Photos.HasMore && Photos.Count < PhoneFolder.Limit) Photos.LoadMore();
    }

    private static DeviceCertificate LoadCertificate(string deviceId)
    {
        // Reuse the stored certificate only if it belongs to this device id. Peers refuse a certificate
        // whose common name differs from the id we announce.
        try
        {
            var stored = DeviceCertificate.FromPfx(File.ReadAllBytes(AppPaths.Certificate));
            if (stored.DeviceId == deviceId) return stored;
        }
        catch (Exception e) when (e is IOException or System.Security.Cryptography.CryptographicException or ProtocolException)
        {
        }

        var certificate = DeviceCertificate.Generate(deviceId);
        File.WriteAllBytes(AppPaths.Certificate, certificate.ExportPfx());

        return certificate;
    }

    // IDeviceDelegate, called on network threads.

    void IDeviceDelegate.DevicePairRequested(Device device, string code) => ui.TryEnqueue(() =>
    {
        Publish();
        PairRequested?.Invoke();
    });

    void IDeviceDelegate.DevicePairStateChanged(Device device, PairState state)
    {
        if (state == PairState.Paired) plugins.OnPaired(device);

        ui.TryEnqueue(Publish);
    }

    void IDeviceDelegate.DeviceReceived(Device device, NetworkPacket packet)
    {
        if (packet.Type == PacketType.Features)
        {
            plugins.OnFeaturesChanged(device);
            ui.TryEnqueue(Publish);
            return;
        }

        plugins.Dispatch(packet, device);
    }

    void IDeviceDelegate.DeviceDisconnected(Device device) => ui.TryEnqueue(() => Remove(device));

    /// A phone asked to pair, so the panel should open.
    public event Action? PairRequested;

    /// Messages should open, writing to this number when one is given.
    public event Action<string?>? MessagesRequested;

    public event Action? PhotosRequested;

    /// The dialer should open, to call from the phone.
    public event Action? DialerRequested;

    public void ShowMessages(string? number = null) => MessagesRequested?.Invoke(number);

    public void ShowPhotos() => PhotosRequested?.Invoke();

    public event Action? SettingsRequested;

    public void ShowSettings() => SettingsRequested?.Invoke();

    public void ShowDialer() => DialerRequested?.Invoke();

    /// Asks the phone or computer to share its screen and opens the window that shows it.
    public void StartMirroring(string id)
    {
        if (connected[id] is not { IsPaired: true } device) return;

        if (device.Link.Identity.DeviceType.IsComputer())
        {
            Desktop.Begin(id, device.Name);
            DesktopRequested?.Invoke();
            return;
        }

        Mirror.Begin(id, device.Name);
        MirrorRequested?.Invoke();
    }

    public event Action? MirrorRequested;

    /// Another computer's screen should show, because it was asked for or started.
    public event Action? DesktopRequested;

    /// A decoded picture of the other computer's screen, on the UI thread. Whoever shows it owns it.
    public event Action<Windows.Graphics.Imaging.SoftwareBitmap>? DesktopPicture;

    /// A phone started streaming its screen, whether asked here or on the phone. Carries the decoder's source.
    public event Action<Windows.Media.Core.MediaStreamSource>? ScreenStarted;
}
