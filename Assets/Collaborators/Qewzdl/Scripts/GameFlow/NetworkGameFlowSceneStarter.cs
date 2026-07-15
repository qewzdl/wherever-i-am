using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkGameFlowSceneStarter : NetworkBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private NetworkGameFlow gameFlow;
    [SerializeField] private NetworkObjectiveFlow objectiveFlow;
    [SerializeField] private GameMapService gameMapService;

    [Header("Start")]
    [SerializeField] private bool startOnServerSpawn = true;
    [SerializeField] private string startReason = "Game scene network spawn completed";
    private bool isSubscribed;
    private IGameMapSessionService mapSessionService;
    private IGameStateService gameStateService;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        if (!ValidateSetup())
        {
            enabled = false;
            return;
        }

        if (!startOnServerSpawn)
        {
            return;
        }

        SubscribeReadinessEvents();
        TryStartMatchWhenReadyServerOnly();
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeReadinessEvents();
    }

    private void OnDisable()
    {
        UnsubscribeReadinessEvents();
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(startReason))
        {
            startReason = "Game scene network spawn completed";
        }
    }

    public bool StartMatchServerOnly()
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkGameFlowSceneStarter)} can start match only on server.", this);
            return false;
        }

        if (!ValidateSetup())
        {
            return false;
        }

        return TryStartMatchWhenReadyServerOnly();
    }

    private bool TryStartMatchWhenReadyServerOnly()
    {
        if (!IsServer || !IsSpawned)
        {
            return false;
        }

        if (!gameFlow.IsServerReady ||
            !objectiveFlow.IsServerReady ||
            !mapSessionService.IsReadyForMatch ||
            gameStateService.CurrentState != GameState.InGame)
        {
            return false;
        }

        if (gameFlow.CurrentPhase != GamePhase.Waiting)
        {
            return false;
        }

        if (gameFlow.StartMatchServerOnly(startReason))
        {
            return true;
        }

        Debug.LogError($"{nameof(NetworkGameFlowSceneStarter)} failed to start match after game flow dependencies became ready.", this);
        return false;
    }

    private void SubscribeReadinessEvents()
    {
        if (isSubscribed)
        {
            return;
        }

        gameFlow.ServerReady += HandleDependencyReady;
        objectiveFlow.ServerReady += HandleDependencyReady;
        mapSessionService.MapReady += HandleDependencyReady;
        gameStateService.StateChanged += HandleGameStateChanged;
        isSubscribed = true;
    }

    private void UnsubscribeReadinessEvents()
    {
        if (!isSubscribed)
        {
            return;
        }

        if (gameFlow != null)
        {
            gameFlow.ServerReady -= HandleDependencyReady;
        }

        if (objectiveFlow != null)
        {
            objectiveFlow.ServerReady -= HandleDependencyReady;
        }

        if (mapSessionService != null)
        {
            mapSessionService.MapReady -= HandleDependencyReady;
        }

        if (gameStateService != null)
        {
            gameStateService.StateChanged -= HandleGameStateChanged;
        }

        isSubscribed = false;
    }

    private void HandleDependencyReady()
    {
        if (!startOnServerSpawn)
        {
            return;
        }

        TryStartMatchWhenReadyServerOnly();
    }

    private void HandleGameStateChanged(GameState previous, GameState current)
    {
        if (!startOnServerSpawn || current != GameState.InGame)
            return;

        TryStartMatchWhenReadyServerOnly();
    }

    private bool ValidateSetup()
    {
        mapSessionService = gameMapService;

        if (mapSessionService == null)
        {
            NetworkObjectServiceContext.TryResolveSessionService(
                NetworkManager,
                out mapSessionService);
        }

        NetworkObjectServiceContext.TryResolveSessionService(
            NetworkManager,
            out gameStateService);

        if (gameFlow == null)
        {
            Debug.LogError($"{nameof(NetworkGameFlowSceneStarter)} requires assigned {nameof(NetworkGameFlow)}.", this);
            return false;
        }

        if (objectiveFlow == null)
        {
            Debug.LogError($"{nameof(NetworkGameFlowSceneStarter)} requires assigned {nameof(NetworkObjectiveFlow)}.", this);
            return false;
        }

        if (mapSessionService == null)
        {
            Debug.LogError($"{nameof(NetworkGameFlowSceneStarter)} requires {nameof(IGameMapSessionService)}.", this);
            return false;
        }

        if (gameStateService == null)
        {
            Debug.LogError($"{nameof(NetworkGameFlowSceneStarter)} requires {nameof(IGameStateService)}.", this);
            return false;
        }

        return true;
    }
}
