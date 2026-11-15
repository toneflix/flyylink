using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using FlyyLink.Core;
using FlyyLink.Plugins;
using Vanara.Extensions;
using Vanara.PInvoke;
using static Vanara.PInvoke.CldApi;

namespace FlyyLink.Shell;

/// Talks to the phone a drive shows. `SendAsync` throws `PhoneFilesException` when the phone says no or isn't there.
internal interface IPhoneStorage
{
    Task<PhoneFiles.Answer> SendAsync(PhoneFiles.Action action, string path, string? to = null, string? upload = null, string? saveTo = null);
}

/// A phone's whole storage as a folder in Explorer, through the Windows Cloud Files API. Folders fill in when they are
/// first opened and files download when they are opened. Deleting and renaming go to the phone as they happen; files
/// and folders copied in are sent to the phone once they are written. Folders opened recently are checked again for
/// changes made on the phone.
internal sealed class PhoneDrive
{
    private static readonly NTStatus Denied = NTStatus.STATUS_ACCESS_DENIED;
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase) { "desktop.ini", "Thumbs.db" };

    /// How many opened folders are checked again for changes.
    private const int WatchedLimit = 30;

    private readonly string root;
    private readonly string cache;
    private readonly Guid providerId;
    private readonly IPhoneStorage phone;
    private readonly CF_CALLBACK fetchPlaceholders;
    private readonly CF_CALLBACK fetchData;
    private readonly CF_CALLBACK notifyDelete;
    private readonly CF_CALLBACK notifyRename;
    private readonly ConcurrentDictionary<string, Task<string?>> downloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> opened = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> ownDeletes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim sending = new(1, 1);
    private FileSystemWatcher? watcher;
    private CF_CONNECTION_KEY connection;
    private bool connected;

    /// `root` is the folder to show the phone in; `cache` is where opened files land on the way in.
    public PhoneDrive(string root, string cache, Guid providerId, IPhoneStorage phone)
    {
        this.root = Path.GetFullPath(root);
        this.cache = cache;
        this.providerId = providerId;
        this.phone = phone;

        // Held so the garbage collector does not free the callbacks Windows calls into.
        fetchPlaceholders = FetchPlaceholders;
        fetchData = FetchData;
        notifyDelete = NotifyDelete;
        notifyRename = NotifyRename;
    }

    public string Root => root;

    public bool IsConnected => connected;

    /// Makes the folder a sync root and starts answering for it.
    public bool Connect(string name)
    {
        if (connected) return true;

        try
        {
            Directory.CreateDirectory(root);
            Register(name);

            CF_CALLBACK_REGISTRATION[] callbacks =
            [
                new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_PLACEHOLDERS, Callback = fetchPlaceholders },
                new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_DATA, Callback = fetchData },
                new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_DELETE, Callback = notifyDelete },
                new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_RENAME, Callback = notifyRename },
                CF_CALLBACK_REGISTRATION.CF_CALLBACK_REGISTRATION_END,
            ];
            CfConnectSyncRoot(root, callbacks, IntPtr.Zero, CF_CONNECT_FLAGS.CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH, out connection).ThrowIfFailed();
            connected = true;
            Watch();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Older Windows, or a file system without Cloud Files support.
            System.Diagnostics.Debug.WriteLine($"Phone drive unavailable: {e.Message}");
        }

        return connected;
    }

    public void Disconnect()
    {
        watcher?.Dispose();
        watcher = null;
        if (!connected) return;

        CfDisconnectSyncRoot(connection);
        connected = false;
    }

    /// Stops answering, removes what was never opened and gives the folder back to the user.
    public void Remove()
    {
        Disconnect();
        RemovePlaceholders(root);

        try
        {
            CfUnregisterSyncRoot(root);
            if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not remove the phone drive: {e.Message}");
        }
    }

    /// Lists the folders opened most recently again, and adds or removes what changed on the phone.
    public async Task RefreshAsync()
    {
        if (!connected) return;

        var folders = opened.OrderByDescending(pair => pair.Value).Take(WatchedLimit).Select(pair => pair.Key).ToList();
        foreach (var folder in opened.Keys.Except(folders).ToList()) opened.TryRemove(folder, out _);

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                opened.TryRemove(folder, out _);
                continue;
            }

            try
            {
                var answer = await phone.SendAsync(PhoneFiles.Action.List, PhonePath(folder));
                Merge(folder, answer.Items);
            }
            catch (PhoneFilesException e) when (e.Failure == PhoneFiles.Failure.Missing)
            {
                opened.TryRemove(folder, out _);
            }
            catch (PhoneFilesException)
            {
                return;
            }
        }
    }

    // MARK: Windows asks

    private void FetchPlaceholders(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        var folder = LocalPath(info);
        var request = (info.ConnectionKey, info.TransferKey, info.RequestKey);

        _ = Task.Run(async () =>
        {
            try
            {
                var answer = await phone.SendAsync(PhoneFiles.Action.List, PhonePath(folder));
                var taken = Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

                TransferPlaceholders(request, answer.Items.Where(file => !taken.Contains(file.Name)).ToList(), NTStatus.STATUS_SUCCESS);
                opened[folder] = DateTimeOffset.Now;
            }
            catch (Exception e) when (e is PhoneFilesException or IOException or UnauthorizedAccessException)
            {
                TransferPlaceholders(request, [], StatusOf(e));
            }
        });
    }

    private void FetchData(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        var fetch = CloudFetch.Of(info, parameters);
        var path = PhonePath(LocalPath(info));

        _ = Task.Run(async () => fetch.Deliver(await downloads.GetOrAdd(path, Download)));
    }

    private void NotifyDelete(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        var local = LocalPath(info);
        var request = (info.ConnectionKey, info.TransferKey, info.RequestKey);

        if (ownDeletes.TryRemove(local, out _))
        {
            Acknowledge(request, CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_DELETE, NTStatus.STATUS_SUCCESS);
            return;
        }

        _ = Task.Run(async () =>
        {
            NTStatus status = NTStatus.STATUS_SUCCESS;
            try
            {
                await phone.SendAsync(PhoneFiles.Action.Delete, PhonePath(local));
            }
            catch (PhoneFilesException e)
            {
                status = StatusOf(e);
            }

            Acknowledge(request, CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_DELETE, status);
        });
    }

    /// Moving out of the drive is refused, so a drag to the desktop never takes a file off the phone.
    private void NotifyRename(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        var rename = parameters.Rename;
        var request = (info.ConnectionKey, info.TransferKey, info.RequestKey);
        var from = LocalPath(info);
        var to = Path.GetFullPath(info.VolumeDosName + rename.TargetPath);

        if (!rename.Flags.HasFlag(CF_CALLBACK_RENAME_FLAGS.CF_CALLBACK_RENAME_FLAG_TARGET_IN_SCOPE))
        {
            Acknowledge(request, CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_RENAME, Denied);
            return;
        }

        _ = Task.Run(async () =>
        {
            NTStatus status = NTStatus.STATUS_SUCCESS;
            try
            {
                await phone.SendAsync(PhoneFiles.Action.Move, PhonePath(from), PhonePath(to));
                if (opened.TryRemove(from, out var seen)) opened[to] = seen;
            }
            catch (PhoneFilesException e)
            {
                status = StatusOf(e);
            }

            Acknowledge(request, CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_RENAME, status);
        });
    }

    // MARK: Changes made in Explorer

    /// New files and edited ones are sent once nothing is writing them any more.
    private void Watch()
    {
        watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Created += (_, args) => Queue(args.FullPath);
        watcher.Changed += (_, args) => Queue(args.FullPath);
        watcher.Renamed += (_, args) => Queue(args.FullPath);
        watcher.EnableRaisingEvents = true;
    }

    private void Queue(string path)
    {
        if (Skipped.Contains(Path.GetFileName(path)) || Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal)) return;
        if (!pending.TryAdd(path, 0)) return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            pending.TryRemove(path, out _);

            await sending.WaitAsync();
            try
            {
                await SendAsync(path);
            }
            finally
            {
                sending.Release();
            }
        });
    }

    /// Sends a folder or file made or changed in Explorer, after its parent folder.
    private async Task SendAsync(string local)
    {
        var state = StateOf(local);
        if (state is null || state.Value.HasFlag(CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_IN_SYNC)) return;

        var parent = Path.GetDirectoryName(local)!;
        if (!SamePath(parent, root) && StateOf(parent) is CF_PLACEHOLDER_STATE folder && !folder.HasFlag(CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PLACEHOLDER))
        {
            await SendAsync(parent);
        }

        try
        {
            if (Directory.Exists(local))
            {
                await MakeFolderAsync(local);
                foreach (var entry in Directory.EnumerateFileSystemEntries(local)) await SendAsync(entry);
            }
            else if (IsClosed(local))
            {
                await phone.SendAsync(PhoneFiles.Action.Upload, PhonePath(local), upload: local);
                MarkInSync(local, isPlaceholder: state.Value.HasFlag(CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PLACEHOLDER));
            }
            else
            {
                Queue(local);
            }
        }
        catch (PhoneFilesException e)
        {
            System.Diagnostics.Debug.WriteLine($"Could not send {local} to the phone: {e.Message}");
        }
    }

    private async Task MakeFolderAsync(string local)
    {
        try
        {
            await phone.SendAsync(PhoneFiles.Action.Folder, PhonePath(local));
        }
        catch (PhoneFilesException e) when (e.Failure == PhoneFiles.Failure.Exists)
        {
        }

        MarkInSync(local, isPlaceholder: false);
        opened[local] = DateTimeOffset.Now;
    }

    // MARK: Placeholders

    private void Register(string name)
    {
        var identity = Marshal.StringToCoTaskMemUni("flyylink-drive");
        try
        {
            var registration = new CF_SYNC_REGISTRATION
            {
                StructSize = (uint)Marshal.SizeOf<CF_SYNC_REGISTRATION>(),
                ProviderName = $"FlyyLink {name}",
                ProviderVersion = "1.0",
                SyncRootIdentity = identity,
                SyncRootIdentityLength = 28,
                ProviderId = providerId,
            };
            var policies = new CF_SYNC_POLICIES
            {
                StructSize = (uint)Marshal.SizeOf<CF_SYNC_POLICIES>(),
                Hydration = new CF_HYDRATION_POLICY { Primary = CF_HYDRATION_POLICY_PRIMARY.CF_HYDRATION_POLICY_FULL },
                Population = new CF_POPULATION_POLICY { Primary = CF_POPULATION_POLICY_PRIMARY.CF_POPULATION_POLICY_FULL },
                InSync = CF_INSYNC_POLICY.CF_INSYNC_POLICY_TRACK_ALL,
                HardLink = CF_HARDLINK_POLICY.CF_HARDLINK_POLICY_NONE,
            };
            CfRegisterSyncRoot(root, registration, policies, CF_REGISTER_FLAGS.CF_REGISTER_FLAG_UPDATE).ThrowIfFailed();
        }
        finally
        {
            Marshal.FreeCoTaskMem(identity);
        }
    }

    /// Answers a folder listing with placeholders for what the phone has there.
    private static void TransferPlaceholders((CF_CONNECTION_KEY, CF_TRANSFER_KEY, CF_REQUEST_KEY) request, IReadOnlyList<PhoneFile> files, NTStatus status)
    {
        var infos = files.Select(Placeholder).ToArray();
        var size = Marshal.SizeOf<CF_PLACEHOLDER_CREATE_INFO>();
        var array = Marshal.AllocHGlobal(Math.Max(1, size * infos.Length));
        var identities = new List<IntPtr>();
        try
        {
            for (var i = 0; i < infos.Length; i++)
            {
                identities.Add(infos[i].FileIdentity);
                Marshal.StructureToPtr(infos[i], array + i * size, false);
            }

            var (connection, transfer, key) = request;
            var operation = new CF_OPERATION_INFO
            {
                StructSize = (uint)Marshal.SizeOf<CF_OPERATION_INFO>(),
                Type = CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_PLACEHOLDERS,
                ConnectionKey = connection,
                TransferKey = transfer,
                RequestKey = key,
            };
            var parameters = CF_OPERATION_PARAMETERS.Create(new CF_OPERATION_PARAMETERS.TRANSFERPLACEHOLDERS
            {
                Flags = CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAGS.CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAG_DISABLE_ON_DEMAND_POPULATION,
                CompletionStatus = status,
                PlaceholderTotalCount = infos.Length,
                PlaceholderArray = array,
                PlaceholderCount = (uint)infos.Length,
            });

            CfExecute(operation, ref parameters);
        }
        finally
        {
            for (var i = 0; i < infos.Length; i++) Marshal.DestroyStructure<CF_PLACEHOLDER_CREATE_INFO>(array + i * size);
            Marshal.FreeHGlobal(array);
            identities.ForEach(Marshal.FreeHGlobal);
        }
    }

    private static void Acknowledge((CF_CONNECTION_KEY, CF_TRANSFER_KEY, CF_REQUEST_KEY) request, CF_OPERATION_TYPE type, NTStatus status)
    {
        var (connection, transfer, key) = request;
        var operation = new CF_OPERATION_INFO
        {
            StructSize = (uint)Marshal.SizeOf<CF_OPERATION_INFO>(),
            Type = type,
            ConnectionKey = connection,
            TransferKey = transfer,
            RequestKey = key,
        };
        var parameters = type == CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_DELETE
            ? CF_OPERATION_PARAMETERS.Create(new CF_OPERATION_PARAMETERS.ACKDELETE { CompletionStatus = status })
            : CF_OPERATION_PARAMETERS.Create(new CF_OPERATION_PARAMETERS.ACKRENAME { CompletionStatus = status });

        CfExecute(operation, ref parameters);
    }

    /// A placeholder for a phone file or folder. Its identity is its path on the phone; free it after use.
    private static CF_PLACEHOLDER_CREATE_INFO Placeholder(PhoneFile file)
    {
        var identity = Encoding.Unicode.GetBytes(file.Path);
        var pointer = Marshal.AllocHGlobal(identity.Length);
        Marshal.Copy(identity, 0, pointer, identity.Length);

        var time = (file.Modified > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(file.Modified) : DateTimeOffset.UtcNow).UtcDateTime.ToFileTimeStruct();
        var flags = CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC;

        return new CF_PLACEHOLDER_CREATE_INFO
        {
            RelativeFileName = file.Name,
            FsMetadata = new CF_FS_METADATA
            {
                FileSize = file.IsFolder ? 0 : file.Size,
                BasicInfo = new Kernel32.FILE_BASIC_INFO
                {
                    CreationTime = time,
                    LastWriteTime = time,
                    LastAccessTime = time,
                    ChangeTime = time,
                    FileAttributes = file.IsFolder ? FileFlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY : FileFlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                },
            },
            FileIdentity = pointer,
            FileIdentityLength = (uint)identity.Length,
            Flags = flags,
        };
    }

    /// Adds placeholders for what is new on the phone and removes the untouched ones it no longer has.
    private void Merge(string folder, IReadOnlyList<PhoneFile> files)
    {
        var onPhone = files.Select(file => file.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var here = Directory.EnumerateFileSystemEntries(folder).ToList();
        var names = here.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var local in here.Where(path => !onPhone.Contains(Path.GetFileName(path))))
        {
            if (StateOf(local) is not CF_PLACEHOLDER_STATE state || !state.HasFlag(CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_IN_SYNC)) continue;

            DeleteLocally(local);
        }

        var fresh = files.Where(file => !names.Contains(file.Name)).ToList();
        if (fresh.Count == 0) return;

        var infos = fresh.Select(Placeholder).ToArray();
        try
        {
            CfCreatePlaceholders(folder, infos, (uint)infos.Length, CF_CREATE_FLAGS.CF_CREATE_FLAG_NONE, out _);
        }
        catch (Exception e) when (e is COMException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not add phone files: {e.Message}");
        }
        finally
        {
            foreach (var info in infos) Marshal.FreeHGlobal(info.FileIdentity);
        }
    }

    /// Deletes a placeholder because the phone no longer has it, without telling the phone to delete it.
    private void DeleteLocally(string local)
    {
        try
        {
            if (Directory.Exists(local))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(local, "*", SearchOption.AllDirectories)) ownDeletes[entry] = 0;
                ownDeletes[local] = 0;
                Directory.Delete(local, recursive: true);
            }
            else
            {
                ownDeletes[local] = 0;
                File.Delete(local);
            }

            opened.TryRemove(local, out _);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ownDeletes.TryRemove(local, out _);
            System.Diagnostics.Debug.WriteLine($"Could not remove {local}: {e.Message}");
        }
    }

    /// Deletes the placeholders that were never opened. Opened files are real now, so they stay.
    private static void RemovePlaceholders(string folder)
    {
        try
        {
            foreach (var file in Directory.Exists(folder) ? Directory.GetFiles(folder) : [])
            {
                if ((File.GetAttributes(file) & FileAttributes.Offline) != 0) File.Delete(file);
            }
            foreach (var child in Directory.Exists(folder) ? Directory.GetDirectories(folder) : [])
            {
                RemovePlaceholders(child);
                if (!Directory.EnumerateFileSystemEntries(child).Any()) Directory.Delete(child);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not clear the phone drive: {e.Message}");
        }
    }

    /// Marks an item as matching the phone, making a placeholder of a new one first.
    private void MarkInSync(string local, bool isPlaceholder)
    {
        var isFolder = Directory.Exists(local);
        using var handle = Kernel32.CreateFile(
            local,
            Kernel32.FileAccess.FILE_WRITE_ATTRIBUTES,
            FileShare.ReadWrite | FileShare.Delete,
            null,
            FileMode.Open,
            isFolder ? FileFlagsAndAttributes.FILE_FLAG_BACKUP_SEMANTICS : FileFlagsAndAttributes.FILE_ATTRIBUTE_NORMAL);
        if (handle.IsInvalid) return;

        if (isPlaceholder)
        {
            CfSetInSyncState(handle, CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_IN_SYNC, CF_SET_IN_SYNC_FLAGS.CF_SET_IN_SYNC_FLAG_NONE, IntPtr.Zero);
            return;
        }

        var identity = Encoding.Unicode.GetBytes(PhonePath(local));
        var pinned = GCHandle.Alloc(identity, GCHandleType.Pinned);
        try
        {
            CfConvertToPlaceholder(handle, pinned.AddrOfPinnedObject(), (uint)identity.Length, CF_CONVERT_FLAGS.CF_CONVERT_FLAG_MARK_IN_SYNC, out _, IntPtr.Zero);
        }
        finally
        {
            pinned.Free();
        }
    }

    private static CF_PLACEHOLDER_STATE? StateOf(string path)
    {
        using var search = Kernel32.FindFirstFile(path, out var data);
        if (search.IsInvalid) return null;

        return CfGetPlaceholderStateFromFindData(data);
    }

    /// Whether nothing is still writing the file.
    private static bool IsClosed(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private async Task<string?> Download(string path)
    {
        var target = Path.Combine(cache, Guid.NewGuid().ToString("N"));

        try
        {
            await phone.SendAsync(PhoneFiles.Action.Download, path, saveTo: target);
            ForgetLater(path, target);

            return target;
        }
        catch (PhoneFilesException)
        {
            downloads.TryRemove(path, out _);
            return null;
        }
    }

    /// Windows may ask for a file in several ranges, so the download is kept for a moment before it goes.
    private void ForgetLater(string path, string file) => _ = Task.Delay(TimeSpan.FromMinutes(2)).ContinueWith(delay =>
    {
        downloads.TryRemove(path, out _);
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
    }, TaskScheduler.Default);

    private static NTStatus StatusOf(Exception error) => error switch
    {
        PhoneFilesException { Failure: PhoneFiles.Failure.Denied } => Denied,
        PhoneFilesException { Failure: PhoneFiles.Failure.Missing } => NTStatus.STATUS_OBJECT_NAME_NOT_FOUND,
        PhoneFilesException { Failure: PhoneFiles.Failure.Exists } => NTStatus.STATUS_OBJECT_NAME_COLLISION,
        _ => CloudFetch.Unsuccessful,
    };

    private string LocalPath(in CF_CALLBACK_INFO info) => Path.GetFullPath(info.VolumeDosName + info.NormalizedPath);

    /// The phone path of a file in the drive, like "/DCIM/Camera".
    private string PhonePath(string local)
    {
        var relative = Path.GetRelativePath(root, local);

        return PhoneFiles.Clean(relative == "." ? "/" : relative.Replace('\\', '/')) ?? PhoneFiles.Root;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
}
