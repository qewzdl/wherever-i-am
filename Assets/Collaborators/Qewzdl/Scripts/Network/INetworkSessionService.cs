using System.Threading.Tasks;

public interface INetworkSessionService
{
    Task HostLanAsync();
    Task JoinLanAsync(string ip);

    void StartGame();
    void ShutdownToMainMenu();

    bool HasLastError { get; }
    string LastErrorMessage { get; }

    void ClearLastError();
}