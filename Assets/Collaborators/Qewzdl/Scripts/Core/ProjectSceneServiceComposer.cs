using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectSceneServiceComposer : MonoBehaviour, IDisposable
{
    [Header("Scene Services")]
    [SerializeField] private LocalSceneLoader localSceneLoader;
    [SerializeField] private NetworkSceneLoader networkSceneLoader;
    [SerializeField] private ProjectSceneNavigator sceneNavigator;
    [SerializeField] private ProjectSceneFlowService sceneFlowService;

    [Header("Scene Flow Services")]
    [SerializeField] private ProjectSceneTransitionValidator sceneTransitionValidator;
    [SerializeField] private ProjectSceneLoadExecutor sceneLoadExecutor;
    [SerializeField] private ProjectNetworkSceneLoadCompletionTracker networkLoadCompletionTracker;
    [SerializeField] private ProjectScenePostLoadActionRunner postLoadActionRunner;

    private ProjectContext composedContext;
    private bool composing;
    private bool compositionFailureLogged;
    private bool composed;
    private bool initialized;

    public LocalSceneLoader LocalSceneLoader => localSceneLoader;
    public NetworkSceneLoader NetworkSceneLoader => networkSceneLoader;
    public ProjectSceneNavigator SceneNavigator => sceneNavigator;
    public ProjectSceneFlowService SceneFlowService => sceneFlowService;
    public ProjectSceneTransitionValidator SceneTransitionValidator => sceneTransitionValidator;
    public ProjectSceneLoadExecutor SceneLoadExecutor => sceneLoadExecutor;
    public ProjectNetworkSceneLoadCompletionTracker NetworkLoadCompletionTracker => networkLoadCompletionTracker;
    public ProjectScenePostLoadActionRunner PostLoadActionRunner => postLoadActionRunner;
    public bool IsComposed => composed;
    public bool IsInitialized => initialized;

    public bool Validate(ProjectContext projectContext, bool logErrors = true)
    {
        return HasRequiredReferences(projectContext, logErrors);
    }

    public bool Compose(ProjectContext projectContext)
    {
        if (composed)
        {
            if (composedContext == projectContext)
                return true;

            Debug.LogError(
                $"{nameof(ProjectSceneServiceComposer)} is already composed with another context.",
                this);

            return false;
        }

        if (composing)
            return false;

        composing = true;

        try
        {
            bool logErrors = !compositionFailureLogged;

            if (!Validate(projectContext, logErrors))
            {
                compositionFailureLogged = true;
                return false;
            }

            NetworkManager networkManager = projectContext.NetworkManager;
            GameStateMachine stateMachine = projectContext.StateMachine;

            localSceneLoader.Construct(projectContext);
            networkSceneLoader.Construct(projectContext.SceneRegistry, networkManager);
            sceneNavigator.Construct(projectContext, localSceneLoader, networkSceneLoader);

            sceneTransitionValidator.Construct(projectContext, networkManager);
            sceneLoadExecutor.Construct(sceneNavigator, networkManager);
            networkLoadCompletionTracker.Construct(projectContext, networkManager);
            postLoadActionRunner.Construct(networkManager);

            sceneFlowService.Construct(
                projectContext,
                stateMachine,
                sceneTransitionValidator,
                sceneLoadExecutor,
                networkLoadCompletionTracker,
                postLoadActionRunner);

            composedContext = projectContext;
            compositionFailureLogged = false;
            composed = true;
            return true;
        }
        finally
        {
            composing = false;
        }
    }

    public bool Initialize()
    {
        if (initialized)
            return true;

        if (!composed)
        {
            Debug.LogError($"{nameof(ProjectSceneServiceComposer)} must be composed before initialize.", this);
            return false;
        }

        if (!networkLoadCompletionTracker.Initialize())
            return false;

        if (!sceneFlowService.Initialize())
        {
            networkLoadCompletionTracker.Shutdown();
            return false;
        }

        initialized = true;
        return true;
    }

    public void Shutdown()
    {
        RunCleanup(() => sceneFlowService?.Shutdown(), sceneFlowService);
        RunCleanup(() => networkLoadCompletionTracker?.Shutdown(), networkLoadCompletionTracker);
        initialized = false;
    }

    public void Dispose()
    {
        Shutdown();

        RunCleanup(() => sceneFlowService?.DisposeComposition(), sceneFlowService);
        RunCleanup(() => postLoadActionRunner?.DisposeComposition(), postLoadActionRunner);
        RunCleanup(() => networkLoadCompletionTracker?.DisposeComposition(), networkLoadCompletionTracker);
        RunCleanup(() => sceneLoadExecutor?.DisposeComposition(), sceneLoadExecutor);
        RunCleanup(() => sceneTransitionValidator?.DisposeComposition(), sceneTransitionValidator);
        RunCleanup(() => sceneNavigator?.DisposeComposition(), sceneNavigator);
        RunCleanup(() => networkSceneLoader?.DisposeComposition(), networkSceneLoader);
        RunCleanup(() => localSceneLoader?.DisposeComposition(), localSceneLoader);

        composedContext = null;
        composed = false;
        composing = false;
        compositionFailureLogged = false;
    }

    private void RunCleanup(Action cleanup, UnityEngine.Object owner)
    {
        try
        {
            cleanup?.Invoke();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, owner != null ? owner : this);
        }
    }

    private bool HasRequiredReferences(ProjectContext projectContext, bool logErrors)
    {
        bool valid = true;

        valid &= ValidateRequiredReference(projectContext, nameof(projectContext), logErrors);

        if (projectContext != null)
        {
            valid &= ValidateRequiredReference(projectContext.NetworkManager, nameof(projectContext.NetworkManager), logErrors);
            valid &= ValidateRequiredReference(projectContext.StateMachine, nameof(projectContext.StateMachine), logErrors);
        }

        valid &= ValidateRequiredReference(localSceneLoader, nameof(localSceneLoader), logErrors);
        valid &= ValidateRequiredReference(networkSceneLoader, nameof(networkSceneLoader), logErrors);
        valid &= ValidateRequiredReference(sceneNavigator, nameof(sceneNavigator), logErrors);
        valid &= ValidateRequiredReference(sceneFlowService, nameof(sceneFlowService), logErrors);
        valid &= ValidateRequiredReference(sceneTransitionValidator, nameof(sceneTransitionValidator), logErrors);
        valid &= ValidateRequiredReference(sceneLoadExecutor, nameof(sceneLoadExecutor), logErrors);
        valid &= ValidateRequiredReference(networkLoadCompletionTracker, nameof(networkLoadCompletionTracker), logErrors);
        valid &= ValidateRequiredReference(postLoadActionRunner, nameof(postLoadActionRunner), logErrors);

        return valid;
    }

    private bool ValidateRequiredReference(UnityEngine.Object reference, string fieldName, bool logError)
    {
        if (reference != null)
            return true;

        if (logError)
            Debug.LogError($"{nameof(ProjectSceneServiceComposer)} is missing '{fieldName}'.", this);

        return false;
    }
}
