using System.Threading.Tasks;

public interface INetworkSessionService
{
    Task HostLanAsync();
    Task JoinLanAsync(string ip);

    void StartGame(int mapId);

    // Same start, with the difficulty the host picked. The overload without it
    // keeps whatever difficulty the catalog defaults to.
    void StartGame(int mapId, int difficultyId);
    void ShutdownToMainMenu();

    /// <summary>
    /// Stops NGO, closes Scene/Player/Session scopes, and completes only after
    /// MainMenu has been activated and committed.
    /// </summary>
    Task<NetworkShutdownResult> ShutdownToMainMenuAsync();
}
