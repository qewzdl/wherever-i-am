using System;
using Unity.Netcode;
using UnityEngine;

public sealed class ObjectiveManager : NetworkBehaviour
{
    [Header("Required")]
    [SerializeField] private NetworkGameFlow gameFlow;

    [Header("Optional")]
    [SerializeField] private GameplayEventHub gameplayEventHub;

    [Header("Objectives")]
    [SerializeField] private ObjectiveCondition[] objectives;
    [SerializeField] private bool startGameFlowOnSpawn = true;
    [SerializeField] private bool startObjectivesOnSpawn = true;

    private NetworkList<ObjectiveProgressData> progressStates;

    private ObjectiveRuntimeService runtimeService;
    private ObjectiveProgressSync progressSync;
    private ObjectiveMatchResultService matchResultService;

    public event Action<ObjectiveProgressData> ObjectiveProgressChanged;

    public bool IsServerActive => IsSpawned && IsServer;
    public GameplayEventHub GameplayEventHub => gameplayEventHub;

    private void Awake()
    {
        progressStates = new NetworkList<ObjectiveProgressData>();

        runtimeService = new ObjectiveRuntimeService();
        progressSync = new ObjectiveProgressSync(progressStates);
        matchResultService = new ObjectiveMatchResultService();
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

        if (!matchResultService.Initialize(gameFlow, objectives, this))
        {
            enabled = false;
            return;
        }

        runtimeService.InitializeObjectivesServerOnly(progressSync);

        if (startGameFlowOnSpawn)
        {
            gameFlow.StartGameServerOnly();
        }

        if (startObjectivesOnSpawn)
        {
            StartObjectivesServerOnly();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            runtimeService.CancelObjectivesServerOnly();
        }

        progressSync.ProgressChanged -= HandleObjectiveProgressChanged;
        progressSync.Unsubscribe();
    }

    public int ProgressCount => progressSync.ProgressCount;

    public ObjectiveProgressData GetProgress(int index)
    {
        return progressSync.GetProgress(index);
    }

    public void StartObjectivesServerOnly()
    {
        if (!IsServerActive)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} can start objectives only on server.", this);
            return;
        }

        runtimeService.StartObjectivesServerOnly();
    }

    public void StopObjectivesServerOnly()
    {
        if (!IsServerActive)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} can stop objectives only on server.", this);
            return;
        }

        runtimeService.CancelObjectivesServerOnly();
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

    internal void UpdateObjectiveProgress(ObjectiveCondition objective)
    {
        if (!IsServerActive || objective == null)
        {
            return;
        }

        progressSync.UpsertObjectiveServerOnly(objective);
    }

    private void HandleObjectiveProgressChanged(ObjectiveProgressData progressData)
    {
        ObjectiveProgressChanged?.Invoke(progressData);
    }
}