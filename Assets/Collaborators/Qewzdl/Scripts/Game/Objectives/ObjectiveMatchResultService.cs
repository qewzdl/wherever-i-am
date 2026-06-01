using UnityEngine;

public sealed class ObjectiveMatchResultService
{
    private NetworkGameFlow gameFlow;

    public bool Initialize(
        NetworkGameFlow networkGameFlow,
        ObjectiveCondition[] objectives,
        Object logContext)
    {
        if (networkGameFlow == null)
        {
            Debug.LogError($"{nameof(ObjectiveMatchResultService)} requires {nameof(NetworkGameFlow)} reference.", logContext);
            return false;
        }

        if (!ValidateMatchResultDefinitions(objectives, logContext))
        {
            return false;
        }

        gameFlow = networkGameFlow;
        return true;
    }

    public void HandleObjectiveOutcomeServerOnly(ObjectiveOutcome objectiveOutcome, Object logContext)
    {
        if (!TryCreateMatchOutcomeServerOnly(objectiveOutcome, out MatchOutcome matchOutcome, logContext))
        {
            return;
        }

        if (gameFlow == null)
        {
            Debug.LogError($"{nameof(ObjectiveMatchResultService)} is not initialized.", logContext);
            return;
        }

        gameFlow.CompleteMatchServerOnly(matchOutcome.ToGameResultData());
    }

    private bool TryCreateMatchOutcomeServerOnly(
        ObjectiveOutcome objectiveOutcome,
        out MatchOutcome matchOutcome,
        Object logContext)
    {
        matchOutcome = default;

        if (!objectiveOutcome.IsLifecycleTerminal)
        {
            Debug.LogError($"{nameof(ObjectiveMatchResultService)} received non-terminal objective outcome: {objectiveOutcome.State}.", logContext);
            return false;
        }

        if (!objectiveOutcome.CanBeEvaluatedForMatch)
        {
            return false;
        }

        ObjectiveCondition objective = objectiveOutcome.Objective;

        if (objective == null)
        {
            Debug.LogError($"{nameof(ObjectiveMatchResultService)} received null objective outcome.", logContext);
            return false;
        }

        ObjectiveDefinition definition = objective.Definition;

        if (definition == null)
        {
            Debug.LogError($"{objective.GetType().Name} requires assigned {nameof(ObjectiveDefinition)}.", objective);
            return false;
        }

        if (!definition.CompletesGame)
        {
            return false;
        }

        if (definition.ResultType == GameResultType.None)
        {
            Debug.LogError($"{definition.name} completes match but has invalid result type.", definition);
            return false;
        }

        matchOutcome = MatchOutcomeFactory.FromObjective(
            definition,
            objectiveOutcome.InstigatorClientId);

        if (!matchOutcome.HasResult)
        {
            Debug.LogError($"{nameof(MatchOutcomeFactory)} failed to create objective match outcome.", definition);
            return false;
        }

        return true;
    }

    private bool ValidateMatchResultDefinitions(ObjectiveCondition[] objectives, Object logContext)
    {
        if (objectives == null || objectives.Length == 0)
        {
            Debug.LogError($"{nameof(ObjectiveMatchResultService)} requires objective list.", logContext);
            return false;
        }

        for (int i = 0; i < objectives.Length; i++)
        {
            ObjectiveCondition objective = objectives[i];

            if (objective == null)
            {
                Debug.LogError($"{nameof(ObjectiveMatchResultService)} has null objective at index {i}.", logContext);
                return false;
            }

            ObjectiveDefinition definition = objective.Definition;

            if (definition == null)
            {
                Debug.LogError($"{objective.GetType().Name} at index {i} requires assigned {nameof(ObjectiveDefinition)}.", objective);
                return false;
            }

            if (definition.CompletesGame && definition.ResultType == GameResultType.None)
            {
                Debug.LogError($"{definition.name} completes match but has invalid result type.", definition);
                return false;
            }
        }

        return true;
    }
}