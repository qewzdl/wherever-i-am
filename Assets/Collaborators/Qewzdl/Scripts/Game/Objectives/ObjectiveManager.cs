using System;
using Unity.Netcode;
using UnityEngine;

public sealed class ObjectiveManager : NetworkBehaviour
{
    [Header("Optional")]
    [SerializeField] private GameplayEventHub gameplayEventHub;

    [Header("Objectives")]
    [SerializeField] private ObjectiveCondition[] objectives;

    private NetworkList<ObjectiveProgressData> progressStates;

    private ObjectiveRuntimeService runtimeService;
    private ObjectiveProgressSync progressSync;
    private ObjectiveMatchResultService matchResultService;
    private bool objectivesInitialized;

    public event Action ObjectivesInitialized;
    public event Action<ObjectiveProgressData> ObjectiveProgressChanged;

    public bool IsServerActive => IsSpawned && IsServer;
    public bool AreObjectivesInitialized => objectivesInitialized;
    public GameplayEventHub GameplayEventHub => gameplayEventHub;

    internal ObjectiveCondition[] ObjectiveConditions => objectives;

    private void Awake()
    {
        progressStates = new NetworkList<ObjectiveProgressData>();

        runtimeService = new ObjectiveRuntimeService();
        progressSync = new ObjectiveProgressSync(progressStates);
    }

    public override void OnNetworkSpawn()
    {
        progressSync.Subscribe();
        progressSync.ProgressChanged += HandleObjectiveProgressChanged;

        if (!IsServer)
        {
            return;
        }

        if (!runtimeService.Initialize(this, gameplayEventHub, objectives))
        {
            enabled = false;
            return;
        }

        if (!HasMatchResultService())
        {
            enabled = false;
            return;
        }

        runtimeService.InitializeObjectivesServerOnly(progressSync);

        objectivesInitialized = true;
        ObjectivesInitialized?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            runtimeService.CancelObjectivesServerOnly();
        }

        objectivesInitialized = false;

        progressSync.ProgressChanged -= HandleObjectiveProgressChanged;
        progressSync.Unsubscribe();
    }

    public int ProgressCount => progressSync.ProgressCount;

    public ObjectiveProgressData GetProgress(int index)
    {
        return progressSync.GetProgress(index);
    }

    public bool StartObjectivesServerOnly()
    {
        if (!IsServerActive)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} can start objectives only on server.", this);
            return false;
        }

        if (!objectivesInitialized)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} cannot start objectives before initialization.", this);
            return false;
        }

        return runtimeService.StartObjectivesServerOnly();
    }

    public bool StopObjectivesServerOnly()
    {
        if (!IsServerActive)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} can stop objectives only on server.", this);
            return false;
        }

        return runtimeService.CancelObjectivesServerOnly();
    }

    internal bool ConfigureMatchResultService(ObjectiveMatchResultService service)
    {
        if (objectivesInitialized)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} cannot configure {nameof(ObjectiveMatchResultService)} after objectives initialization.", this);
            return false;
        }

        if (service == null)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} requires initialized {nameof(ObjectiveMatchResultService)}.", this);
            return false;
        }

        matchResultService = service;
        return true;
    }

    internal void HandleObjectiveCompleted(ObjectiveCondition objective, ulong instigatorClientId)
    {
        if (!IsServerActive)
        {
            return;
        }

        if (objective == null)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} received null completed objective.", this);
            return;
        }

        progressSync.UpsertObjectiveServerOnly(objective);

        ObjectiveOutcome outcome = ObjectiveOutcome.Completed(objective, instigatorClientId);
        matchResultService.HandleObjectiveOutcomeServerOnly(outcome, this);
    }

    internal void HandleObjectiveFailed(ObjectiveCondition objective, ulong instigatorClientId)
    {
        if (!IsServerActive)
        {
            return;
        }

        if (objective == null)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} received null failed objective.", this);
            return;
        }

        progressSync.UpsertObjectiveServerOnly(objective);

        ObjectiveOutcome outcome = ObjectiveOutcome.Failed(objective, instigatorClientId);
        matchResultService.HandleObjectiveOutcomeServerOnly(outcome, this);
    }

    internal void HandleObjectiveCancelled(ObjectiveCondition objective, ulong instigatorClientId)
    {
        if (!IsServerActive)
        {
            return;
        }

        if (objective == null)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} received null cancelled objective.", this);
            return;
        }

        progressSync.UpsertObjectiveServerOnly(objective);

        ObjectiveOutcome outcome = ObjectiveOutcome.Cancelled(objective, instigatorClientId);
        matchResultService.HandleObjectiveOutcomeServerOnly(outcome, this);
    }

    internal void UpdateObjectiveProgress(ObjectiveCondition objective)
    {
        if (!IsServerActive || objective == null)
        {
            return;
        }

        progressSync.UpsertObjectiveServerOnly(objective);
    }

    private bool HasMatchResultService()
    {
        if (matchResultService != null)
        {
            return true;
        }

        Debug.LogError($"{nameof(ObjectiveManager)} requires configured {nameof(ObjectiveMatchResultService)} before server initialization.", this);
        return false;
    }

    private void HandleObjectiveProgressChanged(ObjectiveProgressData progressData)
    {
        ObjectiveProgressChanged?.Invoke(progressData);
    }
}