using System.Net;
using System.Net.NetworkInformation;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;

namespace FlyyLink.Shell;

/// The phone's own hotspot. FlyyLink learns its name the first time this PC is on it, then can join it again on request
/// once the phone has turned it on. Windows keeps the password, so we never see it.
internal sealed class PhoneHotspot(Func<IReadOnlyDictionary<string, string>> names, Action<string, string> remember)
{
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(2);

    private int attempt;

    /// What a join is doing, for the panel. A null status means joined.
    public abstract record Status
    {
        public sealed record Waiting(string Ssid) : Status;

        public sealed record Joining(string Ssid) : Status;

        public sealed record Failed(string Reason) : Status;
    }

    public string? Name(string deviceId) => names().GetValueOrDefault(deviceId);

    /// Remembers the Wi-Fi this PC is on as the phone's hotspot when the phone is its router.
    public void Learn(string deviceId, string phoneHost)
    {
        var host = phoneHost.StartsWith("::ffff:", StringComparison.Ordinal) ? phoneHost[7..] : phoneHost;
        if (!IPAddress.TryParse(host, out var phone) || !IsRouter(phone)) return;

        var ssid = ConnectedSsid();
        if (ssid is not null && Name(deviceId) != ssid) remember(deviceId, ssid);
    }

    /// Waits for the phone's hotspot to show up, then joins it. `onStatus` is called on a background thread.
    public void Join(string deviceId, Action<Status?> onStatus)
    {
        if (Name(deviceId) is not string ssid) return;

        var current = Interlocked.Increment(ref attempt);
        _ = PollAsync(ssid, current, DateTimeOffset.Now + GiveUpAfter, onStatus);
    }

    public void Cancel() => Interlocked.Increment(ref attempt);

    private async Task PollAsync(string ssid, int current, DateTimeOffset deadline, Action<Status?> onStatus)
    {
        try
        {
            if (await WiFiAdapter.RequestAccessAsync() != WiFiAccessStatus.Allowed)
            {
                onStatus(new Status.Failed("Turn on location for apps in Settings to use Wi-Fi."));
                return;
            }

            var adapters = await WiFiAdapter.FindAllAdaptersAsync();
            if (adapters.Count == 0)
            {
                onStatus(new Status.Failed("This PC doesn't have Wi-Fi"));
                return;
            }

            var adapter = adapters[0];
            while (current == attempt)
            {
                if (ConnectedSsid() == ssid)
                {
                    onStatus(null);
                    return;
                }

                await adapter.ScanAsync();
                var network = adapter.NetworkReport.AvailableNetworks.FirstOrDefault(n => n.Ssid == ssid);
                if (network is not null)
                {
                    onStatus(new Status.Joining(ssid));
                    var result = await adapter.ConnectAsync(network, WiFiReconnectionKind.Automatic);
                    onStatus(result.ConnectionStatus == WiFiConnectionStatus.Success
                        ? null
                        : new Status.Failed($"Couldn't join {ssid}. Pick it once in the Wi-Fi menu."));
                    return;
                }

                if (DateTimeOffset.Now > deadline)
                {
                    onStatus(new Status.Failed($"Couldn't find {ssid}. Turn on your phone's hotspot and try again."));
                    return;
                }

                onStatus(new Status.Waiting(ssid));
                await Task.Delay(PollEvery);
            }
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            onStatus(new Status.Failed($"Couldn't use Wi-Fi: {e.Message}"));
        }
    }

    private static bool IsRouter(IPAddress phone) => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().GatewayAddresses)
        .Any(g => g.Address.Equals(phone));

    /// The Wi-Fi network this PC is on. Newer Windows hides the name unless location is allowed for apps.
    private static string? ConnectedSsid()
    {
        try
        {
            var details = NetworkInformation.GetInternetConnectionProfile()?.WlanConnectionProfileDetails;

            return details?.GetConnectedSsid() is { Length: > 0 } ssid ? ssid : null;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
