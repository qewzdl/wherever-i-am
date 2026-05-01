using System;

public interface ILobbyService
{
    event Action LobbyChanged;

    int PlayerCount { get; }
    bool IsHost { get; }
    bool CanStartGame { get; }

    LobbyPlayerData GetPlayer(int index);

    void SetReady(bool isReady);
    void SetCharacter(int characterId);
    void StartGame();
    void LeaveLobby();
}