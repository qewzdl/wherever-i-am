using Unity.Netcode;
using UnityEngine;

public sealed class MatchStartupService : NetworkBehaviour
{
    [Header("Required")]
    [SerializeField] private NetworkGameFlow gameFlow;
    [SerializeField] private ObjectiveManager objectiveManager;

    [Header("Startup")]
    [SerializeField] private bool startOnNetworkSpawn = true;

    private bool startupCompleted;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            return;
        }

        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        objectiveManager.ObjectivesInitialized += HandleObjectivesInitialized;

        if (startOnNetworkSpawn)
        {
            TryStartWhenReadyServerOnly();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (objectiveManager != null)
        {
            objectiveManager.ObjectivesInitialized -= HandleObjectivesInitialized;
        }

        startupCompleted = false;
    }

    public bool StartMatchServerOnly()
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(MatchStartupService)} can start match only on server.", this);
            return false;
        }

        if (!ValidateReferences())
        {
            return false;
        }

        if (startupCompleted)
        {
            return false;
        }

        if (!objectiveManager.AreObjectivesInitialized)
        {
            Debug.LogError($"{nameof(MatchStartupService)} cannot start objectives before {nameof(ObjectiveManager)} initialization.", this);
            return false;
        }

        if (gameFlow.CurrentPhase != GamePhase.Playing)
        {
            if (!gameFlow.StartMatchServerOnly())
            {
                return false;
            }
        }

        if (gameFlow.CurrentPhase != GamePhase.Playing)
        {
            Debug.LogError(
                $"{nameof(MatchStartupService)} expected {nameof(NetworkGameFlow)} phase {nameof(GamePhase.Playing)} before starting objectives. Current phase: {gameFlow.CurrentPhase}.",
                this);

            return false;
        }

        if (!objectiveManager.StartObjectivesServerOnly())
        {
            return false;
        }

        startupCompleted = true;
        return true;
    }

    private void HandleObjectivesInitialized()
    {
        if (!startOnNetworkSpawn || startupCompleted)
        {
            return;
        }

        TryStartWhenReadyServerOnly();
    }

    private bool TryStartWhenReadyServerOnly()
    {
        if (!IsServer || startupCompleted)
        {
            return false;
        }

        if (!objectiveManager.AreObjectivesInitialized)
        {
            return false;
        }

        return StartMatchServerOnly();
    }

    private bool ValidateReferences()
    {
        if (gameFlow == null)
        {
            Debug.LogError($"{nameof(MatchStartupService)} requires {nameof(NetworkGameFlow)} reference.", this);
            return false;
        }

        if (objectiveManager == null)
        {
            Debug.LogError($"{nameof(MatchStartupService)} requires {nameof(ObjectiveManager)} reference.", this);
            return false;
        }

        return true;
    }
}