using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using FlyyLink.Model;
using FlyyLink.Shell;
using Microsoft.Windows.AppLifecycle;

namespace FlyyLink;

/// Starts the app, or hands over to the copy that is already running so there is only ever one tray icon.
public static class Program
{
    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Contains(LaunchArguments.BatteryArgument)) return PrintBattery();

        if (RedirectToRunningInstance()) return 0;

        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });

        return 0;
    }

    /// Writes the shown phone's battery level, 0 to 100, for scripts. Fails when no phone is connected.
    private static int PrintBattery()
    {
        AttachConsole(AttachParentProcess);
        var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        if (PhoneStatus.Load().Phones.FirstOrDefault(p => p.IsConnected) is not { Charge: int charge })
        {
            new StreamWriter(Console.OpenStandardError()) { AutoFlush = true }.WriteLine("None of your connected phones has shared its battery yet.");
            return 1;
        }

        output.WriteLine(charge);
        return 0;
    }

    private static bool RedirectToRunningInstance()
    {
        var main = AppInstance.FindOrRegisterForKey("flyylink");
        if (main.IsCurrent) return false;

        // The redirect is a cross-process call, so wait for it without blocking this thread's COM messages.
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        var done = CreateEvent(IntPtr.Zero, true, false, null);
        Task.Run(() =>
        {
            main.RedirectActivationToAsync(args).AsTask().Wait();
            SetEvent(done);
        });
        CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, [done], out _);

        return true;
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetEvent(IntPtr handle);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint flags, uint milliseconds, uint count, IntPtr[] handles, out uint index);
}
