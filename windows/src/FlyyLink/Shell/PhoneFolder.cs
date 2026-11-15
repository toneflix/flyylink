using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlyyLink.Core;
using Vanara.Extensions;
using Vanara.PInvoke;
using static Vanara.PInvoke.CldApi;

namespace FlyyLink.Shell;

/// The phone's recent photos as a folder in Explorer, through the Windows Cloud Files API.
/// Each photo is a placeholder that takes no space until it's opened; then Windows asks us for its bytes
/// and we fetch the original from the phone.
internal sealed class PhoneFolder
{
    /// Like Phone Link, only the most recent photos show.
    public const int Limit = 2000;

    private static readonly Guid ProviderId = new("6F0C2C31-8B0B-4C8B-9E9F-2B7A3F0E5D11");

    private readonly string root;
    private readonly string recordPath;
    private readonly Record record;
    private readonly object gate = new();
    private readonly ConcurrentDictionary<long, List<CloudFetch>> waiting = new();
    private readonly CF_CALLBACK fetchData;
    private readonly CF_CALLBACK cancelFetch;
    private CF_CONNECTION_KEY connection;
    private bool connected;

    /// Which phone the placeholders are from, and which photos already got one,
    /// so photos the user deleted from the folder don't come back.
    private sealed class Record
    {
        public string? Phone { get; set; }
        public HashSet<long> Ids { get; set; } = [];
    }

    /// `root` is the folder to show the photos in; `recordPath` is where to keep which photos it has.
    public PhoneFolder(string root, string recordPath)
    {
        this.root = root;
        this.recordPath = recordPath;
        record = Load(recordPath);

        // Held so the garbage collector does not free the callbacks Windows calls into.
        fetchData = FetchData;
        cancelFetch = CancelFetch;
    }

    public string Root => root;

    public bool IsConnected => connected;

    /// Asks the phone for a photo in full. Returns false when no phone is connected.
    /// The photo comes back through `Delivered`.
    public Func<long, bool>? Request { get; set; }

