using FlyyLink.Core;

namespace FlyyLink.Core.Tests;

/// Mirrors LicenceTests.swift. The tokens were signed by the licence server's code with a test key.
public class LicenceTests
{
    private const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEncVafPUhUOuV2pFFsl3AV3asVMwIby4RkyUx9aA4H6rua20jBexnHB4GmtFK2KBcQE01LOdbR00l6ttq4ep/ig==";
    private const string LicenceFixture = "eyJ2IjoxLCJraW5kIjoibGljZW5jZSIsIm1hY2hpbmUiOiJtMSIsImVtYWlsIjoiYnV5ZXJAZXhhbXBsZS5jb20iLCJpc3N1ZWQiOjEwMDAsInJlbmV3IjoyMDAwLCJleHBpcmVzIjozMDAwfQ.e6G63Vg1icZgrAkvoXYq0QMxJpBU4QGP0XMlmELrk-ITRSOjgGeIOjdzvGKebc33nWoBtOr0rJsZYpHSL0C14w";
    private const string TrialFixture = "eyJ2IjoxLCJraW5kIjoidHJpYWwiLCJtYWNoaW5lIjoibTEiLCJpc3N1ZWQiOjEwMDAsInJlbmV3Ijo1MDAwLCJleHBpcmVzIjo1MDAwfQ.p_7sfjcfFzFUMyBWR6VaSjy8ux7QFhPTc0EtkjZewOA8-iAMa-Kanv_SSsC8M8um3Nf86iZBUyq41CdmO1zikQ";

    [Fact]
    public void ReadsASignedToken()
    {
        var claims = LicenceToken.Verify(LicenceFixture, PublicKey);

        Assert.Equal(new LicenceClaims("licence", "m1", "buyer@example.com", 1000, 2000, 3000), claims);
    }

    [Fact]
    public void RefusesAChangedToken()
    {
        var trialBody = TrialFixture.Split('.')[0];
        var licenceSignature = LicenceFixture.Split('.')[1];

        Assert.Null(LicenceToken.Verify($"{trialBody}.{licenceSignature}", PublicKey));
        Assert.Null(LicenceToken.Verify("not a token", PublicKey));
    }

    [Fact]
    public void LicensedUntilItExpires()
    {
        var record = new LicenceRecord(LicenceFixture, null);

        Assert.Equal(new LicenceStatus.Licensed("buyer@example.com", false), Evaluate(record, 1500));
        Assert.Equal(new LicenceStatus.Licensed("buyer@example.com", true), Evaluate(record, 2500));
        Assert.Equal(new LicenceStatus.Locked(LicenceLock.Offline), Evaluate(record, 3000));
    }

    [Fact]
    public void TokenForAnotherComputerDoesNotCount()
    {
        var record = new LicenceRecord(LicenceFixture, null);

        Assert.Equal(new LicenceStatus.Locked(LicenceLock.SignedOut), LicenceRules.Evaluate(record, "m2", PublicKey, DateTimeOffset.FromUnixTimeSeconds(1500)));
    }

    [Fact]
    public void OldTrialTokenDoesNotCount()
    {
        Assert.Equal(new LicenceStatus.Locked(LicenceLock.SignedOut), Evaluate(new LicenceRecord(TrialFixture, null), 1500));
    }

    [Fact]
    public void LockedUntilSignedIn()
    {
        Assert.Equal(new LicenceStatus.Locked(LicenceLock.SignedOut), Evaluate(new LicenceRecord(null, null), 1500));
    }

    [Fact]
    public void ServerLockWins()
    {
        Assert.Equal(new LicenceStatus.Locked(LicenceLock.Revoked), Evaluate(new LicenceRecord(null, LicenceLock.Revoked), 1500));
    }

    [Fact]
    public void NoKeyMeansUnlimited()
    {
        Assert.Equal(new LicenceStatus.Unlimited(), LicenceRules.Evaluate(new LicenceRecord(null, null), "m1", null, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void MachineIdIsAStableHash()
    {
        Assert.Equal(MachineId.From(" ABC "), MachineId.From("abc"));
        Assert.Equal("d2411e14a3705cc79bff7bf923a5655a0a23014b45072915c3e38e9046f4d34a", MachineId.From("abc"));
    }

    private static LicenceStatus Evaluate(LicenceRecord record, long seconds) =>
        LicenceRules.Evaluate(record, "m1", PublicKey, DateTimeOffset.FromUnixTimeSeconds(seconds));
}
