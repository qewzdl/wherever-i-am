using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public sealed class NetworkObjectiveFlow : NetworkBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private NetworkGameFlow gameFlow;
    [SerializeField] private ObjectiveSequenceDefinition objectiveSequence;
    [SerializeField] private ObjectiveSceneBindingRegistry sceneBindingRegistry;

    [Header("Behaviour")]
    [SerializeField] private bool startFirstObjectiveWhenMatchStarts = true;

    private readonly NetworkVariable<ObjectiveNetworkState> currentObjective = new NetworkVariable<ObjectiveNetworkState>(
        ObjectiveNetworkState.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<FixedString128Bytes> lastObjectiveReason = new NetworkVariable<FixedString128Bytes>(
        "No objective active",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private ObjectiveSceneBinding activeBinding;
    private bool serverReady;

    public event Action ServerReady;
    public event Action<ObjectiveNetworkState, ObjectiveNetworkState> ObjectiveStateChanged;
    public event Action<FixedString128Bytes, FixedString128Bytes> ObjectiveReasonChanged;

    public ObjectiveNetworkState CurrentObjective => currentObjective.Value;
    public string LastObjectiveReason => lastObjectiveReason.Value.ToString();
    public bool HasActiveObjective => currentObjective.Value.State == ObjectiveRuntimeState.Active;
    public bool IsServerReady => IsSpawned && IsServer && serverReady;

    public override void OnNetworkSpawn()
    {
        serverReady = false;
        currentObjective.OnValueChanged += HandleObjectiveStateChanged;
        lastObjectiveReason.OnValueChanged += HandleObjectiveReasonChanged;

        if (!ValidateSetup())
        {
            enabled = false;
            return;
        }

        if (!IsServer)
        {
            return;
        }

        sceneBindingRegistry.BindAll(this);
        currentObjective.Value = ObjectiveNetworkState.None;
        lastObjectiveReason.Value = "Objective flow spawned";

        gameFlow.PhaseChanged += HandleGamePhaseChanged;
        gameFlow.MatchResolved += HandleMatchResolved;

        serverReady = true;
        ServerReady?.Invoke();

        if (startFirstObjectiveWhenMatchStarts && gameFlow.IsMatchRunning)
        {
            StartFirstObjectiveServerOnly();
        }
    }

    public override void OnNetworkDespawn()
    {
        currentObjective.OnValueChanged -= HandleObjectiveStateChanged;
        lastObjectiveReason.OnValueChanged -= HandleObjectiveReasonChanged;

        if (IsServer && gameFlow != null)
        {
            gameFlow.PhaseChanged -= HandleGamePhaseChanged;
            gameFlow.MatchResolved -= HandleMatchResolved;
        }

        if (sceneBindingRegistry != null)
        {
            sceneBindingRegistry.DeactivateAll();
        }

        activeBinding = null;
        serverReady = false;
    }

    public bool StartFirstObjectiveServerOnly()
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkObjectiveFlow)} can start objectives only on server.", this);
            return false;
        }

        if (!gameFlow.IsMatchRunning)
        {
            return false;
        }

        if (currentObjective.Value.State == ObjectiveRuntimeState.Active)
        {
            return false;
        }

        return TryActivateObjectiveServerOnly(0, "Match entered Playing phase");
    }

    public bool ReportObjectiveProgressServerOnly(string objectiveId, float progress01, ulong instigatorClientId)
    {
        return ReportObjectiveProgressNormalizedServerOnly(objectiveId, progress01, instigatorClientId);
    }

    public bool ReportObjectiveProgressAmountServerOnly(string objectiveId, float progressAmount, ulong instigatorClientId)
    {
        if (!TryGetActiveObjective(objectiveId, out ObjectiveNetworkState state, out ObjectiveDefinition objective))
        {
            return false;
        }

        float normalizedProgress = Mathf.Clamp01(progressAmount / objective.RequiredProgress);
        return ApplyObjectiveProgressServerOnly(state, objective, normalizedProgress, instigatorClientId, "Objective progress amount reported");
    }

    public bool ReportObjectiveProgressNormalizedServerOnly(string objectiveId, float progress01, ulong instigatorClientId)
    {
        if (!TryGetActiveObjective(objectiveId, out ObjectiveNetworkState state, out ObjectiveDefinition objective))
        {
            return false;
        }

        float normalizedProgress = Mathf.Clamp01(progress01);
        return ApplyObjectiveProgressServerOnly(state, objective, normalizedProgress, instigatorClientId, "Objective normalized progress reported");
    }

    public bool CompleteObjectiveServerOnly(string objectiveId, ulong instigatorClientId)
    {
        if (!TryGetActiveObjective(objectiveId, out ObjectiveNetworkState state, out ObjectiveDefinition objective))
        {
            return false;
        }

        return CompleteObjectiveServerOnly(state, objective, instigatorClientId);
    }

    private bool ApplyObjectiveProgressServerOnly(
        ObjectiveNetworkState state,
        ObjectiveDefinition objective,
        float normalizedProgress,
        ulong instigatorClientId,
        string reason)
    {
        if (normalizedProgress <= state.Progress01)
        {
            return false;
        }

        state.Progress01 = normalizedProgress;
        currentObjective.Value = state;
        lastObjectiveReason.Value = reason;

        if (state.Progress01 >= 1f)
        {
            return CompleteObjectiveServerOnly(state, objective, instigatorClientId);
        }

        return true;
    }

    private bool CompleteObjectiveServerOnly(
        ObjectiveNetworkState state,
        ObjectiveDefinition objective,
        ulong instigatorClientId)
    {
        state.State = ObjectiveRuntimeState.Completed;
        state.Progress01 = 1f;
        currentObjective.Value = state;
        lastObjectiveReason.Value = $"Objective '{objective.ObjectiveId}' completed";

        DeactivateActiveBinding();

        int nextIndex = state.SequenceIndex + 1;

        if (nextIndex < objectiveSequence.Count)
        {
            return TryActivateObjectiveServerOnly(nextIndex, $"Objective '{objective.ObjectiveId}' completed");
        }

        if (!objective.CompletesGame)
        {
            Debug.LogError(
                $"{nameof(NetworkObjectiveFlow)} sequence ended with objective '{objective.ObjectiveId}' but it does not complete the game.",
                this);

            return false;
        }

        GameResultData result = GameResultData.Create(
            objective.CompletionResult,
            MatchResultSource.Objective,
            objective.ObjectiveId,
            objective.CompletionReason,
            instigatorClientId);

        return gameFlow.CompleteMatchServerOnly(result, objective.CompletionReason);
    }

    private bool TryActivateObjectiveServerOnly(int index, string reason)
    {
        ObjectiveDefinition objective = objectiveSequence.GetObjective(index);

        if (objective == null)
        {
            Debug.LogError($"{nameof(NetworkObjectiveFlow)} cannot activate objective at index {index}: definition is null.", this);
            return false;
        }

        if (!objective.IsValid(out string error))
        {
            Debug.LogError($"{nameof(NetworkObjectiveFlow)} cannot activate objective '{objective.name}': {error}", this);
            return false;
        }

        DeactivateActiveBinding();

        ObjectiveSceneBinding binding = null;

        if (objective.RequiresSceneBinding)
        {
            if (!sceneBindingRegistry.TryGetBinding(objective.ObjectiveId, out binding))
            {
                Debug.LogError(
                    $"{nameof(NetworkObjectiveFlow)} cannot activate objective '{objective.ObjectiveId}': required scene binding is missing.",
                    this);

                return false;
            }
        }

        ObjectiveNetworkState nextState = new ObjectiveNetworkState
        {
            ObjectiveId = objective.ObjectiveId,
            SequenceIndex = index,
            State = ObjectiveRuntimeState.Active,
            Progress01 = 0f
        };

        currentObjective.Value = nextState;
        lastObjectiveReason.Value = GetReason(reason, $"Objective '{objective.ObjectiveId}' activated");

        activeBinding = binding;

        if (activeBinding != null)
        {
            activeBinding.SetActiveState(true);
        }

        return true;
    }

    private bool TryGetActiveObjective(
        string objectiveId,
        out ObjectiveNetworkState state,
        out ObjectiveDefinition objective)
    {
        state = currentObjective.Value;
        objective = null;

        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkObjectiveFlow)} can change objectives only on server.", this);
            return false;
        }

        if (!gameFlow.IsMatchRunning)
        {
            return false;
        }

        if (state.State != ObjectiveRuntimeState.Active)
        {
            return false;
        }

        if (!string.Equals(state.ObjectiveId.ToString(), objectiveId, StringComparison.Ordinal))
        {
            Debug.LogError(
                $"{nameof(NetworkObjectiveFlow)} rejected objective update for '{objectiveId}'. Active objective: '{state.ObjectiveId}'.",
                this);

            return false;
        }

        objective = objectiveSequence.GetObjective(state.SequenceIndex);

        if (objective == null)
        {
            Debug.LogError(
                $"{nameof(NetworkObjectiveFlow)} cannot update objective at index {state.SequenceIndex}: definition is null.",
                this);

            return false;
        }

        return true;
    }

    private bool ValidateSetup()
    {
        if (gameFlow == null)
        {
            Debug.LogError($"{nameof(NetworkObjectiveFlow)} requires assigned {nameof(NetworkGameFlow)}.", this);
            return false;
        }

        if (objectiveSequence == null)
        {
            Debug.LogError($"{nameof(NetworkObjectiveFlow)} requires assigned {nameof(ObjectiveSequenceDefinition)}.", this);
            return false;
        }

        if (!objectiveSequence.IsValid(out string sequenceError))
        {
            Debug.LogError($"{nameof(NetworkObjectiveFlow)} has invalid objective sequence: {sequenceError}", this);
            return false;
        }

        if (sceneBindingRegistry == null)
        {
            Debug.LogError($"{nameof(NetworkObjectiveFlow)} requires assigned {nameof(ObjectiveSceneBindingRegistry)}.", this);
            return false;
        }

        if (!sceneBindingRegistry.IsValidForSequence(objectiveSequence, out string bindingError))
        {
            Debug.LogError($"{nameof(NetworkObjectiveFlow)} has invalid scene bindings: {bindingError}", this);
            return false;
        }

        return true;
    }

    private void HandleGamePhaseChanged(GamePhase previousPhase, GamePhase newPhase)
    {
        if (!IsServer)
        {
            return;
        }

        if (newPhase == GamePhase.Playing && startFirstObjectiveWhenMatchStarts)
        {
            StartFirstObjectiveServerOnly();
            return;
        }

        if (newPhase == GamePhase.MatchResolved || newPhase == GamePhase.Ending || newPhase == GamePhase.Finished)
        {
            DeactivateActiveBinding();
        }
    }

    private void HandleMatchResolved(GameResultData result)
    {
        if (!IsServer)
        {
            return;
        }

        DeactivateActiveBinding();
    }

    private void DeactivateActiveBinding()
    {
        if (activeBinding != null)
        {
            activeBinding.SetActiveState(false);
            activeBinding = null;
        }
    }

    private void HandleObjectiveStateChanged(ObjectiveNetworkState previousValue, ObjectiveNetworkState newValue)
    {
        ObjectiveStateChanged?.Invoke(previousValue, newValue);
    }

    private void HandleObjectiveReasonChanged(FixedString128Bytes previousValue, FixedString128Bytes newValue)
    {
        ObjectiveReasonChanged?.Invoke(previousValue, newValue);
    }

    private string GetReason(string reason, string fallback)
    {
        return string.IsNullOrWhiteSpace(reason) ? fallback : reason;
    }
}
