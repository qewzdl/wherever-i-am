using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class NetworkSceneLoader : MonoBehaviour
{
    [SerializeField] private ProjectContext projectContext;

    public string LobbySceneName => GetSceneName(ProjectSceneKind.Lobby);
    public string GameSceneName => GetSceneName(ProjectSceneKind.Game);

    public void Construct(ProjectContext context)
    {
        projectContext = context;
    }

    public bool LoadLobby()
    {
        return Load(ProjectSceneKind.Lobby);
    }

    public bool LoadGame()
    {
        return Load(ProjectSceneKind.Game);
    }

    public bool Load(ProjectSceneKind sceneKind)
    {
        if (!IsNetworkScene(sceneKind))
        {
            Debug.LogError($"{nameof(NetworkSceneLoader)} can load only Lobby/Game network scenes. Requested: {sceneKind}.", this);
            return false;
        }

        if (!TryGetSceneName(sceneKind, out string sceneName))
            return false;

        if (!CanLoadNetworkScene())
            return false;

        NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        return true;
    }

    private bool TryGetSceneName(ProjectSceneKind sceneKind, out string sceneName)
    {
        sceneName = string.Empty;

        if (projectContext == null)
        {
            Debug.LogError($"{nameof(NetworkSceneLoader)} is missing {nameof(ProjectContext)}.", this);
            return false;
        }

        if (!projectContext.TryGetScene(sceneKind, out ProjectSceneDefinition scene))
        {
            Debug.LogError($"Network scene is not configured for {sceneKind}.", this);
            return false;
        }

        if (string.IsNullOrWhiteSpace(scene.SceneName))
        {
            Debug.LogError($"Network scene '{sceneKind}' has no configured scene name.", this);
            return false;
        }

        sceneName = scene.SceneName;
        return true;
    }

    private string GetSceneName(ProjectSceneKind sceneKind)
    {
        return TryGetSceneName(sceneKind, out string sceneName)
            ? sceneName
            : string.Empty;
    }

    private bool CanLoadNetworkScene()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager.Singleton is null.", this);
            return false;
        }

        if (!NetworkManager.Singleton.IsListening)
        {
            Debug.LogError("NetworkManager is not listening.", this);
            return false;
        }

        if (NetworkManager.Singleton.SceneManager == null)
        {
            Debug.LogError("NetworkManager.SceneManager is null.", this);
            return false;
        }

        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("Only server can load network scenes.", this);
            return false;
        }

        return true;
    }

    private static bool IsNetworkScene(ProjectSceneKind sceneKind)
    {
        return sceneKind == ProjectSceneKind.Lobby ||
               sceneKind == ProjectSceneKind.Game;
    }
}