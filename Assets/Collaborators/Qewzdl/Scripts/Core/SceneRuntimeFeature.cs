using System;
using UnityEngine;

public abstract class SceneRuntimeFeature : MonoBehaviour
{
    public bool Install(ProjectContext context)
    {
        if (context == null)
        {
            Debug.LogError($"{GetType().Name} cannot install because {nameof(ProjectContext)} is missing.", this);
            return false;
        }

        try
        {
            return InstallFeature(context);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            return false;
        }
    }

    protected abstract bool InstallFeature(ProjectContext context);

    protected bool RequireReference(UnityEngine.Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        LogMissingReference(fieldName);
        return false;
    }

    protected bool RequireService<T>(T service, string serviceName)
        where T : class
    {
        if (service != null)
            return true;

        Debug.LogError($"{GetType().Name} is missing required service '{serviceName}'.", this);
        return false;
    }

    protected void LogMissingReference(string fieldName)
    {
        Debug.LogError($"{GetType().Name} is missing '{fieldName}'.", this);
    }

    protected static T RequireInterface<T>(MonoBehaviour behaviour, UnityEngine.Object owner, string fieldName)
        where T : class
    {
        string ownerName = owner != null
            ? owner.GetType().Name
            : nameof(SceneRuntimeFeature);

        if (behaviour is T service)
            return service;

        if (behaviour == null)
        {
            Debug.LogError($"{ownerName} is missing '{fieldName}'.", owner);
            return null;
        }

        Debug.LogError(
            $"{ownerName} field '{fieldName}' must implement {typeof(T).Name}.",
            owner);

        return null;
    }
}