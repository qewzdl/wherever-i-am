using System.Threading.Tasks;

public interface INetworkSessionService
{
    // The map is chosen before the lobby exists and stays for as long as it
    // does; null keeps the catalog's default.
    Task HostLanAsync(int? mapId = null);

    // A host nobody else can join - see SessionMode.
    Task HostSingleplayerAsync(int? mapId = null);
    Task JoinLanAsync(string ip);

    void StartGame(int mapId);

    // Same start, with the difficulty the host picked. The overload without it
    // keeps whatever difficulty the catalog defaults to.
    void StartGame(int mapId, int difficultyId);

    /// <summary>
    /// Server only. Takes a finished match back to the lobby with the session
    /// still up, so another round does not need hosting and rejoining.
    /// </summary>
    void ReturnToLobby();

    void ShutdownToMainMenu();

    /// <summary>
    /// Stops NGO, closes Scene/Player/Session scopes, and completes only after
    /// MainMenu has been activated and committed.
    /// </summary>
    Task<NetworkShutdownResult> ShutdownToMainMenuAsync();
}
