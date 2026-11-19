using FlyyLink.Model;
using FlyyLink.Shell;
using FlyyLink.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace FlyyLink;

/// Lives in the notification area: owns the tray icon, the panel and the app model.
public partial class App : Application
{
    private AppModel? model;
    private TrayIcon? tray;
    private GlobalShortcuts? shortcuts;
    private PanelWindow? panel;
    private CallWindow? call;
    private RequestWindow? requests;
    private MessagesWindow? messages;
    private PhotosWindow? photos;
    private ScreenWindow? screen;
    private RemoteDesktopWindow? desktop;
    private TvRemoteWindow? remote;
    private SettingsWindow? settings;

    public App()
    {
        InitializeComponent();
        CrashLog.Attach(this);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Closing the panel only hides it; FlyyLink keeps running in the tray until Quit.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

        var ui = DispatcherQueue.GetForCurrentThread();
        model = new AppModel(ui);
        panel = new PanelWindow(model);
        tray = new TrayIcon(AppPaths.TrayIcon, "FlyyLink");

        tray.Clicked += () => panel.Toggle(tray.Bounds);
        tray.MenuRequested += ShowTrayMenu;
        tray.ClipboardChanged += model.ClipboardChanged;
        shortcuts = new GlobalShortcuts(tray);
        shortcuts.Pressed += task =>
        {
            if (task == JumpTask.Panel) panel.Toggle(tray.Bounds);
            else Open(task);
        };
        shortcuts.Use(model.UsesShortcuts);
        model.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(AppModel.UsesShortcuts)) shortcuts.Use(model.UsesShortcuts);
            if (change.PropertyName == nameof(AppModel.Focused)) ShowPhoneOnTray();
        };
        model.FlyyDropRequested += () => panel.ShowNear(tray.Bounds);
        panel.QuitRequested += Quit;
        model.UpdateInstalling += Quit;
        call = new CallWindow(model);
        requests = new RequestWindow(model);
        model.MessagesRequested += number => (messages ??= new MessagesWindow(model)).Present(number);
        model.DialerRequested += () => (messages ??= new MessagesWindow(model)).Present(dial: true);
        model.MirrorRequested += () => (screen ??= new ScreenWindow(model.Mirror)).Present();
        model.DesktopRequested += () => (desktop ??= new RemoteDesktopWindow(model.Desktop)).Present();
        model.RemoteRequested += id => (remote ??= new TvRemoteWindow(model)).Present(id);
        model.DesktopPicture += bitmap =>
        {
            if (desktop is null) bitmap.Dispose();
            else desktop.Show(bitmap);
        };
        model.ScreenStarted += source =>
        {
            screen ??= new ScreenWindow(model.Mirror);
            screen.Play(source);
            screen.Present();
        };
        model.PhotosRequested += () => (photos ??= new PhotosWindow(model.Photos)).Present();
        model.SettingsRequested += () =>
        {
            if (settings is null)
            {
                settings = new SettingsWindow(model);
                settings.PanelRequested += () => panel.ShowNear(tray.Bounds);
            }
            settings.Present();
        };

        // A second launch, such as from the Start menu, Explorer's "Send to phone" or a tel: link, comes here.
        AppInstance.GetCurrent().Activated += (_, activation) =>
        {
            var launch = activation.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs data
                ? LaunchArguments.Parse(data.Arguments ?? "")
                : new LaunchArguments([], null, false);
            ui.TryEnqueue(() => Handle(launch));
        };

        Integration.Register();
        Startup.Refresh();
        JumpList.Register();
        model.Start();

        var first = LaunchArguments.Parse(Environment.GetCommandLineArgs().Skip(1));
        if (!first.IsEmpty) Handle(first);
        else if (!first.Background && (!model.HasPaired || model.Licence.IsLocked)) panel.ShowNear(tray.Bounds);
    }

    /// Sends files or calls a number handed over by Windows, or just opens the panel.
    private void Handle(LaunchArguments launch)
    {
        if (launch.IsEmpty)
        {
            panel!.ShowNear(tray!.Bounds);
            return;
        }

        if (launch.Jump is JumpTask jump)
        {
            Open(jump);
            return;
        }

        model!.Handover(launch.Files, launch.Number, launch.Text);

        // The panel shows the files going across.
        if (launch.Files.Count > 0) panel!.ShowNear(tray!.Bounds);
    }

    /// A jump list task. Phone tasks fall back to the panel when no phone is connected.
    private void Open(JumpTask jump)
    {
        var phone = model!.Focused is { IsPaired: true } focused ? focused.Id : null;

        switch (jump)
        {
            case JumpTask.Messages:
                model.ShowMessages();
                break;
            case JumpTask.Photos:
                model.ShowPhotos();
                break;
            case JumpTask.Mirror when phone is not null:
                model.StartMirroring(phone);
                break;
            case JumpTask.Ring when phone is not null:
                model.Ring(phone);
                panel!.ShowNear(tray!.Bounds);
                break;
            case JumpTask.SendClipboard when phone is not null:
                model.SendClipboard(phone);
                break;
            case JumpTask.SendFiles when phone is not null:
                panel!.ShowNear(tray!.Bounds);
                panel.ChooseFiles(phone);
                break;
            default:
                panel!.ShowNear(tray!.Bounds);
                break;
        }
    }

    /// The shown phone's battery on the tray icon and in its tooltip.
    private void ShowPhoneOnTray()
    {
        var phone = model!.Focused is { IsPaired: true } focused ? focused : null;
        tray!.ShowBattery(phone?.Battery);
        tray.Tooltip = phone switch
        {
            null => "FlyyLink",
            { Battery: { } battery } => $"FlyyLink · {phone.Name} · {battery.Charge}%{(battery.IsCharging ? ", charging" : "")}",
            _ => $"FlyyLink · {phone.Name}",
        };
    }

    private void ShowTrayMenu()
    {
        switch (tray!.ShowMenu(["Open FlyyLink", "Messages", "Photos", "Phone Photos folder", "Open Downloads", "Settings", null, "Quit FlyyLink"]))
        {
            case 0:
                panel!.ShowNear(tray.Bounds);
                break;
            case 1:
                model!.ShowMessages();
                break;
            case 2:
                model!.ShowPhotos();
                break;
            case 3:
                Explorer.Open(model!.PhonePhotosFolder);
                break;
            case 4:
                Explorer.Open(AppPaths.Downloads);
                break;
            case 5:
                model!.ShowSettings();
                break;
            case 7:
                Quit();
                break;
        }
    }

    private void Quit()
    {
        model?.Stop();
        tray?.Dispose();
        panel?.Shutdown();
        call?.Close();
        requests?.Close();
        messages?.Close();
        photos?.Close();
        screen?.Close();
        settings?.Close();
        Exit();
    }
}
