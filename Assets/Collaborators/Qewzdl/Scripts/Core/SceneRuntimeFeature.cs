using System;
using UnityEngine;

public abstract class SceneRuntimeFeature : MonoBehaviour, IDisposable
{
    private ProjectContext installedContext;
    private bool installed;
    private bool lifecycleTransitionInProgress;

    public bool IsInstalled => installed;

    protected virtual void OnDestroy()
    {
        if (!installed)
            return;

        AppRuntime runtime = AppRuntime.Instance;

        if (runtime != null)
            runtime.UninstallSceneScope(gameObject.scene.handle);
    }

    public bool Validate(ProjectContext context)
    {
        if (context == null)
        {
            Debug.LogError($"{GetType().Name} cannot validate because {nameof(ProjectContext)} is missing.", this);
            return false;
        }

        if (lifecycleTransitionInProgress)
        {
            Debug.LogError($"{GetType().Name} cannot validate during another lifecycle transition.", this);
            return false;
        }

        try
        {
            return ValidateFeature(context);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            return false;
        }
    }

    public bool Install(ProjectContext context)
    {
        if (installed)
        {
            if (installedContext == context)
                return true;

            Debug.LogError(
                $"{GetType().Name} is already installed with another {nameof(ProjectContext)}.",
                this);

            return false;
        }

        if (!Validate(context))
            return false;

        lifecycleTransitionInProgress = true;

        try
        {
            if (!InstallFeature(context))
            {
                RollbackFailedInstall();
                return false;
            }

            installedContext = context;
            installed = true;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            RollbackFailedInstall();
            return false;
        }
        finally
        {
            lifecycleTransitionInProgress = false;
        }
    }

    public void Uninstall()
    {
        if (!installed)
            return;

        if (lifecycleTransitionInProgress)
        {
            Debug.LogError($"{GetType().Name} cannot uninstall during another lifecycle transition.", this);
            return;
        }

        lifecycleTransitionInProgress = true;
        installed = false;
        installedContext = null;

        try
        {
            UninstallFeature();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
        finally
        {
            lifecycleTransitionInProgress = false;
        }
    }

    public void Dispose()
    {
        Uninstall();
    }

    protected abstract bool ValidateFeature(ProjectContext context);
    protected abstract bool InstallFeature(ProjectContext context);

    protected virtual void UninstallFeature()
    {
    }

    protected void RunCleanup(Action cleanup, UnityEngine.Object owner = null)
    {
        if (cleanup == null)
            return;

        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, owner != null ? owner : this);
        }
    }

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

    private void RollbackFailedInstall()
    {
        installed = false;
        installedContext = null;

        try
        {
            UninstallFeature();
        }
        catch (Exception rollbackException)
        {
            Debug.LogException(rollbackException, this);
        }
    }
}
