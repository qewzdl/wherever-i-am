using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GameFlowRuntimeDebugPanelSource : RuntimeDebugPanelSource
{
    [Header("GameFlow")]
    [SerializeField] private NetworkGameFlow gameFlow;
    [SerializeField] private NetworkObjectiveFlow objectiveFlow;

    private bool isSubscribed;
    private string lastGameFlowTransition = "None";
    private string lastObjectiveTransition = "None";

    public override string PanelTitle => "GameFlow";

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (RuntimeDebugBuildGuard.DestroyIfDisabled(this))
        {
            return;
        }

        Subscribe();
        RequestRefresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
        RequestRefresh();
    }

    protected override bool ValidateSource(out string error)
    {
        if (gameFlow == null)
        {
            error = $"{nameof(NetworkGameFlow)} is not assigned.";
            return false;
        }

        if (objectiveFlow == null)
        {
            error = $"{nameof(NetworkObjectiveFlow)} is not assigned.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    protected override void BuildPanel(RuntimeDebugTextBuilder builder)
    {
        ObjectiveNetworkState objectiveState = objectiveFlow.CurrentObjective;
        GameResultData resultData = gameFlow.CurrentResult;

        builder
            .Row("Role", GetNetworkRole())
            .Row("GameFlow Spawned", ToYesNo(gameFlow.IsSpawned))
            .Row("ObjectiveFlow Spawned", ToYesNo(objectiveFlow.IsSpawned))
            .Row("Local Client Id", GetLocalClientId())
            .Row("Authority", gameFlow.IsServer ? "Server writer" : "Replicated reader")
            .Row("Phase", gameFlow.CurrentPhase)
            .Row("Is Match Running", ToYesNo(gameFlow.IsMatchRunning))
            .Row("Is Match Finished", ToYesNo(gameFlow.IsMatchFinished))
            .Row("Last GameFlow Transition", lastGameFlowTransition)
            .Row("GameFlow Reason", gameFlow.LastTransitionReason)
            .Row("Sequence Index", objectiveState.SequenceIndex)
            .Row("Objective State", objectiveState.State)
            .Row("Objective Progress", FormatProgress(objectiveState.Progress01))
            .Row("Has Active Objective", ToYesNo(objectiveFlow.HasActiveObjective))
            .Row("Last Objective Transition", lastObjectiveTransition)
            .Row("Objective Reason", objectiveFlow.LastObjectiveReason)
            .Row("Has Result", ToYesNo(resultData.HasResult))
            .Row("Result Type", resultData.ResultType)
            .Row("Source", resultData.Source)
            .Row("Source Id", ToDisplayText(resultData.SourceId.ToString()))
            .Row("Reason", ToDisplayText(resultData.Reason.ToString()))
            .Row("Instigator Client Id", resultData.InstigatorClientId);
    }

    private void Subscribe()
    {
        if (isSubscribed || gameFlow == null || objectiveFlow == null)
        {
            return;
        }

        gameFlow.PhaseChanged += HandlePhaseChanged;
        gameFlow.ResultChanged += HandleResultChanged;
        gameFlow.TransitionReasonChanged += HandleTransitionReasonChanged;
        objectiveFlow.ObjectiveStateChanged += HandleObjectiveStateChanged;
        objectiveFlow.ObjectiveReasonChanged += HandleObjectiveReasonChanged;

        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!isSubscribed)
        {
            return;
        }

        if (gameFlow != null)
        {
            gameFlow.PhaseChanged -= HandlePhaseChanged;
            gameFlow.ResultChanged -= HandleResultChanged;
            gameFlow.TransitionReasonChanged -= HandleTransitionReasonChanged;
        }

        if (objectiveFlow != null)
        {
            objectiveFlow.ObjectiveStateChanged -= HandleObjectiveStateChanged;
            objectiveFlow.ObjectiveReasonChanged -= HandleObjectiveReasonChanged;
        }

        isSubscribed = false;
    }

    private void HandlePhaseChanged(GamePhase previousPhase, GamePhase newPhase)
    {
        lastGameFlowTransition = $"{previousPhase} -> {newPhase}";
        RequestRefresh();
    }

    private void HandleResultChanged(GameResultData previousResult, GameResultData newResult)
    {
        if (newResult.HasResult)
        {
            lastGameFlowTransition = $"Result: {newResult.ResultType} / {newResult.Source}";
        }
        else
        {
            lastGameFlowTransition = "Result cleared";
        }

        RequestRefresh();
    }

    private void HandleTransitionReasonChanged(Unity.Collections.FixedString128Bytes previousReason, Unity.Collections.FixedString128Bytes newReason)
    {
        RequestRefresh();
    }

    private void HandleObjectiveStateChanged(ObjectiveNetworkState previousState, ObjectiveNetworkState newState)
    {
        string previousIndex = previousState.SequenceIndex.ToString();
        string newIndex = newState.SequenceIndex.ToString();

        lastObjectiveTransition = $"{previousIndex} [{previousState.State}] -> {newIndex} [{newState.State}]";

        RequestRefresh();
    }

    private void HandleObjectiveReasonChanged(Unity.Collections.FixedString128Bytes previousReason, Unity.Collections.FixedString128Bytes newReason)
    {
        RequestRefresh();
    }

    private string GetNetworkRole()
    {
        if (!gameFlow.IsSpawned)
        {
            return "Not Spawned";
        }

        if (gameFlow.IsHost)
        {
            return "Host";
        }

        if (gameFlow.IsServer)
        {
            return "Server";
        }

        if (gameFlow.IsClient)
        {
            return "Client";
        }

        return "Offline";
    }

    private string GetLocalClientId()
    {
        NetworkManager networkManager = gameFlow.NetworkManager;

        if (networkManager == null)
        {
            return "None";
        }

        return networkManager.LocalClientId.ToString();
    }

    private string FormatProgress(float progress01)
    {
        float clampedProgress = Mathf.Clamp01(progress01);
        int percent = Mathf.RoundToInt(clampedProgress * 100f);
        return $"{clampedProgress:0.00} / {percent}%";
    }

    private string ToYesNo(bool value)
    {
        return value ? "Yes" : "No";
    }

    private string ToDisplayText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "None" : value;
    }
}
