using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GameFlowRuntimeDebugPanelSource : RuntimeDebugPanelSource
{
    [Header("GameFlow")]
    [SerializeField] private NetworkGameFlow gameFlow;
    [SerializeField] private NetworkObjectiveFlow objectiveFlow;

    private bool isSubscribed;
    private string lastTransitionReason = "No replicated transition observed yet.";
    private string lastGameFlowTransition = "None";
    private string lastObjectiveTransition = "None";

    public override string PanelTitle => "GameFlow";

    private void OnEnable()
    {
        if (!Application.isPlaying)
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
            .Row("Objective Id", GetObjectiveId(objectiveState))
            .Row("Sequence Index", objectiveState.SequenceIndex)
            .Row("Objective State", objectiveState.State)
            .Row("Objective Progress", FormatProgress(objectiveState.Progress01))
            .Row("Has Active Objective", ToYesNo(objectiveFlow.HasActiveObjective))
            .Row("Last Objective Transition", lastObjectiveTransition)
            .Row("Has Result", ToYesNo(resultData.HasResult))
            .Row("Result Type", resultData.ResultType)
            .Row("Source", resultData.Source)
            .Row("Source Id", ToDisplayText(resultData.SourceId.ToString()))
            .Row("Reason", ToDisplayText(resultData.Reason.ToString()))
            .Row("Instigator Client Id", resultData.InstigatorClientId)
            .Row("Last Transition Reason", ToDisplayText(lastTransitionReason));
    }

    private void Subscribe()
    {
        if (isSubscribed || gameFlow == null || objectiveFlow == null)
        {
            return;
        }

        gameFlow.PhaseChanged += HandlePhaseChanged;
        gameFlow.ResultChanged += HandleResultChanged;
        objectiveFlow.ObjectiveStateChanged += HandleObjectiveStateChanged;

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
        }

        if (objectiveFlow != null)
        {
            objectiveFlow.ObjectiveStateChanged -= HandleObjectiveStateChanged;
        }

        isSubscribed = false;
    }

    private void HandlePhaseChanged(GamePhase previousPhase, GamePhase newPhase)
    {
        lastGameFlowTransition = $"{previousPhase} -> {newPhase}";
        lastTransitionReason = $"GameFlow phase changed: {previousPhase} -> {newPhase}";
        RequestRefresh();
    }

    private void HandleResultChanged(GameResultData previousResult, GameResultData newResult)
    {
        if (newResult.HasResult)
        {
            lastGameFlowTransition = $"Result: {newResult.ResultType} / {newResult.Source}";
            lastTransitionReason = newResult.Reason.ToString();
        }
        else
        {
            lastGameFlowTransition = "Result cleared";
            lastTransitionReason = "Game result cleared";
        }

        RequestRefresh();
    }

    private void HandleObjectiveStateChanged(ObjectiveNetworkState previousState, ObjectiveNetworkState newState)
    {
        string previousObjectiveId = GetObjectiveId(previousState);
        string newObjectiveId = GetObjectiveId(newState);

        lastObjectiveTransition = $"{previousObjectiveId} [{previousState.State}] -> {newObjectiveId} [{newState.State}]";
        lastTransitionReason = $"Objective state changed: {lastObjectiveTransition}";

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

    private string GetObjectiveId(ObjectiveNetworkState objectiveState)
    {
        return ToDisplayText(objectiveState.ObjectiveId.ToString());
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