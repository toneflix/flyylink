using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlyyLink.Core;

/// What the licence server signed. Times are Unix seconds.
public sealed record LicenceClaims(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("machine")] string Machine,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("issued")] long Issued,
    [property: JsonPropertyName("renew")] long Renew,
    [property: JsonPropertyName("expires")] long Expires)
{
    public const string Licence = "licence";
}

/// Checks tokens from the licence server: `base64url(json).base64url(r||s)`, signed with ECDSA P-256.
public static class LicenceToken
{
    /// The claims, or null when the token is malformed or not signed by `publicKey` (base64 SubjectPublicKeyInfo).
    public static LicenceClaims? Verify(string? token, string publicKey)
    {
        var parts = token?.Split('.');
        if (parts is not { Length: 2 }) return null;

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (!key.VerifyData(Encoding.ASCII.GetBytes(parts[0]), FromBase64Url(parts[1]), HashAlgorithmName.SHA256)) return null;

            return JsonSerializer.Deserialize<LicenceClaims>(FromBase64Url(parts[0]));
        }
        catch (Exception e) when (e is FormatException or CryptographicException or JsonException)
        {
            return null;
        }
    }

    private static byte[] FromBase64Url(string text)
    {
        var plain = text.Replace('-', '+').Replace('_', '/');

        return Convert.FromBase64String(plain.PadRight(plain.Length + (4 - plain.Length % 4) % 4, '='));
    }
}

/// Why the computer apps stopped connecting.
public enum LicenceLock
{
    /// Not signed in yet. Trials start with a plan bought on the website.
    SignedOut,

    /// The token ran out without a renewal; going online once fixes it.
    Offline,

    /// The computer was removed from the account on the website.
    Removed,

    /// The plan ended, the trial was cancelled or didn't convert, or the purchase was refunded.
    Revoked,
}

public abstract record LicenceStatus
{
    /// A build without a licence key, such as a local one: nothing is checked.
    public sealed record Unlimited : LicenceStatus;

    public sealed record Licensed(string Email, bool ShouldRenew) : LicenceStatus;

    public sealed record Locked(LicenceLock Reason) : LicenceStatus;

    public bool IsLocked => this is Locked;
}

/// What a computer app saved from the licence server.
public sealed record LicenceRecord(string? LicenceToken, LicenceLock? ServerLock);

/// Decides, offline, whether this computer may use FlyyLink. Mirrors LicenceRules.swift.
public static class LicenceRules
{
    public static LicenceStatus Evaluate(LicenceRecord record, string machine, string? publicKey, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(publicKey)) return new LicenceStatus.Unlimited();

        var seconds = now.ToUnixTimeSeconds();
        var licence = LicenceToken.Verify(record.LicenceToken, publicKey) is { } claims && claims.Kind == LicenceClaims.Licence && claims.Machine == machine ? claims : null;
        if (licence is not null && seconds < licence.Expires) return new LicenceStatus.Licensed(licence.Email ?? "", seconds >= licence.Renew);
        if (record.ServerLock is LicenceLock locked) return new LicenceStatus.Locked(locked);

        return new LicenceStatus.Locked(licence is null ? LicenceLock.SignedOut : LicenceLock.Offline);
    }
}

/// The computer's hardware ID, scrambled so the licence server never sees the real one.
public static class MachineId
{
    public static string From(string hardwareId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"flyylink:{hardwareId.Trim().ToLowerInvariant()}"));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
