using System;
using UnityEngine;

public abstract class SceneRuntimeFeature : MonoBehaviour, IDisposable
{
    private SceneFeatureContext installedContext;
    private bool installed;
    private bool lifecycleTransitionInProgress;

    public bool IsInstalled => installed;

    protected virtual void OnDestroy()
    {
        if (!installed)
            return;

        SceneFeatureContext context = installedContext;

        if (context == null || !context.RequestScopeUninstall())
            Uninstall();
    }

    public bool Validate(SceneFeatureContext context)
    {
        if (!CanUseContext(context))
            return false;

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

    public bool Install(SceneFeatureContext context)
    {
        return InstallInternal(context, true);
    }

    internal bool InstallValidated(SceneFeatureContext context)
    {
        return InstallInternal(context, false);
    }

    private bool InstallInternal(
        SceneFeatureContext context,
        bool validateBeforeInstall)
    {
        if (installed)
        {
            if (ReferenceEquals(installedContext, context))
                return true;

            Debug.LogError(
                $"{GetType().Name} is already installed with another {nameof(SceneFeatureContext)}.",
                this);

            return false;
        }

        if (validateBeforeInstall)
        {
            if (!Validate(context))
                return false;
        }
        else
        {
            if (!CanUseContext(context))
                return false;

            if (lifecycleTransitionInProgress)
            {
                Debug.LogError(
                    $"{GetType().Name} cannot install during another lifecycle transition.",
                    this);

                return false;
            }
        }

        lifecycleTransitionInProgress = true;

        try
        {
            if (!InstallFeature(context))
            {
                RollbackFailedInstall(context);
                return false;
            }

            installedContext = context;
            installed = true;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            RollbackFailedInstall(context);
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
        SceneFeatureContext context = installedContext;
        installed = false;

        try
        {
            UninstallFeature(context);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
        finally
        {
            installedContext = null;
            lifecycleTransitionInProgress = false;
        }
    }

    public void Dispose()
    {
        Uninstall();
    }

    protected abstract bool ValidateFeature(SceneFeatureContext context);
    protected abstract bool InstallFeature(SceneFeatureContext context);

    protected virtual void UninstallFeature(SceneFeatureContext context)
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

    protected bool RequireService<T>(
        SceneFeatureContext context,
        out T service,
        string serviceName = null)
        where T : class
    {
        service = null;

        if (context != null &&
            context.Services != null &&
            !context.Services.IsDisposed &&
            context.Services.TryResolve(out service))
        {
            return true;
        }

        string resolvedName = string.IsNullOrWhiteSpace(serviceName)
            ? typeof(T).Name
            : serviceName;

        Debug.LogError($"{GetType().Name} is missing required service '{resolvedName}'.", this);
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

    private bool CanUseContext(SceneFeatureContext context)
    {
        if (context == null)
        {
            Debug.LogError($"{GetType().Name} cannot run without {nameof(SceneFeatureContext)}.", this);
            return false;
        }

        if (context.Services != null && !context.Services.IsDisposed)
            return true;

        Debug.LogError($"{GetType().Name} cannot run with an inactive scene resolver.", this);
        return false;
    }

    private void RollbackFailedInstall(SceneFeatureContext context)
    {
        installed = false;
        installedContext = null;

        try
        {
            UninstallFeature(context);
        }
        catch (Exception rollbackException)
        {
            Debug.LogException(rollbackException, this);
        }
    }
}
