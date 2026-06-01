using UnityEngine;

public sealed class ObjectiveCompletionRouter
{
    private NetworkGameFlow gameFlow;

    public bool Initialize(NetworkGameFlow networkGameFlow, Object logContext)
    {
        if (networkGameFlow == null)
        {
            Debug.LogError($"{nameof(ObjectiveCompletionRouter)} requires {nameof(NetworkGameFlow)} reference.", logContext);
            return false;
        }

        gameFlow = networkGameFlow;
        return true;
    }

    public void RouteCompletedObjectiveServerOnly(
        ObjectiveCondition objective,
        ulong instigatorClientId,
        Object logContext)
    {
        RouteTerminalObjectiveServerOnly(objective, instigatorClientId, logContext);
    }

    public void RouteFailedObjectiveServerOnly(
        ObjectiveCondition objective,
        ulong instigatorClientId,
        Object logContext)
    {
        RouteTerminalObjectiveServerOnly(objective, instigatorClientId, logContext);
    }

    private void RouteTerminalObjectiveServerOnly(
        ObjectiveCondition objective,
        ulong instigatorClientId,
        Object logContext)
    {
        if (objective == null)
        {
            Debug.LogError($"{nameof(ObjectiveCompletionRouter)} received null terminal objective.", logContext);
            return;
        }

        if (!objective.CompletesGame)
        {
            return;
        }

        if (gameFlow == null)
        {
            Debug.LogError($"{nameof(ObjectiveCompletionRouter)} is not initialized.", logContext);
            return;
        }

        gameFlow.FinishGameServerOnly(
            objective.ResultType,
            objective.CompletionReason,
            objective.ObjectiveId,
            instigatorClientId);
    }
}