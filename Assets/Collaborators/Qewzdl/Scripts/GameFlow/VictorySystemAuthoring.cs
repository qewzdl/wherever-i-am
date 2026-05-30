using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class VictorySystemAuthoring : MonoBehaviour
{
    [Header("Victory Setup")]
    [SerializeField] private EscapeVictoryMode victoryMode = EscapeVictoryMode.AnyPlayerEscapes;
    [SerializeField] private EscapePointAuthoring escapePoint;
    [SerializeField] private List<VictoryObjectiveAuthoring> objectives = new();

    [Header("Generated Runtime")]
    [SerializeField] private NetworkGameOutcome runtimeOutcome;
    [SerializeField] private Transform objectivesRoot;
    [SerializeField] private Transform escapeRoot;

    public EscapeVictoryMode VictoryMode => victoryMode;
    public EscapePointAuthoring EscapePoint => escapePoint;
    public IReadOnlyList<VictoryObjectiveAuthoring> Objectives => objectives;
    public NetworkGameOutcome RuntimeOutcome => runtimeOutcome;
    public Transform ObjectivesRoot => objectivesRoot;
    public Transform EscapeRoot => escapeRoot;

    private void OnValidate()
    {
        RemoveNullObjectives();
        RemoveDuplicateObjectives();
    }

    private void RemoveNullObjectives()
    {
        for (int i = objectives.Count - 1; i >= 0; i--)
        {
            if (objectives[i] == null)
                objectives.RemoveAt(i);
        }
    }

    private void RemoveDuplicateObjectives()
    {
        HashSet<VictoryObjectiveAuthoring> uniqueObjectives = new();

        for (int i = objectives.Count - 1; i >= 0; i--)
        {
            VictoryObjectiveAuthoring objective = objectives[i];

            if (!uniqueObjectives.Add(objective))
                objectives.RemoveAt(i);
        }
    }
}