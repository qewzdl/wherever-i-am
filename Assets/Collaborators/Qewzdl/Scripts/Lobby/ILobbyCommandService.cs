public interface ILobbyCommandService
{
    void SetReady(bool isReady);
    void SetGameMode(int gameModeId);
    void SetMap(int mapId);
    void SetDifficulty(int difficultyId);
    void SetLobbyPublic(bool isPublic);
    void KickPlayer(ulong clientId);
    void StartGame();
    void LeaveLobby();
}
