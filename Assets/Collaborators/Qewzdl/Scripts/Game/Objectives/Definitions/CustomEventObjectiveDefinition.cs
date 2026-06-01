using UnityEngine;

[CreateAssetMenu(
    fileName = "CustomEventObjectiveDefinition",
    menuName = "Wherever I Am/Objectives/Custom Event Objective Definition")]
public sealed class CustomEventObjectiveDefinition : ObjectiveDefinition
{
    [Header("Event")]
    [SerializeField] private string eventId = "objective.completed";

    public string EventId => eventId;
}
