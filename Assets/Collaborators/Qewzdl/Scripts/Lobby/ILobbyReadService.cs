using System;

public interface ILobbyReadService
{
    event Action LobbyChanged;
    event Action PlayersChanged;
    event Action SettingsChanged;
    event Action OwnerChanged;
    event Action StartAvailabilityChanged;

    int PlayerCount { get; }
    bool IsLocalPlayerRoomOwner { get; }
    bool CanStartGame { get; }
    LobbySettingsData Settings { get; }

    LobbyPlayerData GetPlayer(int index);
    bool TryGetLocalPlayer(out LobbyPlayerData player);
}