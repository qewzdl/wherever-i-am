using NUnit.Framework;

// The one piece of discovery that is a decision rather than a socket: what a
// host says about itself, and what a browser is willing to believe.
//
// It matters more than its size. This is a port on the local network that will
// hear whatever else is shouting there, and every one of those datagrams is
// read by this parser. Anything it accepts becomes a row somebody can click.
public sealed class LanLobbyAdvertTests
{
    private const int Protocol = 2;

    [Test]
    public void AnAdvertSurvivesTheRoundTrip()
    {
        LanLobbyAdvert sent = new(Protocol, 7777, 2, 4, "Alex");

        Assert.That(
            LanLobbyAdvert.TryParse(sent.Serialize(), Protocol, out LanLobbyAdvert read),
            Is.True);

        Assert.That(read.port, Is.EqualTo(7777));
        Assert.That(read.players, Is.EqualTo(2));
        Assert.That(read.maxPlayers, Is.EqualTo(4));
        Assert.That(read.name, Is.EqualTo("Alex"));
    }

    // A name is typed by a player, so it will one day contain the character
    // that breaks whatever was used to join the fields together.
    [Test]
    public void ANameCarriesWhateverAPlayerTypedIntoIt()
    {
        LanLobbyAdvert sent = new(Protocol, 7777, 1, 4, "a|b\"c{d}e");

        Assert.That(
            LanLobbyAdvert.TryParse(sent.Serialize(), Protocol, out LanLobbyAdvert read),
            Is.True);

        Assert.That(read.name, Is.EqualTo("a|b\"c{d}e"));
    }

    // Everything else on the port. None of it may become a row.
    [Test]
    public void NothingElseOnThePortIsMistakenForALobby()
    {
        foreach (string noise in new[]
                 {
                     null,
                     "",
                     "hello",
                     "{\"port\":7777}",
                     LanLobbyAdvert.ProbeMessage
                 })
        {
            Assert.That(
                LanLobbyAdvert.TryParse(noise, Protocol, out _),
                Is.False,
                $"'{noise}' is not a lobby.");
        }
    }

    // A build that speaks a different protocol is a lobby this one cannot join,
    // so it is not offered. Better an empty list than a row that refuses.
    [Test]
    public void ALobbyFromAnotherBuildIsNotOffered()
    {
        LanLobbyAdvert sent = new(Protocol + 1, 7777, 1, 4, "Alex");

        Assert.That(LanLobbyAdvert.TryParse(sent.Serialize(), Protocol, out _), Is.False);
    }

    // Nowhere to connect to is the same as nothing at all.
    [Test]
    public void AnAdvertWithNoPortIsNotALobby()
    {
        LanLobbyAdvert sent = new(Protocol, 0, 1, 4, "Alex");

        Assert.That(LanLobbyAdvert.TryParse(sent.Serialize(), Protocol, out _), Is.False);
    }

    [Test]
    public void AProbeIsRecognisedAndNothingElseIs()
    {
        Assert.That(LanLobbyAdvert.IsProbe(LanLobbyAdvert.ProbeMessage), Is.True);
        Assert.That(LanLobbyAdvert.IsProbe("wia-probe"), Is.False, "Case is not close enough.");
        Assert.That(LanLobbyAdvert.IsProbe(new LanLobbyAdvert(Protocol, 7777, 1, 4, "A").Serialize()), Is.False);
    }

    [Test]
    public void AFullRoomSaysSo()
    {
        Assert.That(new LanLobbyAdvert(Protocol, 7777, 4, 4, "A").IsFull, Is.True);
        Assert.That(new LanLobbyAdvert(Protocol, 7777, 3, 4, "A").IsFull, Is.False);

        // A room that has not said how big it is is not a room that is full.
        Assert.That(new LanLobbyAdvert(Protocol, 7777, 3, 0, "A").IsFull, Is.False);
    }
}
