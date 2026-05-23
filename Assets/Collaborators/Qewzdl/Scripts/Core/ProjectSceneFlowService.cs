using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class ProjectSceneFlowService : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ProjectContext projectContext;
    [SerializeField] private ProjectSceneNavigator sceneNavigator;
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameStateMachine stateMachine;

    [Header("Server Actions")]
    [SerializeField] private MonoBehaviour[] serverActionHandlers;

    private NetworkSceneManager subscribedSceneManager;
    private bool networkSceneCallbackSubscribed;

    private bool hasPendingNetworkTransition;
    private ProjectSceneKind pendingNetworkScene;
    private ProjectSceneServerAction[] pendingServerActionsAfterLoad;

    private void Awake()
    {
        HasRequiredReferences();
    }

    private void OnEnable()
    {
        SubscribeToNetworkSceneCallback();
    }

    private void OnDisable()
    {
        UnsubscribeFromNetworkSceneCallback();
    }

    public void Construct(ProjectContext context, ProjectSceneNavigator navigator)
    {
        projectContext = context;
        sceneNavigator = navigator;
    }

    public bool LoadScene(ProjectSceneKind targetScene)
    {
        if (!HasRequiredReferences())
            return false;

        if (!projectContext.TryGetScene(targetScene, out ProjectSceneDefinition scene))
        {
            Debug.LogError($"Scene is not configured for {targetScene}.", this);
            return false;
        }

        ProjectSceneKind currentScene = projectContext.GetActiveSceneKind();

        if (!projectContext.SceneFlow.TryGetTransition(
                currentScene,
                scene.Kind,
                out ProjectSceneTransitionDefinition transition))
        {
            Debug.LogError($"Scene transition is not configured: {currentScene} -> {scene.Kind}.", this);
            return false;
        }

        if (!CanUseTransition(transition))
            return false;

        if (!ValidateServerActionHandlers(transition))
            return false;

        stateMachine.ChangeState(transition.StateBeforeLoad);

        switch (transition.LoadMode)
        {
            case ProjectSceneLoadMode.Local:
                return LoadLocalScene(scene, transition);

            case ProjectSceneLoadMode.Network:
                return LoadNetworkScene(scene, transition);
        }

        Debug.LogError(
            $"Unsupported scene load mode '{transition.LoadMode}' for transition {transition.FromScene} -> {transition.ToScene}.",
            this);

        return false;
    }

    private bool LoadLocalScene(
        ProjectSceneDefinition scene,
        ProjectSceneTransitionDefinition transition)
    {
        if (IsNetworkSessionActive())
        {
            Debug.LogError(
                $"Cannot load local scene '{scene.Kind}' while NetworkManager is listening. Shutdown network session first.",
                this);
            return false;
        }

        if (!sceneNavigator.Load(scene.Kind, transition.LoadMode))
            return false;

        ApplyTargetState(scene.Kind);
        RunServerActionsAfterLoad(scene.Kind, transition.ServerActionsAfterLoad);

        return true;
    }

    private bool LoadNetworkScene(
        ProjectSceneDefinition scene,
        ProjectSceneTransitionDefinition transition)
    {
        if (!IsNetworkSessionActive())
        {
#if UNITY_EDITOR
            if (transition.AllowEditorDirectLoad)
            {
                if (!sceneNavigator.Load(scene.Kind, ProjectSceneLoadMode.Local))
                    return false;

                ApplyTargetState(scene.Kind);
                return true;
            }
#endif

            Debug.LogError(
                $"Cannot load network scene '{scene.Kind}' without an active network session.",
                this);
            return false;
        }

        PreparePendingNetworkTransition(scene.Kind, transition.ServerActionsAfterLoad);
        SubscribeToNetworkSceneCallback();

        if (sceneNavigator.Load(scene.Kind, transition.LoadMode))
            return true;

        ClearPendingNetworkTransition();
        return false;
    }

    private bool CanUseTransition(ProjectSceneTransitionDefinition transition)
    {
        if (transition.Authority == ProjectSceneTransitionAuthority.ServerOnly)
        {
            if (networkManager == null || !networkManager.IsServer)
            {
                Debug.LogWarning(
                    $"Only server can execute scene transition {transition.FromScene} -> {transition.ToScene}.",
                    this);
                return false;
            }
        }

        if (!transition.RequiresActiveNetworkSession)
            return true;

        if (IsNetworkSessionActive())
            return true;

#if UNITY_EDITOR
        if (transition.AllowEditorDirectLoad)
            return true;
#endif

        Debug.LogError(
            $"Scene transition {transition.FromScene} -> {transition.ToScene} requires an active network session.",
            this);
        return false;
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

        ApplyTargetState(loadedScene);

        if (!hasPendingNetworkTransition)
            return;

        ProjectSceneServerAction[] actions = pendingServerActionsAfterLoad;
        ClearPendingNetworkTransition();

        RunServerActionsAfterLoad(loadedScene, actions);
    }

    private void PreparePendingNetworkTransition(
        ProjectSceneKind scene,
        ProjectSceneServerAction[] serverActionsAfterLoad)
    {
        pendingNetworkScene = scene;
        pendingServerActionsAfterLoad = serverActionsAfterLoad;
        hasPendingNetworkTransition = true;
    }

    private void ClearPendingNetworkTransition()
    {
        pendingNetworkScene = ProjectSceneKind.Unknown;
        pendingServerActionsAfterLoad = null;
        hasPendingNetworkTransition = false;
    }

    private void ApplyTargetState(ProjectSceneKind sceneKind)
    {
        if (!projectContext.TryGetScene(sceneKind, out ProjectSceneDefinition scene))
            return;

        stateMachine.ChangeState(scene.State);
    }

    private bool RunServerActionsAfterLoad(
        ProjectSceneKind loadedScene,
        ProjectSceneServerAction[] actions)
    {
        if (actions == null || actions.Length == 0)
            return true;

        if (networkManager == null || !networkManager.IsServer)
            return true;

        for (int i = 0; i < actions.Length; i++)
        {
            if (!TryGetServerActionHandler(actions[i], out IProjectSceneFlowServerActionHandler handler))
                return false;

            handler.Handle(actions[i], loadedScene);
        }

        return true;
    }

    private bool ValidateServerActionHandlers(ProjectSceneTransitionDefinition transition)
    {
        ProjectSceneServerAction[] actions = transition.ServerActionsAfterLoad;

        if (actions == null || actions.Length == 0)
            return true;

        if (networkManager == null || !networkManager.IsServer)
            return true;

        for (int i = 0; i < actions.Length; i++)
        {
            if (!TryGetServerActionHandler(actions[i], out _))
                return false;
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
                $"{nameof(ProjectSceneFlowService)} has no server action handlers assigned for action '{action}'.",
                this);
            return false;
        }

        for (int i = 0; i < serverActionHandlers.Length; i++)
        {
            MonoBehaviour behaviour = serverActionHandlers[i];

            if (behaviour == null)
            {
                Debug.LogError(
                    $"{nameof(ProjectSceneFlowService)} has an empty server action handler slot.",
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

        Debug.LogError(
            $"No server action handler found for action '{action}'.",
            this);
        return false;
    }

    private void SubscribeToNetworkSceneCallback()
    {
        if (networkManager == null)
            return;

        if (networkManager.SceneManager == null)
            return;

        if (networkSceneCallbackSubscribed && subscribedSceneManager == networkManager.SceneManager)
            return;

        UnsubscribeFromNetworkSceneCallback();

        subscribedSceneManager = networkManager.SceneManager;
        subscribedSceneManager.OnLoadEventCompleted += HandleNetworkLoadEventCompleted;
        networkSceneCallbackSubscribed = true;
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

    private bool IsNetworkSessionActive()
    {
        return networkManager != null && networkManager.IsListening;
    }

    private bool HasRequiredReferences()
    {
        if (projectContext == null)
        {
            Debug.LogError($"{nameof(ProjectSceneFlowService)} is missing {nameof(ProjectContext)}.", this);
            return false;
        }

        if (projectContext.SceneFlow == null)
        {
            Debug.LogError($"{nameof(ProjectSceneFlowService)} is missing {nameof(ProjectSceneFlow)}.", this);
            return false;
        }

        if (sceneNavigator == null)
        {
            Debug.LogError($"{nameof(ProjectSceneFlowService)} is missing {nameof(ProjectSceneNavigator)}.", this);
            return false;
        }

        if (networkManager == null)
        {
            Debug.LogError($"{nameof(ProjectSceneFlowService)} is missing {nameof(NetworkManager)}.", this);
            return false;
        }

        if (stateMachine == null)
        {
            Debug.LogError($"{nameof(ProjectSceneFlowService)} is missing {nameof(GameStateMachine)}.", this);
            return false;
        }

        return true;
    }
}