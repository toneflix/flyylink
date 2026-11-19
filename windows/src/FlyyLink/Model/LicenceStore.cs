using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlyyLink.Core;

namespace FlyyLink.Model;

/// The licence tokens and refresh secret, in licence.json. The secret is encrypted for this Windows user.
internal sealed class LicenceStore(string path)
{
    private sealed class Values
    {
        public string? LicenceToken { get; set; }
        public string? Secret { get; set; }
        public LicenceLock? ServerLock { get; set; }
    }

    private readonly Values values = Load(path);

    public LicenceRecord Record => new(values.LicenceToken, values.ServerLock);

    public string? Secret
    {
        get
        {
            if (values.Secret is null) return null;

            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(values.Secret), null, DataProtectionScope.CurrentUser));
            }
            catch (Exception e) when (e is CryptographicException or FormatException)
            {
                return null;
            }
        }
    }

    public void Activated(string token, string secret)
    {
        values.LicenceToken = token;
        values.Secret = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser));
        values.ServerLock = null;
        Save();
    }

    public void Renewed(string token)
    {
        values.LicenceToken = token;
        Save();
    }

    /// The server turned this computer away; forget its licence.
    public void Locked(LicenceLock reason)
    {
        values.LicenceToken = null;
        values.Secret = null;
        values.ServerLock = reason;
        Save();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(values));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static Values Load(string path)
    {
        Values? values = null;

        try
        {
            values = JsonSerializer.Deserialize<Values>(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return values ?? new Values();
    }
}
