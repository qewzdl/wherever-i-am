using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class NetworkSceneLoader : MonoBehaviour
{
    private IProjectSceneRegistry sceneRegistry;
    private NetworkManager networkManager;

    public string LobbySceneName => GetSceneName(ProjectSceneKind.Lobby);
    public string GameSceneName => GetSceneName(ProjectSceneKind.Game);

    public void Construct(
        IProjectSceneRegistry projectSceneRegistry,
        NetworkManager runtimeNetworkManager)
    {
        sceneRegistry = projectSceneRegistry;
        networkManager = runtimeNetworkManager;
    }

    public void DisposeComposition()
    {
        sceneRegistry = null;
        networkManager = null;
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

        SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(
            sceneName,
            LoadSceneMode.Single);

        if (status == SceneEventProgressStatus.Started)
            return true;

        Debug.LogError(
            $"Failed to start network scene load for '{sceneKind}'. Status: {status}.",
            this);

        return false;
    }

    private bool TryGetSceneName(ProjectSceneKind sceneKind, out string sceneName)
    {
        sceneName = string.Empty;

        if (sceneRegistry == null)
        {
            Debug.LogError($"{nameof(NetworkSceneLoader)} is missing its scene registry.", this);
            return false;
        }

        if (!sceneRegistry.TryGetScene(sceneKind, out ProjectSceneDefinition scene))
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
        if (networkManager == null)
        {
            Debug.LogError("NetworkManager dependency is missing.", this);
            return false;
        }

        if (!networkManager.IsListening)
        {
            Debug.LogError("NetworkManager is not listening.", this);
            return false;
        }

        if (networkManager.SceneManager == null)
        {
            Debug.LogError("NetworkManager.SceneManager is null.", this);
            return false;
        }

        if (!networkManager.IsServer)
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
