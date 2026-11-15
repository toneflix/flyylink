using System.Text;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace FlyyLink.Plugins;

/// Reads the toasts Windows is showing from its own notification database. Unpackaged apps can't use
/// `UserNotificationListener`, but the database sits in the user's profile and any of their apps can read it.
internal sealed class NotificationDatabase
{
    public sealed record Toast(uint Id, string AppId, IReadOnlyList<string> Texts, DateTimeOffset Arrived);

    private const string Query = """
        SELECT n.Id, h.PrimaryId, n.Payload, n.ArrivalTime
        FROM Notification n JOIN NotificationHandler h ON h.RecordId = n.HandlerId
        WHERE n.Type = 'toast'
        ORDER BY n.ArrivalTime DESC LIMIT $limit
        """;

    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Notifications", "wpndatabase.db");

    /// The newest toasts, newest first, or null when the database can't be read.
    public IReadOnlyList<Toast>? Read(int limit = 100)
    {
        if (!File.Exists(Path)) return null;

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = Query;
            command.Parameters.AddWithValue("$limit", limit);

            var toasts = new List<Toast>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (ReadToast(reader) is Toast toast) toasts.Add(toast);
            }

            return toasts;
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Reading the notification database failed: {e.Message}");
            return null;
        }
    }

    private static Toast? ReadToast(SqliteDataReader reader)
    {
        if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2)) return null;

        var payload = reader.GetValue(2) switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            _ => null,
        };
        if (payload is null) return null;

        var arrived = reader.IsDBNull(3) ? DateTimeOffset.Now : DateTimeOffset.FromFileTime(reader.GetInt64(3));

        return new Toast((uint)reader.GetInt64(0), reader.GetString(1), Texts(payload), arrived);
    }

    /// The text lines of a toast's XML, without the small attribution line.
    public static IReadOnlyList<string> Texts(string payload)
    {
        try
        {
            var binding = XDocument.Parse(payload).Descendants("binding").FirstOrDefault();

            return binding?.Elements("text")
                .Where(text => (string?)text.Attribute("placement") != "attribution")
                .Select(text => text.Value.Trim())
                .Where(text => text.Length > 0)
                .ToList() ?? [];
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }
    }
}
