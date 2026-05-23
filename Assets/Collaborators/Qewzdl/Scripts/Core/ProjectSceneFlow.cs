using UnityEngine;

[CreateAssetMenu(menuName = "Wherever I Am/Scene Flow", fileName = "ProjectSceneFlow")]
public sealed class ProjectSceneFlow : ScriptableObject
{
    [SerializeField] private ProjectSceneTransitionDefinition[] transitions;

    public bool TryGetTransition(
        ProjectSceneKind currentScene,
        ProjectSceneKind targetScene,
        out ProjectSceneTransitionDefinition transition)
    {
        if (transitions != null)
        {
            for (int i = 0; i < transitions.Length; i++)
            {
                if (!transitions[i].IsExactMatch(currentScene, targetScene))
                    continue;

                transition = transitions[i];
                return true;
            }

            for (int i = 0; i < transitions.Length; i++)
            {
                if (!transitions[i].IsWildcardMatch(targetScene))
                    continue;

                transition = transitions[i];
                return true;
            }
        }

        transition = default;
        return false;
    }
}