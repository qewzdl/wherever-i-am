using System.Collections.Generic;
using UnityEngine;

public sealed class ObjectiveRuntimeService
{
    private readonly HashSet<string> objectiveIds = new HashSet<string>();

    private ObjectiveManager manager;
    private GameplayEventHub gameplayEventHub;
    private ObjectiveCondition[] objectives;

    public bool Initialize(
        ObjectiveManager objectiveManager,
        GameplayEventHub eventHub,
        ObjectiveCondition[] objectiveList)
    {
        if (objectiveManager == null)
        {
            Debug.LogError($"{nameof(ObjectiveRuntimeService)} requires {nameof(ObjectiveManager)}.");
            return false;
        }

        if (objectiveList == null || objectiveList.Length == 0)
        {
            Debug.LogError($"{nameof(ObjectiveManager)} requires at least one objective.", objectiveManager);
            return false;
        }

        if (!ValidateObjectives(objectiveManager, eventHub, objectiveList))
        {
            return false;
        }

        manager = objectiveManager;
        gameplayEventHub = eventHub;
        objectives = objectiveList;

        return true;
    }

    public void InitializeObjectivesServerOnly(ObjectiveProgressSync progressSync)
    {
        if (manager == null || objectives == null)
        {
            Debug.LogError($"{nameof(ObjectiveRuntimeService)} is not initialized.");
            return;
        }

        if (progressSync == null)
        {
            Debug.LogError($"{nameof(ObjectiveRuntimeService)} requires {nameof(ObjectiveProgressSync)}.", manager);
            return;
        }

        progressSync.ClearServerOnly();

        for (int i = 0; i < objectives.Length; i++)
        {
            ObjectiveCondition objective = objectives[i];

            objective.Initialize(manager, gameplayEventHub);
            progressSync.UpsertObjectiveServerOnly(objective);
        }
    }

    public void StartObjectivesServerOnly()
    {
        if (objectives == null)
        {
            Debug.LogError($"{nameof(ObjectiveRuntimeService)} cannot start objectives before initialization.");
            return;
        }

        for (int i = 0; i < objectives.Length; i++)
        {
            objectives[i].StartObjectiveServerOnly();
        }
    }

    public void CancelObjectivesServerOnly()
    {
        if (objectives == null)
        {
            return;
        }

        for (int i = 0; i < objectives.Length; i++)
        {
            if (objectives[i] != null)
            {
                objectives[i].CancelObjectiveServerOnly();
            }
        }
    }

    private bool ValidateObjectives(
        Object logContext,
        GameplayEventHub eventHub,
        ObjectiveCondition[] objectiveList)
    {
        objectiveIds.Clear();

        for (int i = 0; i < objectiveList.Length; i++)
        {
            ObjectiveCondition objective = objectiveList[i];

            if (objective == null)
            {
                Debug.LogError($"{nameof(ObjectiveManager)} has null objective at index {i}.", logContext);
                return false;
            }

            ObjectiveDefinition definition = objective.Definition;

            if (definition == null)
            {
                Debug.LogError($"{objective.GetType().Name} at index {i} requires assigned {nameof(ObjectiveDefinition)}.", objective);
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.ObjectiveId))
            {
                Debug.LogError($"{definition.name} has empty objective id.", definition);
                return false;
            }

            if (!objectiveIds.Add(definition.ObjectiveId))
            {
                Debug.LogError($"{nameof(ObjectiveManager)} has duplicate objective id: {definition.ObjectiveId}.", definition);
                return false;
            }

            if (objective.RequiresGameplayEventHub && eventHub == null)
            {
                Debug.LogError($"{objective.GetType().Name} requires {nameof(GameplayEventHub)} reference.", objective);
                return false;
            }
        }

        return true;
    }
}