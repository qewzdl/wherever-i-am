using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkSceneLoader : MonoBehaviour
{
    [SerializeField] private string mainMenuSceneName = "Main Menu";
    [SerializeField] private string lobbySceneName = "Lobby";
    [SerializeField] private string gameSceneName = "Game";

    public string MainMenuSceneName => mainMenuSceneName;
    public string LobbySceneName => lobbySceneName;
    public string GameSceneName => gameSceneName;

    public void LoadMainMenu()
    {
        SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
    }

    public bool LoadLobby()
    {
        if (!CanLoadNetworkScene()) return false;

        NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
        return true;
    }

    public bool LoadGame()
    {
        if (!CanLoadNetworkScene()) return false;

        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        return true;
    }

    private bool CanLoadNetworkScene()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager.Singleton is null.");
            return false;
        }

        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("Only server can load network scenes.");
            return false;
        }

        return true;
    }
}