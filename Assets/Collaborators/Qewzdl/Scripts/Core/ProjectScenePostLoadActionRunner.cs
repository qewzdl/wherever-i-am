using System;
using System.Collections.Generic;
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

    public void DisposeComposition()
    {
        networkManager = null;
    }

    internal bool Validate(
        ProjectSceneKind loadedScene,
        ProjectSceneServerAction[] actions,
        out string error)
    {
        error = string.Empty;

        if (actions == null || actions.Length == 0)
            return true;

        if (!HasRequiredReferences(out error))
            return false;

        if (!networkManager.IsServer)
            return true;

        for (int i = 0; i < actions.Length; i++)
        {
            if (!TryGetServerActionHandler(
                    actions[i],
                    out IProjectSceneFlowServerActionHandler handler,
                    out error))
            {
                return false;
            }

            ProjectSceneActionResult validation = InvokeHandler(
                handler,
                () => handler.Validate(actions[i], loadedScene),
                actions[i],
                "validation");

            if (validation.Succeeded)
                continue;

            error = FormatFailure(actions[i], "validation", validation.Error);
            return false;
        }

        return true;
    }

    internal ProjectSceneActionBatch Execute(
        ProjectSceneKind loadedScene,
        ProjectSceneServerAction[] actions)
    {
        ProjectSceneActionBatch batch = new();

        if (!HasRequiredReferences(out string referenceError))
        {
            batch.Fail(referenceError);
            return batch;
        }

        if (!networkManager.IsServer)
            return batch;

        try
        {
            if (actions != null)
            {
                for (int i = 0; i < actions.Length; i++)
                {
                    ProjectSceneServerAction action = actions[i];

                    if (!TryGetServerActionHandler(
                            action,
                            out IProjectSceneFlowServerActionHandler handler,
                            out string handlerError))
                    {
                        batch.Fail(handlerError);
                        return batch;
                    }

                    ProjectSceneActionResult validation = InvokeHandler(
                        handler,
                        () => handler.Validate(action, loadedScene),
                        action,
                        "validation");

                    if (!validation.Succeeded)
                    {
                        batch.Fail(
                            FormatFailure(action, "validation", validation.Error),
                            validation.Exception);
                        return batch;
                    }

                    ProjectSceneActionResult execution = InvokeHandler(
                        handler,
                        () => handler.Execute(action, loadedScene),
                        action,
                        "execution");
                    batch.Add(execution);

                    if (execution.Succeeded)
                        continue;

                    batch.Fail(
                        FormatFailure(action, "execution", execution.Error),
                        execution.Exception);
                    return batch;
                }
            }

            if (!NetworkObjectServiceContext.TryGetSessionServices(
                    networkManager,
                    out IServiceResolver sessionServices))
            {
                batch.Fail(
                    $"Cannot validate dynamic contracts for scene '{loadedScene}' " +
                    "without an active Session resolver.");
                return batch;
            }

            if (!ProjectSceneDynamicContractPolicy.Validate(
                    loadedScene,
                    sessionServices,
                    out string dynamicContractError))
            {
                batch.Fail(dynamicContractError);
            }
        }
        catch (Exception exception)
        {
            batch.Fail(
                $"Unexpected failure while completing post-load actions for scene " +
                $"'{loadedScene}'.",
                exception);
        }

        return batch;
    }

    private bool TryGetServerActionHandler(
        ProjectSceneServerAction action,
        out IProjectSceneFlowServerActionHandler handler,
        out string error)
    {
        handler = null;
        error = string.Empty;

        if (serverActionHandlers == null || serverActionHandlers.Length == 0)
        {
            error = $"No server action handlers are assigned for action '{action}'.";
            return false;
        }

        for (int i = 0; i < serverActionHandlers.Length; i++)
        {
            MonoBehaviour behaviour = serverActionHandlers[i];

            if (behaviour == null)
            {
                error = $"Server action handler slot {i} is empty.";
                return false;
            }

            if (behaviour is not IProjectSceneFlowServerActionHandler candidate)
            {
                error =
                    $"'{behaviour.name}' does not implement " +
                    $"{nameof(IProjectSceneFlowServerActionHandler)}.";
                return false;
            }

            bool canHandle;

            try
            {
                canHandle = candidate.CanHandle(action);
            }
            catch (Exception exception)
            {
                error =
                    $"'{behaviour.name}' threw while checking action '{action}': " +
                    exception.Message;
                return false;
            }

            if (!canHandle)
                continue;

            if (handler != null)
            {
                error =
                    $"Action '{action}' has more than one handler: " +
                    $"'{((MonoBehaviour)handler).name}' and '{behaviour.name}'.";
                return false;
            }

            handler = candidate;
        }

        if (handler != null)
            return true;

        error = $"No server action handler can execute action '{action}'.";
        return false;
    }

    private static ProjectSceneActionResult InvokeHandler(
        IProjectSceneFlowServerActionHandler handler,
        Func<ProjectSceneActionResult> invocation,
        ProjectSceneServerAction action,
        string stage)
    {
        try
        {
            return invocation.Invoke() ?? ProjectSceneActionResult.Failure(
                $"Handler '{handler.GetType().Name}' returned no result.");
        }
        catch (Exception exception)
        {
            return ProjectSceneActionResult.Failure(
                $"Handler '{handler.GetType().Name}' threw during {stage} " +
                $"of action '{action}'.",
                exception);
        }
    }

    private static string FormatFailure(
        ProjectSceneServerAction action,
        string stage,
        string details)
    {
        return $"Server action '{action}' {stage} failed. {details}";
    }

    private bool HasRequiredReferences(out string error)
    {
        if (networkManager != null)
        {
            error = string.Empty;
            return true;
        }

        error = $"{nameof(ProjectScenePostLoadActionRunner)} is missing '{nameof(networkManager)}'.";
        return false;
    }
}

