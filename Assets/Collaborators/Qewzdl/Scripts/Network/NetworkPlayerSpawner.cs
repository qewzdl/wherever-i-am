using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class NetworkPlayerSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private ProjectSceneNavigator sceneNavigator;

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;

    private bool serverStartedCallbackSubscribed;
    private bool clientConnectedCallbackSubscribed;
    private bool networkSceneCallbackSubscribed;

    private NetworkManager serverStartedCallbackNetworkManager;
    private NetworkManager clientConnectedCallbackNetworkManager;
    private NetworkSceneManager networkSceneManager;

    private void Awake()
    {
        HasRequiredReferences();
    }

    private void OnEnable()
    {
        if (!HasRequiredReferences())
            return;

        SubscribeToServerStartedCallback();
        RefreshRuntimeSubscriptions();
    }

    private void OnDisable()
    {
        UnsubscribeFromRuntimeSubscriptions();
        UnsubscribeFromServerStartedCallback();
    }

    private void HandleServerStarted()
    {
        RefreshRuntimeSubscriptions();

        if (IsCurrentGameScene())
            SpawnPlayersForConnectedClients();
    }

    private void RefreshRuntimeSubscriptions()
    {
        if (!HasRequiredReferences())
            return;

        if (!networkManager.IsServer)
            return;

        SubscribeToClientConnectedCallback();
        SubscribeToNetworkSceneCallback();
    }

    private void SubscribeToServerStartedCallback()
    {
        if (serverStartedCallbackSubscribed)
            return;

        serverStartedCallbackNetworkManager = networkManager;
        serverStartedCallbackNetworkManager.OnServerStarted += HandleServerStarted;

        serverStartedCallbackSubscribed = true;
    }

    private void UnsubscribeFromServerStartedCallback()
    {
        if (!serverStartedCallbackSubscribed)
            return;

        if (serverStartedCallbackNetworkManager != null)
            serverStartedCallbackNetworkManager.OnServerStarted -= HandleServerStarted;

        serverStartedCallbackNetworkManager = null;
        serverStartedCallbackSubscribed = false;
    }

    private void SubscribeToClientConnectedCallback()
    {
        if (clientConnectedCallbackSubscribed && clientConnectedCallbackNetworkManager == networkManager)
            return;

        UnsubscribeFromClientConnectedCallback();

        clientConnectedCallbackNetworkManager = networkManager;
        clientConnectedCallbackNetworkManager.OnClientConnectedCallback += HandleClientConnected;

        clientConnectedCallbackSubscribed = true;
    }

    private void UnsubscribeFromClientConnectedCallback()
    {
        if (!clientConnectedCallbackSubscribed)
            return;

        if (clientConnectedCallbackNetworkManager != null)
            clientConnectedCallbackNetworkManager.OnClientConnectedCallback -= HandleClientConnected;

        clientConnectedCallbackNetworkManager = null;
        clientConnectedCallbackSubscribed = false;
    }

    private void SubscribeToNetworkSceneCallback()
    {
        if (networkManager.SceneManager == null)
        {
            Debug.LogError($"{nameof(NetworkPlayerSpawner)} cannot subscribe to scene events because NetworkSceneManager is missing.", this);
            return;
        }

        if (networkSceneCallbackSubscribed && networkSceneManager == networkManager.SceneManager)
            return;

        UnsubscribeFromNetworkSceneCallback();

        networkSceneManager = networkManager.SceneManager;
        networkSceneManager.OnLoadEventCompleted += HandleNetworkLoadEventCompleted;

        networkSceneCallbackSubscribed = true;
    }

    private void UnsubscribeFromNetworkSceneCallback()
    {
        if (!networkSceneCallbackSubscribed)
            return;

        if (networkSceneManager != null)
            networkSceneManager.OnLoadEventCompleted -= HandleNetworkLoadEventCompleted;

        networkSceneManager = null;
        networkSceneCallbackSubscribed = false;
    }

    private void UnsubscribeFromRuntimeSubscriptions()
    {
        UnsubscribeFromClientConnectedCallback();
        UnsubscribeFromNetworkSceneCallback();
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!CanSpawn())
            return;

        if (!IsCurrentGameScene())
            return;

        SpawnPlayerForClient(clientId);
    }

    private void HandleNetworkLoadEventCompleted(
        string sceneName,
        LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut)
    {
        if (!CanSpawn())
            return;

        if (sceneName != sceneNavigator.GameSceneName)
            return;

        SpawnPlayersForConnectedClients();
    }

    private void SpawnPlayersForConnectedClients()
    {
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
            SpawnPlayerForClient(clientId);
    }

    private void SpawnPlayerForClient(ulong clientId)
    {
        if (!networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            return;

        if (client.PlayerObject != null)
            return;

        if (!playerPrefab.TryGetComponent(out NetworkObject playerNetworkObject))
        {
            Debug.LogError("Player prefab is missing NetworkObject.", playerPrefab);
            return;
        }

        NetworkObject playerInstance = Instantiate(
            playerNetworkObject,
            playerPrefab.transform.position,
            playerPrefab.transform.rotation);

        playerInstance.SpawnAsPlayerObject(clientId, true);
    }

    private bool CanSpawn()
    {
        if (!HasRequiredReferences())
            return false;

        return networkManager.IsServer;
    }

    private bool IsCurrentGameScene()
    {
        return SceneManager.GetActiveScene().name == sceneNavigator.GameSceneName;
    }

    private bool HasRequiredReferences()
    {
        if (networkManager == null)
        {
            Debug.LogError($"{nameof(NetworkPlayerSpawner)} is missing {nameof(NetworkManager)}.", this);
            return false;
        }

        if (sceneNavigator == null)
        {
            Debug.LogError($"{nameof(NetworkPlayerSpawner)} is missing {nameof(ProjectSceneNavigator)}.", this);
            return false;
        }

        if (playerPrefab == null)
        {
            Debug.LogError($"{nameof(NetworkPlayerSpawner)} is missing player prefab.", this);
            return false;
        }

        return true;
    }
}