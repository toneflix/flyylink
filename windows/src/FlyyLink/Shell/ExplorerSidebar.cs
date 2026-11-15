using Microsoft.Win32;

namespace FlyyLink.Shell;

/// Pins a folder to File Explorer's navigation pane, as cloud drives are, for the current user only.
internal static class ExplorerSidebar
{
    /// Shell's "folder shortcut" object, which shows the target folder in place.
    private const string FolderShortcut = "{0E5AAE11-A475-4c5b-AB00-C66DE400274E}";
    private const string NameSpace = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace";
    private const string DesktopIcons = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";

    public static void Add(Guid id, string name, string folder)
    {
        var key = id.ToString("B");

        try
        {
            using (var entry = Registry.CurrentUser.CreateSubKey($@"Software\Classes\CLSID\{key}"))
            {
                entry.SetValue("", name);
                entry.SetValue("System.IsPinnedToNameSpaceTree", 1, RegistryValueKind.DWord);
                entry.SetValue("SortOrderIndex", 0x42, RegistryValueKind.DWord);
                using (var icon = entry.CreateSubKey("DefaultIcon")) icon.SetValue("", $"{Environment.ProcessPath},0");
                using (var server = entry.CreateSubKey("InProcServer32")) server.SetValue("", @"%SystemRoot%\system32\shell32.dll", RegistryValueKind.ExpandString);
                using (var instance = entry.CreateSubKey("Instance"))
                {
                    instance.SetValue("CLSID", FolderShortcut);
                    using var bag = instance.CreateSubKey("InitPropertyBag");
                    bag.SetValue("Attributes", 0x11, RegistryValueKind.DWord);
                    bag.SetValue("TargetFolderPath", folder);
                }
                using (var shell = entry.CreateSubKey("ShellFolder"))
                {
                    shell.SetValue("FolderValueFlags", 0x28, RegistryValueKind.DWord);
                    shell.SetValue("Attributes", unchecked((int)0xF080004D), RegistryValueKind.DWord);
                }
            }

            using (var space = Registry.CurrentUser.CreateSubKey($@"{NameSpace}\{key}")) space.SetValue("", name);
            using (var hidden = Registry.CurrentUser.CreateSubKey(DesktopIcons)) hidden.SetValue(key, 1, RegistryValueKind.DWord);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not add {name} to Explorer: {e.Message}");
        }
    }

    public static void Remove(Guid id)
    {
        var key = id.ToString("B");

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\CLSID\{key}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"{NameSpace}\{key}", throwOnMissingSubKey: false);
            using var hidden = Registry.CurrentUser.OpenSubKey(DesktopIcons, writable: true);
            hidden?.DeleteValue(key, throwOnMissingValue: false);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not remove a phone from Explorer: {e.Message}");
        }
    }
}
