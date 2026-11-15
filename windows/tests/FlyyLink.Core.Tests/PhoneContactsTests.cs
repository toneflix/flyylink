using FlyyLink.Core;

namespace FlyyLink.Core.Tests;

/// Mirrors PhoneContactsTests.swift and PhoneContactsTest.kt.
public class PhoneContactsTests
{
    private static NetworkPacket Wire(NetworkPacket packet) => NetworkPacket.Parse(packet.Serialize());

    private static readonly PhoneContact Ada = new("Ada Obi", ["+234 803 555 0101", "0803-555-0102"]);
    private static readonly PhoneContact Bayo = new("Bayo", ["08125550199"]);
    private static readonly PhoneContact Nada = new("Nada", ["+44 20 7946 0000"]);

    [Fact]
    public void ContactsRoundTrip()
    {
        PhoneContact[] contacts = [Ada, Bayo];

        Assert.Equal(contacts, PhoneContacts.ContactsOf(Wire(PhoneContacts.Packet(contacts))));
        Assert.True(PhoneContacts.IsRequest(Wire(PhoneContacts.RequestPacket())));
    }

    [Fact]
    public void DropsContactsWithoutNameOrNumber()
    {
        var packet = NetworkPacket.Parse("""{"id":1,"type":"flyylink.contacts","body":{"contacts":[{"name":"","numbers":["1"]},{"name":"Ann","numbers":[]},{"name":"Bo","numbers":[" 2 "]}]}}""");

        Assert.Equal([new PhoneContact("Bo", ["2"])], PhoneContacts.ContactsOf(packet));
    }

    [Fact]
    public void MatchesNamesAndNumbers()
    {
        Assert.True(Ada.Matches("obi"));
        Assert.True(Ada.Matches("555 0102"));
        Assert.True(Ada.Matches(""));
        Assert.False(Ada.Matches("Bayo"));
        Assert.False(Ada.Matches("+"));
    }

    [Fact]
    public void SearchPutsNameStartsFirst()
    {
        Assert.Equal([Ada, Nada], PhoneContacts.Search([Nada, Bayo, Ada], "ada"));
        Assert.Equal([Bayo], PhoneContacts.Search([Nada, Bayo, Ada], "0812"));
    }

    [Fact]
    public void PhonesAndComputersShareContactsButTvsDont()
    {
        Assert.Contains(PacketType.Contacts, Capabilities.Mvp);
        Assert.DoesNotContain(PacketType.ContactsRequest, Capabilities.Tv);
    }
}
