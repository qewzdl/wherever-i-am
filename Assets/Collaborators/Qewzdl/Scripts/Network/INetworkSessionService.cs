using System.Threading.Tasks;

public interface INetworkSessionService
{
    Task HostLanAsync();
    Task JoinLanAsync(string ip);

    void StartGame(int mapId);
    void ShutdownToMainMenu();

    /// <summary>
    /// Stops NGO, closes Scene/Player/Session scopes, and completes only after
    /// MainMenu has been activated and committed.
    /// </summary>
    Task<NetworkShutdownResult> ShutdownToMainMenuAsync();
}