    /// Makes the folder a sync root and starts answering for its placeholders.
    public bool Connect()
    {
        if (connected) return true;

        try
        {
            Directory.CreateDirectory(root);

            var identity = Marshal.StringToCoTaskMemUni("flyylink");
            try
            {
                var registration = new CF_SYNC_REGISTRATION
                {
                    StructSize = (uint)Marshal.SizeOf<CF_SYNC_REGISTRATION>(),
                    ProviderName = "FlyyLink",
                    ProviderVersion = "1.0",
                    SyncRootIdentity = identity,
                    SyncRootIdentityLength = 12,
                    ProviderId = ProviderId,
                };
                var policies = new CF_SYNC_POLICIES
                {
                    StructSize = (uint)Marshal.SizeOf<CF_SYNC_POLICIES>(),
                    Hydration = new CF_HYDRATION_POLICY { Primary = CF_HYDRATION_POLICY_PRIMARY.CF_HYDRATION_POLICY_FULL },
                    Population = new CF_POPULATION_POLICY { Primary = CF_POPULATION_POLICY_PRIMARY.CF_POPULATION_POLICY_ALWAYS_FULL },
                    InSync = CF_INSYNC_POLICY.CF_INSYNC_POLICY_NONE,
                    HardLink = CF_HARDLINK_POLICY.CF_HARDLINK_POLICY_NONE,
                };
                CfRegisterSyncRoot(root, registration, policies, CF_REGISTER_FLAGS.CF_REGISTER_FLAG_UPDATE).ThrowIfFailed();
            }
            finally
            {
                Marshal.FreeCoTaskMem(identity);
            }

            CF_CALLBACK_REGISTRATION[] callbacks =
            [
                new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_DATA, Callback = fetchData },
                new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_CANCEL_FETCH_DATA, Callback = cancelFetch },
                CF_CALLBACK_REGISTRATION.CF_CALLBACK_REGISTRATION_END,
            ];
            CfConnectSyncRoot(root, callbacks, IntPtr.Zero, CF_CONNECT_FLAGS.CF_CONNECT_FLAG_NONE, out connection).ThrowIfFailed();
            connected = true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Older Windows, or a file system without Cloud Files support.
            System.Diagnostics.Debug.WriteLine($"Phone photos folder unavailable: {e.Message}");
        }

        return connected;
    }

    /// Shows this phone's photos. Placeholders from another phone are cleared first, since their ids mean nothing to this one.
    public void Use(string phone)
    {
        lock (gate)
        {
            if (record.Phone == phone) return;

            if (record.Ids.Count > 0) ClearPlaceholders();
            record.Phone = phone;
            record.Ids.Clear();
            Save();
        }
    }

    /// Stops answering, removes the placeholders we made and gives the folder back to the user.
    public void Remove()
    {
        lock (gate)
        {
            Disconnect();
            ClearPlaceholders();

            try
            {
                CfUnregisterSyncRoot(root);
                if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                System.Diagnostics.Debug.WriteLine($"Could not remove the phone photos folder: {e.Message}");
            }

            record.Phone = null;
            record.Ids.Clear();
            File.Delete(recordPath);
        }
    }

    /// Deletes the photos that were never opened. Opened ones are real files now, so they stay.
    private void ClearPlaceholders()
    {
        try
        {
            foreach (var file in Directory.Exists(root) ? Directory.GetFiles(root) : [])
            {
                if ((File.GetAttributes(file) & FileAttributes.Offline) != 0) File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not clear phone photos: {e.Message}");
        }
    }

    public void Disconnect()
    {
        if (!connected) return;

        CfDisconnectSyncRoot(connection);
        connected = false;
        FailAll();
    }

    /// Adds a placeholder for each photo not shown yet.
    public void Add(IEnumerable<PhonePhoto> photos)
    {
        lock (gate)
        {
            if (!connected) return;

            var fresh = photos.Where(p => p.Size > 0 && !record.Ids.Contains(p.Id)).ToList();
            if (fresh.Count == 0) return;

            var taken = new HashSet<string>(Directory.GetFiles(root).Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
            var identities = new List<IntPtr>();
            try
            {
                var infos = new List<CF_PLACEHOLDER_CREATE_INFO>();
                foreach (var photo in fresh)
                {
                    var name = UniqueName(photo, taken);
                    var identity = Marshal.AllocHGlobal(sizeof(long));
                    identities.Add(identity);
                    Marshal.WriteInt64(identity, photo.Id);

                    var time = DateTimeOffset.FromUnixTimeMilliseconds(photo.Date).UtcDateTime;
                    infos.Add(new CF_PLACEHOLDER_CREATE_INFO
                    {
                        RelativeFileName = name,
                        FsMetadata = new CF_FS_METADATA
                        {
                            FileSize = photo.Size,
                            BasicInfo = new Kernel32.FILE_BASIC_INFO
                            {
                                CreationTime = time.ToFileTimeStruct(),
                                LastWriteTime = time.ToFileTimeStruct(),
                                LastAccessTime = time.ToFileTimeStruct(),
                                ChangeTime = time.ToFileTimeStruct(),
                                FileAttributes = FileFlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                            },
                        },
                        FileIdentity = identity,
                        FileIdentityLength = sizeof(long),
                        Flags = CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC,
                    });
                }

                var array = infos.ToArray();
                CfCreatePlaceholders(root, array, (uint)array.Length, CF_CREATE_FLAGS.CF_CREATE_FLAG_NONE, out _);
                for (var i = 0; i < array.Length; i++)
                {
                    if (array[i].Result.Succeeded) record.Ids.Add(fresh[i].Id);
                }

                Save();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                System.Diagnostics.Debug.WriteLine($"Could not add phone photos: {e.Message}");
            }
            finally
            {
                identities.ForEach(Marshal.FreeHGlobal);
            }
        }
    }

    /// A photo asked for with `Request` arrived at `file`, or failed when it's null.
    public void Delivered(long id, string? file)
    {
        if (!waiting.TryRemove(id, out var fetches)) return;

        lock (fetches)
        {
            foreach (var fetch in fetches) _ = Task.Run(() => fetch.Deliver(file));
        }
    }

    private void FetchData(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        var fetch = CloudFetch.Of(info, parameters);
        if (info.FileIdentityLength < sizeof(long) || info.FileIdentity == IntPtr.Zero)
        {
            fetch.Fail();
            return;
        }

        var id = Marshal.ReadInt64(info.FileIdentity);
        var fetches = waiting.GetOrAdd(id, _ => []);
        bool first;
        lock (fetches)
        {
            first = fetches.Count == 0;
            fetches.Add(fetch);
        }

        if (first && Request?.Invoke(id) != true) Delivered(id, null);
    }

    private void CancelFetch(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        if (info.FileIdentityLength < sizeof(long) || info.FileIdentity == IntPtr.Zero) return;

        var id = Marshal.ReadInt64(info.FileIdentity);
        if (!waiting.TryGetValue(id, out var fetches)) return;

        var transfer = info.TransferKey;
        lock (fetches) fetches.RemoveAll(f => f.Transfer.Equals(transfer));
    }

    private void FailAll()
    {
        foreach (var id in waiting.Keys.ToList()) Delivered(id, null);
    }

    private static string UniqueName(PhonePhoto photo, HashSet<string> taken)
    {
        var name = Share.SafeName(string.IsNullOrWhiteSpace(photo.Name) ? $"photo-{photo.Id}" : photo.Name);
        if (taken.Add(name)) return name;

        name = $"{Path.GetFileNameWithoutExtension(name)} ({photo.Id}){Path.GetExtension(name)}";
        taken.Add(name);

        return name;
    }

    private static Record Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Record>(File.ReadAllText(path)) ?? new Record();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Record();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(recordPath, JsonSerializer.Serialize(record));
        }
        catch (IOException)
        {
        }
    }
}
