public interface ILobbyCommandService
{
    void SetReady(bool isReady);
    void SetGameMode(int gameModeId);
    void SetMap(int mapId);
    void SetDifficulty(int difficultyId);
    void StartGame();
    void LeaveLobby();
}
