using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkSceneLoader : MonoBehaviour
{
    [SerializeField] private string mainMenuSceneName = "Main Menu";
    [SerializeField] private string lobbySceneName = "Lobby";
    [SerializeField] private string gameSceneName = "Game";

    public void LoadMainMenu()
    {
        SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
    }

    public void LoadLobby()
    {
        if (!CanLoadNetworkScene()) return;

        NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
    }

    public void LoadGame()                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                
    {
        if (!CanLoadNetworkScene()) return;

        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
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