internal sealed class ProjectSceneActionBatch
{
    private readonly List<ProjectSceneActionResult> executedActions = new();
    private bool finalized;

    internal bool Succeeded { get; private set; } = true;
    internal string Error { get; private set; } = string.Empty;
    internal Exception Exception { get; private set; }

    internal void Add(ProjectSceneActionResult result)
    {
        if (result != null)
            executedActions.Add(result);
    }

    internal void Fail(string error, Exception exception = null)
    {
        if (!Succeeded)
            return;

        Succeeded = false;
        Error = string.IsNullOrWhiteSpace(error)
            ? "Project scene post-load action batch failed."
            : error;
        Exception = exception;
    }

    internal void Commit()
    {
        if (finalized)
            return;

        finalized = true;

        for (int i = 0; i < executedActions.Count; i++)
            executedActions[i].Commit();

        executedActions.Clear();
    }

    internal void Rollback()
    {
        if (finalized)
            return;

        finalized = true;
        List<Exception> failures = null;

        for (int i = executedActions.Count - 1; i >= 0; i--)
        {
            try
            {
                executedActions[i].Rollback();
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(exception);
            }
        }

        executedActions.Clear();

        if (failures != null)
        {
            throw new AggregateException(
                "Failed to roll back project scene post-load actions.",
                failures);
        }
    }
}

internal static class ProjectSceneDynamicContractPolicy
{
    internal static bool Validate(
        ProjectSceneKind sceneKind,
        IServiceResolver services,
        out string error)
    {
        if (sceneKind != ProjectSceneKind.Lobby &&
            sceneKind != ProjectSceneKind.Game)
        {
            error = string.Empty;
            return true;
        }

        if (services == null || services.IsDisposed)
        {
            error =
                $"Scene '{sceneKind}' requires an active Session resolver " +
                "for dynamic contract validation.";
            return false;
        }

        List<string> missingContracts = new();

        Require<IChatReadService>(services, missingContracts);
        Require<IChatCommandService>(services, missingContracts);

        if (sceneKind == ProjectSceneKind.Game)
            Require<IMatchCompletionService>(services, missingContracts);

        if (missingContracts.Count == 0)
        {
            error = string.Empty;
            return true;
        }

        error =
            $"Scene '{sceneKind}' is missing required dynamic Session contract(s): " +
            string.Join(", ", missingContracts) + ".";
        return false;
    }

    private static void Require<TContract>(
        IServiceResolver services,
        ICollection<string> missingContracts)
        where TContract : class
    {
        try
        {
            if (services.TryResolve(out TContract _))
                return;
        }
        catch (ObjectDisposedException)
        {
        }

        missingContracts.Add(typeof(TContract).Name);
    }
}
