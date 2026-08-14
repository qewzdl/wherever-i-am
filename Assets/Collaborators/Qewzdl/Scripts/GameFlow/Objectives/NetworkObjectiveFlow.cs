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
    private ObjectiveSequenceDefinition activeObjectiveSequence;
    private IGameMapSessionService gameMapService;
    private bool subscribedToGameMap;
    private bool serverReady;
    private bool faultReported;
    private bool matchCompletionCommitted;

    public event Action ServerReady;
    public event Action<ObjectiveNetworkState, ObjectiveNetworkState> ObjectiveStateChanged;
    public event Action<FixedString128Bytes, FixedString128Bytes> ObjectiveReasonChanged;

    public ObjectiveNetworkState CurrentObjective => currentObjective.Value;

    // Which objective the index points at. Callers used to carry its name
    // around to hand back to the report methods; the flow knows it.
    public ObjectiveDefinition ActiveObjective =>
        activeObjectiveSequence == null
            ? null
            : activeObjectiveSequence.GetObjective(currentObjective.Value.SequenceIndex);

    public string LastObjectiveReason => lastObjectiveReason.Value.ToString();
    public bool HasActiveObjective => currentObjective.Value.State == ObjectiveRuntimeState.Active;
    public bool IsServerReady => IsSpawned && IsServer && serverReady;

#if UNITY_EDITOR
    public ObjectiveSequenceDefinition DefaultObjectiveSequenceEditor => objectiveSequence;
