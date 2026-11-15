using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlyyLink.Core;
using FlyyLink.Plugins;
using FlyyLink.Shell;

namespace FlyyLink.Model;

/// Each paired phone's storage as a folder in File Explorer's navigation pane, at %USERPROFILE%\FlyyLink\<phone>.
/// A phone keeps its folder across launches, even after a rename.
internal sealed class PhoneDrives(FilesPlugin files, DeviceRegistry devices, string recordPath, string cache)
{
    private readonly Dictionary<string, Record> records = Load(recordPath);
    private readonly Dictionary<string, PhoneDrive> drives = [];
    private readonly object gate = new();

    private sealed record Record(string Name, string Root);

    /// Sends one phone's requests to it while it is connected and paired.
    private sealed class Storage(FilesPlugin files, DeviceRegistry devices, string phoneId) : IPhoneStorage
    {
        public Task<PhoneFiles.Answer> SendAsync(PhoneFiles.Action action, string path, string? to = null, string? upload = null, string? saveTo = null)
        {
            if (devices[phoneId] is not Device { IsPaired: true } device) throw new PhoneFilesException(null);

            return files.SendAsync(device, action, path, to, upload, saveTo);
        }
    }

    /// Connects a folder for each of these phones.
    public void Show(IEnumerable<(string Id, string Name)> phones)
    {
        lock (gate)
        {
            foreach (var (id, name) in phones)
            {
                if (drives.ContainsKey(id)) continue;

                if (!records.TryGetValue(id, out var record))
                {
                    record = new Record(name, FreeRoot(name));
                    records[id] = record;
                    Save();
                }

                var drive = new PhoneDrive(record.Root, cache, IdOf(id), new Storage(files, devices, id));
                if (!drive.Connect(record.Name)) continue;

                drives[id] = drive;
                ExplorerSidebar.Add(IdOf(id), record.Name, record.Root);
            }
        }
    }

    /// Removes the folders of phones that are no longer paired.
    public void Prune(IReadOnlyCollection<string> paired)
    {
        List<string> gone;
        lock (gate) gone = records.Keys.Where(id => !paired.Contains(id)).ToList();

        gone.ForEach(Forget);
    }

    /// Removes the phone's folder: what was never opened goes, and opened files stay as ordinary files.
    public void Forget(string phoneId)
    {
        lock (gate)
        {
            if (!records.Remove(phoneId, out var record)) return;

            if (!drives.Remove(phoneId, out var drive)) drive = new PhoneDrive(record.Root, cache, IdOf(phoneId), new Storage(files, devices, phoneId));
            drive.Remove();
            ExplorerSidebar.Remove(IdOf(phoneId));
            Save();
        }
    }

    /// Removes every phone's folder, for when the setting is turned off.
    public void ForgetAll()
    {
        List<string> all;
        lock (gate) all = records.Keys.ToList();

        all.ForEach(Forget);
    }

    public string? RootOf(string phoneId)
    {
        lock (gate) return drives.ContainsKey(phoneId) ? records[phoneId].Root : null;
    }

    /// Checks the folders people opened for changes made on the phone.
    public async Task RefreshAsync()
    {
        List<PhoneDrive> connected;
        lock (gate) connected = drives.Values.ToList();

        foreach (var drive in connected) await drive.RefreshAsync();
    }

    public void Stop()
    {
        lock (gate)
        {
            foreach (var drive in drives.Values) drive.Disconnect();
            drives.Clear();
        }
    }

    /// A folder named after the phone that no other phone uses.
    private string FreeRoot(string name)
    {
        var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "FlyyLink");
        var safe = Share.SafeName(name);
        var root = Path.Combine(parent, safe);

        for (var n = 2; records.Values.Any(r => string.Equals(r.Root, root, StringComparison.OrdinalIgnoreCase)); n++)
        {
            root = Path.Combine(parent, $"{safe} {n}");
        }

        return root;
    }

    /// The same id for a phone every time, for its sync root and its Explorer entry.
    private static Guid IdOf(string phoneId) => new(MD5.HashData(Encoding.UTF8.GetBytes($"flyylink-drive:{phoneId}")));

    private static Dictionary<string, Record> Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, Record>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(recordPath, JsonSerializer.Serialize(records));
        }
        catch (IOException)
        {
        }
    }
}
