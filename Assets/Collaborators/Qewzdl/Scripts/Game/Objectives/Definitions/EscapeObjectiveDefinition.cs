using UnityEngine;

[CreateAssetMenu(
    fileName = "EscapeObjectiveDefinition",
    menuName = "Wherever I Am/Objectives/Escape Objective Definition")]
public sealed class EscapeObjectiveDefinition : ObjectiveDefinition
{
    [Header("Escape")]
    [SerializeField] private EscapeObjectiveMode escapeMode = EscapeObjectiveMode.AnyPlayerEscapes;
    [SerializeField] private string requiredTag = "Player";
    [SerializeField] private bool disableColliderAfterCompletion = true;

    public EscapeObjectiveMode EscapeMode => escapeMode;
    public string RequiredTag => requiredTag;
    public bool DisableColliderAfterCompletion => disableColliderAfterCompletion;
}