#endif

    public override void OnNetworkSpawn()
    {
        serverReady = false;
        faultReported = false;
        matchCompletionCommitted = false;
        currentObjective.OnValueChanged += HandleObjectiveStateChanged;
        lastObjectiveReason.OnValueChanged += HandleObjectiveReasonChanged;

        if (!ValidateStaticSetup())
        {
            FaultObjectiveFlowServer(
                $"{nameof(NetworkObjectiveFlow)} static setup validation failed.");
            enabled = false;
            return;
        }

        if (!IsServer)
        {
            return;
        }

        ResolveGameMapService();

        if (gameMapService != null && !gameMapService.IsReadyForMatch)
        {
            SubscribeToGameMap();
            return;
        }

        InitializeServer();
    }

    private void InitializeServer()
    {
        if (!IsServer || serverReady)
            return;

        ResolveMapDependencies();

        if (!ValidateRuntimeSetup())
        {
            FaultObjectiveFlowServer(
                $"{nameof(NetworkObjectiveFlow)} runtime setup validation failed.");
            enabled = false;
            return;
        }

        if (!sceneBindingRegistry.TryBindAll(this, out string bindingError))
        {
            Debug.LogError(bindingError, this);
            FaultObjectiveFlowServer(bindingError);
            enabled = false;
            return;
        }

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
        UnsubscribeFromGameMap();

        if (IsServer && gameFlow != null)
        {
            gameFlow.PhaseChanged -= HandleGamePhaseChanged;
            gameFlow.MatchResolved -= HandleMatchResolved;
        }

        if (sceneBindingRegistry != null)
        {
            sceneBindingRegistry.UnbindAll();
        }

        activeBinding = null;
        serverReady = false;
        faultReported = false;
        matchCompletionCommitted = false;
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

        if (TryPrepareObjectiveActivation(
                0,
                out ObjectiveDefinition objective,
                out ObjectiveSceneBinding binding,
                out string error))
        {
            CommitObjectiveActivation(
                0,
                objective,
                binding,
                "Match entered Playing phase");
            return true;
        }

        return FaultObjectiveFlowServer(error);
    }

    public bool ReportObjectiveProgressServerOnly(ObjectiveDefinition objective, float progress01, ulong instigatorClientId)
    {
        return ReportObjectiveProgressNormalizedServerOnly(objective, progress01, instigatorClientId);
    }

    public bool ReportObjectiveProgressAmountServerOnly(ObjectiveDefinition objective, float progressAmount, ulong instigatorClientId)
    {
        if (!TryGetActiveObjective(objective, out ObjectiveNetworkState state, out ObjectiveDefinition active))
        {
            return false;
        }

        float normalizedProgress = Mathf.Clamp01(progressAmount / active.RequiredProgress);
        return ApplyObjectiveProgressServerOnly(state, active, normalizedProgress, instigatorClientId, "Objective progress amount reported");
    }

    public bool ReportObjectiveProgressNormalizedServerOnly(ObjectiveDefinition objective, float progress01, ulong instigatorClientId)
    {
        if (!TryGetActiveObjective(objective, out ObjectiveNetworkState state, out ObjectiveDefinition active))
        {
            return false;
        }

        float normalizedProgress = Mathf.Clamp01(progress01);
        return ApplyObjectiveProgressServerOnly(state, active, normalizedProgress, instigatorClientId, "Objective normalized progress reported");
    }

    public bool CompleteObjectiveServerOnly(ObjectiveDefinition objective, ulong instigatorClientId)
    {
        if (!TryGetActiveObjective(objective, out ObjectiveNetworkState state, out ObjectiveDefinition active))
        {
            return false;
        }

        return CompleteObjectiveServerOnly(state, active, instigatorClientId);
    }

    public bool FailObjectiveServerOnly(ObjectiveDefinition objective, ulong instigatorClientId)
    {
        if (!TryGetActiveObjective(objective, out ObjectiveNetworkState state, out ObjectiveDefinition active))
        {
            return false;
        }

        return ResolveObjectiveServerOnly(
            state,
            active,
            ObjectiveRuntimeState.Failed,
            instigatorClientId);
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

        if (normalizedProgress >= 1f)
        {
            return CompleteObjectiveServerOnly(
                state,
                objective,
                instigatorClientId);
        }

        state.Progress01 = normalizedProgress;
        currentObjective.Value = state;
        lastObjectiveReason.Value = reason;

        return true;
    }

    private bool CompleteObjectiveServerOnly(
        ObjectiveNetworkState state,
        ObjectiveDefinition objective,
        ulong instigatorClientId)
    {
        return ResolveObjectiveServerOnly(
            state,
            objective,
            ObjectiveRuntimeState.Completed,
            instigatorClientId);
    }

    // The sequence decides what the match does about one of its objectives,
    // because it is the sequence that knows which one is last. Losing one ends
    // the match when losing is declared to cost it; otherwise the sequence
    // simply carries on, and running out of objectives is what wins.
    private bool ResolveObjectiveServerOnly(
        ObjectiveNetworkState state,
        ObjectiveDefinition objective,
        ObjectiveRuntimeState resolvedState,
        ulong instigatorClientId)
    {
        bool completed = resolvedState == ObjectiveRuntimeState.Completed;

        string resolutionReason = completed
            ? $"Objective '{objective.name}' completed"
            : $"Objective '{objective.name}' failed";

        state.State = resolvedState;

        if (completed)
        {
            state.Progress01 = 1f;
        }

        int nextIndex = state.SequenceIndex + 1;
        bool sequenceFinished = nextIndex >= activeObjectiveSequence.Count;

        GameResultType matchResult;
        string matchReason;

        if (!completed && activeObjectiveSequence.LosingAnObjectiveEndsMatch)
        {
            matchResult = activeObjectiveSequence.FailureResult;
            matchReason = activeObjectiveSequence.FailureReason;
        }
        else if (sequenceFinished)
        {
            // Nothing left to do. A losable objective lost on the last step
            // still gets here, which is what "losing it costs nothing" means.
            matchResult = activeObjectiveSequence.CompletionResult;
            matchReason = activeObjectiveSequence.CompletionReason;
        }
        else
        {
            matchResult = GameResultType.None;
            matchReason = string.Empty;
        }

        if (matchResult == GameResultType.None)
        {
            if (!TryPrepareObjectiveActivation(
                    nextIndex,
                    out ObjectiveDefinition nextObjective,
                    out ObjectiveSceneBinding nextBinding,
                    out string activationError))
            {
                return FaultObjectiveFlowServer(activationError);
            }

            currentObjective.Value = state;
            lastObjectiveReason.Value = resolutionReason;

            CommitObjectiveActivation(
                nextIndex,
                nextObjective,
                nextBinding,
                resolutionReason);
            return true;
        }

        if (matchCompletionCommitted)
            return false;

        GameResultData result = GameResultData.Create(
            matchResult,
            MatchResultSource.Objective,
            objective.SourceId,
            matchReason,
            instigatorClientId);

        bool matchCompleted;

        try
        {
            matchCompleted = gameFlow.CompleteMatchServerOnly(result, matchReason);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            return FaultObjectiveFlowServer(
                $"{nameof(NetworkObjectiveFlow)} match completion threw for " +
                $"objective '{objective.name}': {exception.Message}");
        }

        if (!matchCompleted)
        {
            string error =
                $"{nameof(NetworkObjectiveFlow)} could not commit match completion " +
                $"for objective '{objective.name}'.";
            Debug.LogError(error, this);
            return FaultObjectiveFlowServer(error);
        }

        matchCompletionCommitted = true;
        currentObjective.Value = state;
        lastObjectiveReason.Value = resolutionReason;
        DeactivateActiveBinding();
        return true;
    }

    private bool TryPrepareObjectiveActivation(
        int index,
        out ObjectiveDefinition objective,
        out ObjectiveSceneBinding binding,
        out string error)
    {
        objective = activeObjectiveSequence.GetObjective(index);
        binding = null;

        if (objective == null)
        {
            error =
                $"{nameof(NetworkObjectiveFlow)} cannot activate objective at " +
                $"index {index}: definition is null.";
            Debug.LogError(error, this);
            return false;
        }

        if (!objective.IsValid(out string validationError))
        {
            error =
                $"{nameof(NetworkObjectiveFlow)} cannot activate objective " +
                $"'{objective.name}': {validationError}";
            Debug.LogError(error, this);
            return false;
        }

        if (objective.RequiresSceneBinding)
        {
            if (!sceneBindingRegistry.TryGetBinding(objective, out binding))
            {
                error =
                    $"{nameof(NetworkObjectiveFlow)} cannot activate objective " +
                    $"'{objective.name}': required scene binding is missing.";
                Debug.LogError(error, this);
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private void CommitObjectiveActivation(
        int index,
        ObjectiveDefinition objective,
        ObjectiveSceneBinding binding,
        string reason)
    {
        DeactivateActiveBinding();

        ObjectiveNetworkState nextState = new ObjectiveNetworkState
        {
            SequenceIndex = index,
            State = ObjectiveRuntimeState.Active,
            Progress01 = 0f
        };

        currentObjective.Value = nextState;
        lastObjectiveReason.Value = GetReason(reason, $"Objective '{objective.name}' activated");

        activeBinding = binding;

        if (activeBinding != null)
        {
            activeBinding.SetActiveState(true);
        }
    }

    private bool TryGetActiveObjective(
        ObjectiveDefinition requested,
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

        objective = activeObjectiveSequence.GetObjective(state.SequenceIndex);

        if (objective != null && requested != null && requested != objective)
        {
            Debug.LogError(
                $"{nameof(NetworkObjectiveFlow)} rejected an update for objective " +
                $"'{requested.name}'. The active one is '{objective.name}'.",
                this);

            objective = null;
            return false;
        }

        if (objective == null)
        {
            Debug.LogError(
                $"{nameof(NetworkObjectiveFlow)} cannot update objective at index {state.SequenceIndex}: definition is null.",
                this);

            return false;
        }

        return true;
    }

    private bool ValidateStaticSetup()
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

        return true;
    }

    private bool ValidateRuntimeSetup()
    {
        if (activeObjectiveSequence == null)
        {
            Debug.LogError($"{nameof(NetworkObjectiveFlow)} has no active objective sequence.", this);
            return false;
        }

        if (!activeObjectiveSequence.IsValid(out string sequenceError))
        {
            Debug.LogError($"{nameof(NetworkObjectiveFlow)} has invalid objective sequence: {sequenceError}", this);
            return false;
        }

        if (sceneBindingRegistry == null)
        {
            string mapName = gameMapService != null && gameMapService.ActiveMap != null
                ? gameMapService.ActiveMap.DisplayName
                : "unknown";

            Debug.LogError(
                $"{nameof(NetworkObjectiveFlow)} cannot initialize map '{mapName}': " +
                $"{nameof(GameMapRoot)} has no assigned {nameof(ObjectiveSceneBindingRegistry)}.",
                this);
            return false;
        }

        if (!sceneBindingRegistry.IsValidForSequence(activeObjectiveSequence, out string bindingError))
        {
            Debug.LogError($"{nameof(NetworkObjectiveFlow)} has invalid scene bindings: {bindingError}", this);
            return false;
        }

        return true;
    }

    private void ResolveMapDependencies()
    {
        activeObjectiveSequence = objectiveSequence;

        if (gameMapService == null)
            return;

        GameMapDefinition map = gameMapService.ActiveMap;

        if (map != null && map.ObjectiveSequenceOverride != null)
            activeObjectiveSequence = map.ObjectiveSequenceOverride;

        ObjectiveSceneBindingRegistry mapBindings =
            gameMapService.ActiveMapRoot != null
                ? gameMapService.ActiveMapRoot.ObjectiveBindingRegistry
                : null;

        sceneBindingRegistry = mapBindings;
    }

    private void ResolveGameMapService()
    {
        NetworkObjectServiceContext.TryResolveSessionService(
            NetworkManager,
            out gameMapService);
    }

    private void SubscribeToGameMap()
    {
        if (subscribedToGameMap || gameMapService == null)
            return;

        gameMapService.MapReady += HandleGameMapReady;
        subscribedToGameMap = true;
    }

    private void UnsubscribeFromGameMap()
    {
        if (!subscribedToGameMap || gameMapService == null)
            return;

        gameMapService.MapReady -= HandleGameMapReady;
        subscribedToGameMap = false;
    }

    private void HandleGameMapReady()
    {
        UnsubscribeFromGameMap();
        InitializeServer();
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

    // Configuration or invariant error - not a lost objective. This tears the
    // session down; gameplay failure goes through FailObjectiveServerOnly.
    private bool FaultObjectiveFlowServer(string details)
    {
        if (!IsServer || faultReported)
            return false;

        faultReported = true;
        serverReady = false;

        ObjectiveNetworkState faultedState = currentObjective.Value;
        faultedState.State = ObjectiveRuntimeState.Faulted;
        currentObjective.Value = faultedState;

        string faultDetails = string.IsNullOrWhiteSpace(details)
            ? "Objective flow faulted."
            : details;
        lastObjectiveReason.Value = faultDetails;

        DeactivateActiveBinding();
        sceneBindingRegistry?.UnbindAll();

        _ = NetworkObjectServiceContext.ReportSessionReadinessFailureAsync(
            this,
            faultDetails);
        return false;
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
