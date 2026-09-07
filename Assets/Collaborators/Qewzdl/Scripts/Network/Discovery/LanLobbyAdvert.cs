using System;
using UnityEngine;

// What a host says about itself on the local network, and what a browser reads.
//
// There is no server anywhere in this game - a lobby is somebody's machine with
// a port open - so there is nowhere to ask what lobbies exist. A host that has
// opened its door says so instead, out loud, to the whole network, and anybody
// listening hears it within a beacon. That is what makes the list live without
// anything to be live against: nobody polls, the room announces itself.
//
// The address is deliberately not in here. It is the one field that cannot be
// wrong, because the receiver reads it off the datagram that arrived - which
// spares this the question LanAddressProvider exists to answer, and spares a
// host advertising an address that only reaches itself.
[Serializable]
public struct LanLobbyAdvert
{
    // Bumped when the shape below changes in a way an older build would read
    // wrongly. A build that does not recognise the number ignores the advert
    // rather than showing a lobby it cannot join.
    public const int CurrentFormat = 1;

    private const string Prefix = "WIA-LOBBY ";

    // What a browser sends when somebody presses Refresh. A host answers it at
    // once rather than making the browser wait for the next beacon, which is
    // the difference between a button that works and a button that seems to.
    public const string ProbeMessage = "WIA-PROBE";

    public int format;
    public int protocol;
    public ushort port;
    public int players;
    public int maxPlayers;
    public string name;

    public LanLobbyAdvert(
        int protocol,
        ushort port,
        int players,
        int maxPlayers,
        string name)
    {
        format = CurrentFormat;
        this.protocol = protocol;
        this.port = port;
        this.players = players;
        this.maxPlayers = maxPlayers;
        this.name = name ?? string.Empty;
    }

    public bool IsFull => maxPlayers > 0 && players >= maxPlayers;

    // Prefixed rather than bare JSON. This port will hear whatever else is
    // shouting on the network, and a parser that tries to read all of it is a
    // parser that spends its time failing on somebody else's protocol.
    public string Serialize()
    {
        return Prefix + JsonUtility.ToJson(this);
    }

    public static bool TryParse(string message, int expectedProtocol, out LanLobbyAdvert advert)
    {
        advert = default;

        if (string.IsNullOrEmpty(message) || !message.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        try
        {
            advert = JsonUtility.FromJson<LanLobbyAdvert>(message.Substring(Prefix.Length));
        }
        catch (ArgumentException)
        {
            return false;
        }

        // A shape this build does not know, or a game it cannot join. Both are
        // somebody else's lobby as far as this list is concerned.
        if (advert.format != CurrentFormat || advert.protocol != expectedProtocol)
            return false;

        return advert.port != 0;
    }

    public static bool IsProbe(string message)
    {
        return string.Equals(message, ProbeMessage, StringComparison.Ordinal);
    }
}
