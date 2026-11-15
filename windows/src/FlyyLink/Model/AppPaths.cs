using System.Runtime.InteropServices;

namespace FlyyLink.Model;

/// Where FlyyLink keeps its files.
internal static class AppPaths
{
    private static readonly Guid DownloadsFolder = new("374DE290-123F-4565-9164-39C4925E467B");

    /// Settings, keys and paired devices: %LOCALAPPDATA%\FlyyLink.
    public static string Data { get; } = Folder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlyyLink"));

    /// Notification icons and pasted files, safe to delete.
    public static string Cache { get; } = Folder(Path.Combine(Data, "Cache"));

    /// Where received files go.
    public static string Downloads { get; } = Folder(KnownDownloads() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));

    public static string Settings => Path.Combine(Data, "settings.json");

    public static string TrustedDevices => Path.Combine(Data, "trusted.json");

    public static string Transfers => Path.Combine(Data, "transfers.json");

    public static string Licence => Path.Combine(Data, "licence.json");

    /// The newest server notice this PC has seen.
    public static string ServerNotices => Path.Combine(Data, "notices.txt");

    /// The paired phones' battery, read by scripts.
    public static string Status => Path.Combine(Data, "status.json");

    public static string Certificate => Path.Combine(Data, "device.pfx");

    /// The phone's photos in Explorer: %USERPROFILE%\Phone Photos.
    public static string PhonePhotos => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Phone Photos");

    public static string PhonePhotosRecord => Path.Combine(Data, "phone-photos.json");

    /// Which folder in Explorer each phone's storage shows in.
    public static string PhoneDrives => Path.Combine(Data, "phone-drives.json");

    public static string AppIcon => Path.Combine(AppContext.BaseDirectory, "Assets", "FlyyLink.png");

    public static string WindowIcon => Path.Combine(AppContext.BaseDirectory, "Assets", "FlyyLink.ico");

    public static string TrayIcon => Path.Combine(AppContext.BaseDirectory, "Assets", "Tray.ico");

    private static string Folder(string path)
    {
        Directory.CreateDirectory(path);

        return path;
    }

    private static string? KnownDownloads()
    {
        if (SHGetKnownFolderPath(DownloadsFolder, 0, IntPtr.Zero, out var pointer) != 0) return null;

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
