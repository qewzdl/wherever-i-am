using System;

public interface ILobbyReadService
{
    event Action LobbyChanged;

    int PlayerCount { get; }
    bool IsLocalPlayerRoomOwner { get; }
    bool CanStartGame { get; }

    LobbyPlayerData GetPlayer(int index);
}