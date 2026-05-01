public interface ILobbyCommandService
{
    void SetReady(bool isReady);
    void SetCharacter(int characterId);
    void SetGameMode(int gameModeId);
    void SetMap(int mapId);
    void StartGame();
    void LeaveLobby();
}