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

    public override void OnNetworkSpawn()
    {
        if (!ValidateSetup())
        {
            enabled = false;
            return;
        }

        if (!IsServer)
        {
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
            !gameMapService.IsReadyForMatch)
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
        gameMapService.MapReady += HandleDependencyReady;
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

        if (gameMapService != null)
        {
            gameMapService.MapReady -= HandleDependencyReady;
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

    private bool ValidateSetup()
    {
        if (gameMapService == null)
        {
            ProjectContext context = ProjectContext.Instance;
            gameMapService = context != null ? context.GameMaps : null;
        }

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

        if (gameMapService == null)
        {
            Debug.LogError($"{nameof(NetworkGameFlowSceneStarter)} requires {nameof(GameMapService)}.", this);
            return false;
        }

        return true;
    }
}
