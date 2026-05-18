using UnityEngine;

public abstract class SceneRuntimeFeature : MonoBehaviour
{
    public abstract void Install(ProjectContext context);

    protected static T RequireInterface<T>(MonoBehaviour behaviour, Object owner, string fieldName)
        where T : class
    {
        if (behaviour is T service)
            return service;

        if (behaviour == null)
        {
            Debug.LogError($"{owner.GetType().Name} is missing '{fieldName}'.", owner);
            return null;
        }

        Debug.LogError(
            $"{owner.GetType().Name} field '{fieldName}' must implement {typeof(T).Name}.",
            owner);

        return null;
    }
}
