using System.Reflection;
using Microsoft.Win32;

namespace FlyyLink.Shell;

/// Keeps FlyyLink up to date with WinSparkle, which reads the signed update feed on the website and installs new versions.
internal sealed class Updater
{
    private const string Feed = "https://flyylink.toneflix.net/updates/windows/appcast.xml";
    private const string DownloadPage = "https://flyylink.toneflix.net/#download";
    private const string SettingsKey = @"Software\FlyyLink\WinSparkle";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(12);

    /// The running app's version.
    public static Version Current { get; } = Trim(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    /// The version shown to the user, like 1.2.3.
    public static string CurrentText => Current.ToString(3);

    /// The key update files are signed with. Builds without it don't update themselves.
    private static readonly string? PublicKey = typeof(Updater).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "UpdatePublicKey")?.Value;

    // Kept in fields so they live as long as WinSparkle holds them.
    private readonly WinSparkle.CanShutdownCallback canShutdown = () => 1;
    private readonly WinSparkle.ShutdownRequestCallback shutdownRequest;

    private bool running;

    public Updater() => shutdownRequest = () => OnInstalling?.Invoke();

    /// Called on a background thread once the installer is running, so the app can quit and let it replace the files.
    public Action? OnInstalling { get; set; }

    /// Checks in the background on launch and every 12 hours, unless the user turned that off in WinSparkle's window.
    public void Start()
    {
        if (running || string.IsNullOrEmpty(PublicKey)) return;

        WinSparkle.SetAppcastUrl(Feed);
        if (WinSparkle.SetEdDsaPublicKey(PublicKey) != 1) return;

        WinSparkle.SetAppDetails("Toneflix", "FlyyLink", CurrentText);
        WinSparkle.SetRegistryPath(SettingsKey);
        if (!HasCheckSetting()) WinSparkle.SetAutomaticCheckForUpdates(1);
        WinSparkle.SetUpdateCheckInterval((int)CheckEvery.TotalSeconds);
        WinSparkle.SetCanShutdownCallback(canShutdown);
        WinSparkle.SetShutdownRequestCallback(shutdownRequest);
        WinSparkle.Init();

        running = true;
    }

    /// Shows WinSparkle's window, which says whether FlyyLink is up to date. Builds that can't update open the download page.
    public void Check()
    {
        if (running) WinSparkle.CheckUpdateWithUI();
        else Explorer.Open(DownloadPage);
    }

    public void Stop()
    {
        if (!running) return;

        WinSparkle.Cleanup();
        running = false;
    }

    private static bool HasCheckSetting()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);

        return key?.GetValue("CheckForUpdates") is not null;
    }

    private static Version Trim(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
