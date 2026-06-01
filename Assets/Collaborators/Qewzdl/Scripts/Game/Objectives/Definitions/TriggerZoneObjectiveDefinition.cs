using UnityEngine;

[CreateAssetMenu(
    fileName = "TriggerZoneObjectiveDefinition",
    menuName = "Wherever I Am/Objectives/Trigger Zone Objective Definition")]
public sealed class TriggerZoneObjectiveDefinition : ObjectiveDefinition
{
    [Header("Zone")]
    [SerializeField] private string requiredTag = "Player";
    [SerializeField] private bool countUniqueClients = true;

    public string RequiredTag => requiredTag;
    public bool CountUniqueClients => countUniqueClients;
}
