using System.Runtime.InteropServices;

namespace FlyyLink.Shell;

/// The WinSparkle.dll calls FlyyLink uses.
internal static class WinSparkle
{
    private const string Dll = "WinSparkle.dll";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int CanShutdownCallback();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ShutdownRequestCallback();

    [DllImport(Dll, EntryPoint = "win_sparkle_init", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Init();

    [DllImport(Dll, EntryPoint = "win_sparkle_cleanup", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Cleanup();

    [DllImport(Dll, EntryPoint = "win_sparkle_set_appcast_url", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void SetAppcastUrl(string url);

    /// Returns 1 when the key is valid.
    [DllImport(Dll, EntryPoint = "win_sparkle_set_eddsa_public_key", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int SetEdDsaPublicKey(string key);

    [DllImport(Dll, EntryPoint = "win_sparkle_set_app_details", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void SetAppDetails(string company, string app, string version);

    [DllImport(Dll, EntryPoint = "win_sparkle_set_registry_path", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void SetRegistryPath(string path);

    [DllImport(Dll, EntryPoint = "win_sparkle_set_automatic_check_for_updates", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetAutomaticCheckForUpdates(int state);

    [DllImport(Dll, EntryPoint = "win_sparkle_set_update_check_interval", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetUpdateCheckInterval(int seconds);

    [DllImport(Dll, EntryPoint = "win_sparkle_set_can_shutdown_callback", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetCanShutdownCallback(CanShutdownCallback callback);

    [DllImport(Dll, EntryPoint = "win_sparkle_set_shutdown_request_callback", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetShutdownRequestCallback(ShutdownRequestCallback callback);

    [DllImport(Dll, EntryPoint = "win_sparkle_check_update_with_ui", CallingConvention = CallingConvention.Cdecl)]
    public static extern void CheckUpdateWithUI();
}
