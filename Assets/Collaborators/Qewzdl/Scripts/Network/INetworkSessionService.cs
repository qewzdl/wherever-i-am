using System.Threading.Tasks;

public interface INetworkSessionService
{
    Task HostLanAsync();
    Task JoinLanAsync(string ip);

    void StartGame(int mapId);
    void ShutdownToMainMenu();
    Task ShutdownToMainMenuAsync();
}
