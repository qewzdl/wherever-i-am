using System;
using System.Collections.Generic;
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
    private readonly HashSet<string> objectiveIds = new HashSet<string>();

    public event Action<ObjectiveProgressData> ObjectiveProgressChanged;

    public bool IsServerActive => IsSpawned && IsServer;
    public GameplayEventHub GameplayEventHub => gameplayEventHub;

    private void Awake()
    {
        progressStates = new NetworkList<ObjectiveProgressData>();
    }

    public override void OnNetworkSpawn()
    {
        progressStates.OnListChanged += HandleProgressListChanged;

        if (!IsServer)
        {
            return;
        }

        if (!ValidateSetup())
        {
            enabled = false;
            return;
        }

        InitializeObjectives();

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
        progressStates.OnListChanged -= HandleProgressListChanged;

        if (IsServer && objectives != null)
        {
            for (int i = 0; i < objectives.Length; i++)
            {
                if (objectives[i] != null)
                {
                    objectives[i].StopObjectiveServerOnly();
                }
            }
        }
    }

    public int ProgressCount => progressStates.Count;

    public ObjectiveProgressData GetProgress(int index)
    {
        return progressStates[index];
    }

    public void StartObjectivesServerOnly()
    {
        if (!IsServerActive)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} can start objectives only on server.", this);
            return;
        }

        for (int i = 0; i < objectives.Length; i++)
        {
            objectives[i].StartObjectiveServerOnly();
        }
    }

    public void StopObjectivesServerOnly()
    {
        if (!IsServerActive)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} can stop objectives only on server.", this);
            return;
        }

        for (int i = 0; i < objectives.Length; i++)
        {
            objectives[i].StopObjectiveServerOnly();
        }
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

        UpdateObjectiveProgress(objective);

        if (!objective.CompletesGame)
        {
            return;
        }

        gameFlow.FinishGameServerOnly(
            objective.ResultType,
            objective.CompletionReason,
            objective.ObjectiveId,
            instigatorClientId);
    }

    internal void UpdateObjectiveProgress(ObjectiveCondition objective)
    {
        if (!IsServerActive || objective == null)
        {
            return;
        }

        ObjectiveProgressData progress = ObjectiveProgressData.Create(
            objective.ObjectiveId,
            objective.DisplayName,
            objective.CurrentValue,
            objective.TargetValue,
            objective.IsCompleted);

        int index = FindProgressIndex(objective.ObjectiveId);

        if (index >= 0)
        {
            progressStates[index] = progress;
            return;
        }

        progressStates.Add(progress);
    }

    private bool ValidateSetup()
    {
        if (gameFlow == null)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} requires {nameof(NetworkGameFlow)} reference.", this);
            return false;
        }

        if (objectives == null || objectives.Length == 0)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} requires at least one objective.", this);
            return false;
        }

        objectiveIds.Clear();

        for (int i = 0; i < objectives.Length; i++)
        {
            ObjectiveCondition objective = objectives[i];

            if (objective == null)
            {
                Debug.LogError($"{nameof(ObjectiveManager)} has null objective at index {i}.", this);
                return false;
            }

            if (string.IsNullOrWhiteSpace(objective.ObjectiveId))
            {
                Debug.LogError($"{nameof(ObjectiveManager)} has objective with empty id at index {i}.", objective);
                return false;
            }

            if (!objectiveIds.Add(objective.ObjectiveId))
            {
                Debug.LogError($"{nameof(ObjectiveManager)} has duplicate objective id: {objective.ObjectiveId}.", objective);
                return false;
            }

            if (objective.RequiresGameplayEventHub && gameplayEventHub == null)
            {
                Debug.LogError($"{objective.GetType().Name} requires {nameof(GameplayEventHub)} reference.", objective);
                return false;
            }
        }

        return true;
    }

    private void InitializeObjectives()
    {
        progressStates.Clear();

        for (int i = 0; i < objectives.Length; i++)
        {
            ObjectiveCondition objective = objectives[i];
            objective.Initialize(this, gameplayEventHub);
            UpdateObjectiveProgress(objective);
        }
    }

    private int FindProgressIndex(string objectiveId)
    {
        for (int i = 0; i < progressStates.Count; i++)
        {
            if (progressStates[i].ObjectiveId.ToString() == objectiveId)
            {
                return i;
            }
        }

        return -1;
    }

    private void HandleProgressListChanged(NetworkListEvent<ObjectiveProgressData> changeEvent)
    {
        ObjectiveProgressChanged?.Invoke(changeEvent.Value);
    }
}