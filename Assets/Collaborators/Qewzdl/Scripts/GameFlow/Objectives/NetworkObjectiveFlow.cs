using System;
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

    private ObjectiveSceneBinding activeBinding;

    public event Action<ObjectiveNetworkState, ObjectiveNetworkState> ObjectiveStateChanged;

    public ObjectiveNetworkState CurrentObjective => currentObjective.Value;
    public bool HasActiveObjective => currentObjective.Value.State == ObjectiveRuntimeState.Active;

    public override void OnNetworkSpawn()
    {
        currentObjective.OnValueChanged += HandleObjectiveStateChanged;

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

        gameFlow.PhaseChanged += HandleGamePhaseChanged;
        gameFlow.MatchResolved += HandleMatchResolved;

        if (startFirstObjectiveWhenMatchStarts && gameFlow.IsMatchRunning)
        {
            StartFirstObjectiveServerOnly();
        }
    }

    public override void OnNetworkDespawn()
    {
        currentObjective.OnValueChanged -= HandleObjectiveStateChanged;

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

        return TryActivateObjectiveServerOnly(0);
    }

    public bool ReportObjectiveProgressServerOnly(string objectiveId, float progress01, ulong instigatorClientId)
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkObjectiveFlow)} can report objective progress only on server.", this);
            return false;
        }

        if (!gameFlow.IsMatchRunning)
        {
            return false;
        }

        ObjectiveNetworkState state = currentObjective.Value;

        if (state.State != ObjectiveRuntimeState.Active)
        {
            return false;
        }

        if (!string.Equals(state.ObjectiveId.ToString(), objectiveId, StringComparison.Ordinal))
        {
            Debug.LogError(
                $"{nameof(NetworkObjectiveFlow)} rejected progress for objective '{objectiveId}'. Active objective: '{state.ObjectiveId}'.",
                this);

            return false;
        }

        float clampedProgress = Mathf.Clamp01(progress01);

        if (clampedProgress <= state.Progress01)
        {
            return false;
        }

        state.Progress01 = clampedProgress;
        currentObjective.Value = state;

        if (state.Progress01 >= 1f)
        {
            return CompleteObjectiveServerOnly(objectiveId, instigatorClientId);
        }

        return true;
    }

    public bool CompleteObjectiveServerOnly(string objectiveId, ulong instigatorClientId)
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkObjectiveFlow)} can complete objective only on server.", this);
            return false;
        }

        if (!gameFlow.IsMatchRunning)
        {
            return false;
        }

        ObjectiveNetworkState state = currentObjective.Value;

        if (state.State != ObjectiveRuntimeState.Active)
        {
            return false;
        }

        if (!string.Equals(state.ObjectiveId.ToString(), objectiveId, StringComparison.Ordinal))
        {
            Debug.LogError(
                $"{nameof(NetworkObjectiveFlow)} rejected completion for objective '{objectiveId}'. Active objective: '{state.ObjectiveId}'.",
                this);

            return false;
        }

        ObjectiveDefinition objective = objectiveSequence.GetObjective(state.SequenceIndex);

        if (objective == null)
        {
            Debug.LogError(
                $"{nameof(NetworkObjectiveFlow)} cannot complete objective at index {state.SequenceIndex}: definition is null.",
                this);

            return false;
        }

        state.State = ObjectiveRuntimeState.Completed;
        state.Progress01 = 1f;
        currentObjective.Value = state;

        DeactivateActiveBinding();

        int nextIndex = state.SequenceIndex + 1;

        if (nextIndex < objectiveSequence.Count)
        {
            return TryActivateObjectiveServerOnly(nextIndex);
        }

        GameResultData result = GameResultData.Create(
            objective.CompletionResult,
            MatchResultSource.Objective,
            objective.ObjectiveId,
            objective.CompletionReason,
            instigatorClientId);

        return gameFlow.CompleteMatchServerOnly(result);
    }

    private bool TryActivateObjectiveServerOnly(int index)
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

        activeBinding = binding;

        if (activeBinding != null)
        {
            activeBinding.SetActiveState(true);
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
}