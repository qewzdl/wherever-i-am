using System;

public interface ILobbyReadService
{
    event Action LobbyChanged;
    event Action PlayersChanged;
    event Action SettingsChanged;
    event Action OwnerChanged;
    event Action StartAvailabilityChanged;
    event Action PhaseChanged;

    int PlayerCount { get; }
    ulong RoomOwnerClientId { get; }
    bool IsLocalPlayerRoomOwner { get; }
    bool CanStartGame { get; }
    LobbyPhase Phase { get; }
    LobbySettingsData Settings { get; }

    LobbyPlayerData GetPlayer(int index);
    bool TryGetLocalPlayer(out LobbyPlayerData player);
}
