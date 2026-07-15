using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class ProjectNetworkSceneLoadCompletionTracker : MonoBehaviour
{
    [SerializeField] private ProjectContext projectContext;
    [SerializeField] private NetworkManager networkManager;

    private NetworkSceneManager subscribedSceneManager;
    private bool networkSceneCallbackSubscribed;

    private bool hasPendingNetworkTransition;
    private long pendingOperationId;
    private ProjectSceneKind pendingNetworkScene;
    private ProjectSceneServerAction[] pendingServerActionsAfterLoad;

    public event Action<long, ProjectSceneKind, ProjectSceneServerAction[]> NetworkLoadCompleted;
    public event Action<long, ProjectSceneKind> NetworkLoadFailed;

    private void OnDisable()
    {
        Shutdown();
    }

    public void Construct(ProjectContext context, NetworkManager manager)
    {
        Shutdown();
        projectContext = context;
        networkManager = manager;
    }

    public bool Initialize()
    {
        return HasRequiredReferences();
    }

    public void Shutdown()
    {
        CancelPending();
    }

    public void DisposeComposition()
    {
        Shutdown();
        projectContext = null;
        networkManager = null;
    }

    public bool Track(
        long operationId,
        ProjectSceneKind scene,
        ProjectSceneServerAction[] serverActionsAfterLoad)
    {
        if (!HasRequiredReferences())
            return false;

        if (!networkManager.IsListening)
        {
            Debug.LogError(
                $"Cannot track network scene load completion for '{scene}' without an active network session.",
                this);

            return false;
        }

        if (hasPendingNetworkTransition)
        {
            Debug.LogError(
                $"Cannot track network scene '{scene}' because another network transition is still pending.",
                this);

            return false;
        }

        if (!TrySubscribeToNetworkSceneCallback(true))
            return false;

        pendingOperationId = operationId;
        pendingNetworkScene = scene;
        pendingServerActionsAfterLoad = serverActionsAfterLoad;
        hasPendingNetworkTransition = true;

        return true;
    }

    private void ClearPending()
    {
        pendingOperationId = 0;
        pendingNetworkScene = ProjectSceneKind.Unknown;
        pendingServerActionsAfterLoad = null;
        hasPendingNetworkTransition = false;
    }

    public void CancelPending()
    {
        ClearPending();
        UnsubscribeFromNetworkSceneCallback();
    }

    private void HandleNetworkLoadEventCompleted(
        string sceneName,
        LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut)
    {
        if (!HasRequiredReferences())
            return;

        ProjectSceneKind loadedScene = projectContext.GetSceneKind(sceneName);

        if (loadedScene == ProjectSceneKind.Unknown)
            return;

        if (!hasPendingNetworkTransition)
            return;

        if (loadedScene != pendingNetworkScene)
            return;

        long operationId = pendingOperationId;
        ProjectSceneServerAction[] actions = pendingServerActionsAfterLoad;
        bool clientsFailedToLoad = clientsTimedOut != null && clientsTimedOut.Count > 0;
        CancelPending();

        if (clientsFailedToLoad)
        {
            Debug.LogError(
                $"Network scene '{loadedScene}' timed out for {clientsTimedOut.Count} client(s).",
                this);

            RuntimeEventDispatcher.Invoke(
                NetworkLoadFailed,
                operationId,
                loadedScene,
                $"{nameof(ProjectNetworkSceneLoadCompletionTracker)}." +
                nameof(NetworkLoadFailed),
                this);
            return;
        }

        RuntimeEventDispatcher.Invoke(
            NetworkLoadCompleted,
            operationId,
            loadedScene,
            actions,
            $"{nameof(ProjectNetworkSceneLoadCompletionTracker)}." +
            nameof(NetworkLoadCompleted),
            this);
    }

    private bool TrySubscribeToNetworkSceneCallback(bool logErrors)
    {
        if (networkManager == null)
        {
            if (logErrors)
                Debug.LogError($"{nameof(ProjectNetworkSceneLoadCompletionTracker)} is missing {nameof(NetworkManager)}.", this);

            return false;
        }

        if (networkManager.SceneManager == null)
        {
            if (logErrors)
                Debug.LogError($"{nameof(NetworkManager)} has no active {nameof(NetworkSceneManager)}.", this);

            return false;
        }

        if (networkSceneCallbackSubscribed &&
            subscribedSceneManager == networkManager.SceneManager)
            return true;

        UnsubscribeFromNetworkSceneCallback();

        subscribedSceneManager = networkManager.SceneManager;
        subscribedSceneManager.OnLoadEventCompleted += HandleNetworkLoadEventCompleted;
        networkSceneCallbackSubscribed = true;

        return true;
    }

    private void UnsubscribeFromNetworkSceneCallback()
    {
        if (!networkSceneCallbackSubscribed)
            return;

        if (subscribedSceneManager != null)
            subscribedSceneManager.OnLoadEventCompleted -= HandleNetworkLoadEventCompleted;

        subscribedSceneManager = null;
        networkSceneCallbackSubscribed = false;
    }

    private bool HasRequiredReferences()
    {
        bool valid = true;

        valid &= ValidateRequiredReference(projectContext, nameof(projectContext));
        valid &= ValidateRequiredReference(networkManager, nameof(networkManager));

        return valid;
    }

    private bool ValidateRequiredReference(UnityEngine.Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(ProjectNetworkSceneLoadCompletionTracker)} is missing '{fieldName}'.", this);
        return false;
    }
}
