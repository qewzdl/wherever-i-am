using UnityEngine;

// A host saying it is here, for as long as it is worth joining.
//
// It speaks only while the door is open. That is not a convenience - it is the
// same switch the lobby already has, and it means the list on everybody else's
// screen is the list of rooms that would actually let them in. A shut lobby is
// silent, and a lobby that shuts goes quiet and ages off the browsers within a
// few seconds without anybody being told to remove it.
//
// Made in code rather than placed in the scene: it belongs to the lobby's
// lifetime, and LobbySceneFeature is where that begins and ends.
[DisallowMultipleComponent]
public sealed class LanLobbyBeacon : MonoBehaviour
{
    // Often enough that a room appears while somebody is still looking at the
    // list, rarely enough to be nothing on a network. Four of these have to be
    // missed before a browser gives up on a lobby.
    private const float BeaconSeconds = 1f;

    private ILobbyReadService readService;
    private LanLobbySocket socket;
    private float nextBeaconAt;

    // Started by whoever owns the lobby, and stopped with it. Null services
    // mean there is nothing to announce, which is not an error - a lobby that
    // cannot be read is a lobby nobody should be invited to.
    public static LanLobbyBeacon Announce(ILobbyReadService readService)
    {
        if (readService == null)
            return null;

        GameObject host = new GameObject(nameof(LanLobbyBeacon));
        Object.DontDestroyOnLoad(host);

        LanLobbyBeacon beacon = host.AddComponent<LanLobbyBeacon>();
        beacon.readService = readService;

        return beacon;
    }

    public void Stop()
    {
        if (this != null)
            Destroy(gameObject);
    }

    private void OnEnable()
    {
        socket = new LanLobbySocket();
    }

    private void OnDisable()
    {
        socket?.Dispose();
        socket = null;
    }

    private void Update()
    {
        if (socket == null || !socket.IsOpen)
            return;

        AnswerProbes();

        if (!ShouldAnnounce())
            return;

        if (Time.unscaledTime < nextBeaconAt)
            return;

        nextBeaconAt = Time.unscaledTime + BeaconSeconds;
        socket.Broadcast(BuildAdvert().Serialize());
    }

    // Answered even while shut, with nothing. A browser that has just been
    // refreshed learns what is out there from the hosts that reply; a host that
    // is not taking anybody does not reply, and so is not in the list.
    private void AnswerProbes()
    {
        while (socket.TryReceive(out string message, out System.Net.IPEndPoint from))
        {
            if (!LanLobbyAdvert.IsProbe(message) || !ShouldAnnounce())
                continue;

            socket.Send(BuildAdvert().Serialize(), from);
        }
    }

    // Open, and still a lobby. A room that has started its match is not one
    // anybody can walk into, whatever its door says.
    private bool ShouldAnnounce()
    {
        return readService != null &&
               readService.Settings.IsPublic &&
               readService.Phase == LobbyPhase.Open;
    }

    private LanLobbyAdvert BuildAdvert()
    {
        // Read at the moment of speaking rather than remembered from before
        // the session existed: a beacon made while the transport was still
        // being set up would otherwise advertise the port it had then.
        return new LanLobbyAdvert(
            LanLobbyNetwork.ProtocolVersion,
            LanLobbyNetwork.GamePort,
            readService.PlayerCount,
            readService.Settings.MaxPlayers,
            ResolveName());
    }

    // The host's own name, which is what everybody else in the room is looking
    // at anyway. Falling back to something rather than nothing: a row in a list
    // with no name at all reads as a fault in the list.
    private string ResolveName()
    {
        for (int i = 0; i < readService.PlayerCount; i++)
        {
            LobbyPlayerData player = readService.GetPlayer(i);

            if (player.ClientId != readService.RoomOwnerClientId)
                continue;

            string name = player.PlayerName.ToString();

            if (!string.IsNullOrWhiteSpace(name))
                return name;

            break;
        }

        return "Lobby";
    }
}
