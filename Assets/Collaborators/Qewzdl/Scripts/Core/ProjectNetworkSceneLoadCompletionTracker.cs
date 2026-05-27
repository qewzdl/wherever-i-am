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
    private ProjectSceneKind pendingNetworkScene;
    private ProjectSceneServerAction[] pendingServerActionsAfterLoad;

    public event Action<ProjectSceneKind, ProjectSceneServerAction[]> NetworkLoadCompleted;

    private void Awake()
    {
        HasRequiredReferences();
    }

    private void OnEnable()
    {
        TrySubscribeToNetworkSceneCallback(false);
    }

    private void OnDisable()
    {
        UnsubscribeFromNetworkSceneCallback();
    }

    public void Construct(ProjectContext context, NetworkManager manager)
    {
        projectContext = context;
        networkManager = manager;

        if (isActiveAndEnabled)
            TrySubscribeToNetworkSceneCallback(false);
    }

    public bool Track(
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

        if (!TrySubscribeToNetworkSceneCallback(true))
            return false;

        pendingNetworkScene = scene;
        pendingServerActionsAfterLoad = serverActionsAfterLoad;
        hasPendingNetworkTransition = true;

        return true;
    }

    public void ClearPending()
    {
        pendingNetworkScene = ProjectSceneKind.Unknown;
        pendingServerActionsAfterLoad = null;
        hasPendingNetworkTransition = false;
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

        if (hasPendingNetworkTransition && loadedScene != pendingNetworkScene)
            return;

        ProjectSceneServerAction[] actions = null;

        if (hasPendingNetworkTransition)
        {
            actions = pendingServerActionsAfterLoad;
            ClearPending();
        }

        NetworkLoadCompleted?.Invoke(loadedScene, actions);
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
