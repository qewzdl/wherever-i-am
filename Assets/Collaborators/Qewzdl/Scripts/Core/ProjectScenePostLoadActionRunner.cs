using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectScenePostLoadActionRunner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;

    [Header("Server Actions")]
    [SerializeField] private MonoBehaviour[] serverActionHandlers;

    public void Construct(NetworkManager manager)
    {
        networkManager = manager;
    }

    public bool Validate(ProjectSceneServerAction[] actions)
    {
        if (actions == null || actions.Length == 0)
            return true;

        if (!HasRequiredReferences())
            return false;

        if (!networkManager.IsServer)
            return true;

        for (int i = 0; i < actions.Length; i++)
        {
            if (!TryGetServerActionHandler(actions[i], out _))
                return false;
        }

        return true;
    }

    public bool Run(ProjectSceneKind loadedScene, ProjectSceneServerAction[] actions)
    {
        if (actions == null || actions.Length == 0)
            return true;

        if (!HasRequiredReferences())
            return false;

        if (!networkManager.IsServer)
            return true;

        for (int i = 0; i < actions.Length; i++)
        {
            if (!TryGetServerActionHandler(actions[i], out IProjectSceneFlowServerActionHandler handler))
                return false;

            handler.Handle(actions[i], loadedScene);
        }

        return true;
    }

    private bool TryGetServerActionHandler(
        ProjectSceneServerAction action,
        out IProjectSceneFlowServerActionHandler handler)
    {
        handler = null;

        if (serverActionHandlers == null || serverActionHandlers.Length == 0)
        {
            Debug.LogError(
                $"{nameof(ProjectScenePostLoadActionRunner)} has no server action handlers assigned for action '{action}'.",
                this);

            return false;
        }

        for (int i = 0; i < serverActionHandlers.Length; i++)
        {
            MonoBehaviour behaviour = serverActionHandlers[i];

            if (behaviour == null)
            {
                Debug.LogError(
                    $"{nameof(ProjectScenePostLoadActionRunner)} has an empty server action handler slot.",
                    this);

                return false;
            }

            if (behaviour is not IProjectSceneFlowServerActionHandler candidate)
            {
                Debug.LogError(
                    $"{behaviour.name} does not implement {nameof(IProjectSceneFlowServerActionHandler)}.",
                    behaviour);

                return false;
            }

            if (!candidate.CanHandle(action))
                continue;

            handler = candidate;
            return true;
        }

        Debug.LogError($"No server action handler found for action '{action}'.", this);
        return false;
    }

    private bool HasRequiredReferences()
    {
        return ValidateRequiredReference(networkManager, nameof(networkManager));
    }

    private bool ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(ProjectScenePostLoadActionRunner)} is missing '{fieldName}'.", this);
        return false;
    }
}