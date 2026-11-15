using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace FlyyLink.Shell;

/// Apps' names by their AppUserModelID, as the Start menu shows them.
internal static class AppNames
{
    private const int NormalDisplay = 0;

    private static readonly ConcurrentDictionary<string, string> Names = new();

    public static string Of(string appId) => Names.GetOrAdd(appId, Look);

    private static string Look(string appId)
    {
        try
        {
            var iid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName($@"shell:AppsFolder\{appId}", IntPtr.Zero, ref iid, out var found);
            ((IShellItem)found).GetDisplayName(NormalDisplay, out var pointer);

            try
            {
                if (Marshal.PtrToStringUni(pointer) is { Length: > 0 } name) return name;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine($"No name for {appId}: {e.Message}");
        }

        return appId;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);

        void GetParent(out IShellItem parent);

        void GetDisplayName(int form, out IntPtr name);

        void GetAttributes(uint mask, out uint attributes);

        void Compare(IShellItem other, uint hint, out int order);
    }
}
