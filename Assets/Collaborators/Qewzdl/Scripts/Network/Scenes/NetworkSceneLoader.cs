using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkSceneLoader : MonoBehaviour
{
    private const string FallbackMainMenuSceneName = "Main Menu";
    private const string FallbackLobbySceneName = "Lobby";
    private const string FallbackGameSceneName = "Game";

    [SerializeField] private ProjectContext projectContext;

    public string MainMenuSceneName => GetSceneName(ProjectSceneKind.MainMenu, FallbackMainMenuSceneName);
    public string LobbySceneName => GetSceneName(ProjectSceneKind.Lobby, FallbackLobbySceneName);
    public string GameSceneName => GetSceneName(ProjectSceneKind.Game, FallbackGameSceneName);

    private void Awake()
    {
        ResolveProjectContext();
    }

    public void LoadMainMenu()
    {
        SceneManager.LoadScene(MainMenuSceneName, LoadSceneMode.Single);
    }

    public bool LoadLobby()
    {
        if (!CanLoadNetworkScene()) return false;

        NetworkManager.Singleton.SceneManager.LoadScene(LobbySceneName, LoadSceneMode.Single);
        return true;
    }

    public bool LoadGame()
    {
        if (!CanLoadNetworkScene()) return false;

        NetworkManager.Singleton.SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);
        return true;
    }

    private void ResolveProjectContext()
    {
        if (projectContext == null)
            projectContext = GetComponent<ProjectContext>();

        if (projectContext == null)
            projectContext = ProjectContext.Instance;
    }

    private string GetSceneName(ProjectSceneKind sceneKind, string fallbackSceneName)
    {
        ResolveProjectContext();

        if (projectContext == null)
            return fallbackSceneName;

        string sceneName = projectContext.GetSceneName(sceneKind);

        return string.IsNullOrWhiteSpace(sceneName)
            ? fallbackSceneName
            : sceneName;
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
