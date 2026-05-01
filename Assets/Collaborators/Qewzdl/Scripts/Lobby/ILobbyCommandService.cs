public interface ILobbyCommandService
{
    void SetReady(bool isReady);
    void SetCharacter(int characterId);
    void StartGame();
    void LeaveLobby();
}