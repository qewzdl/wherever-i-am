using System;
using UnityEngine;

public class ChatSessionBinder : MonoBehaviour, IDisposable
{
    [Header("References")]
    [SerializeField] private ChatWindowUI chatWindow;
    [SerializeField] private ChatNotificationController notificationController;
    private ISessionServiceRegistry serviceRegistry;
    private IPlayerScopeRegistry playerScopes;
    private IGameStateService stateMachine;
    private bool registrySubscribed;
    private bool playerRegistrySubscribed;

    private void Awake()
    {
        ResolveReferences();
    }

    internal void Construct(
        ISessionServiceRegistry registry,
        IPlayerScopeRegistry playerScopeRegistry,
        IGameStateService gameState)
    {
        UnsubscribeFromRegistries();
        serviceRegistry = registry;
        playerScopes = playerScopeRegistry;
        stateMachine = gameState;

        if (isActiveAndEnabled)
            SubscribeToRegistries();

        RefreshBinding();
    }

    private void OnEnable()
    {
        SubscribeToRegistries();
        RefreshBinding();
    }

    private void OnDisable()
    {
        UnsubscribeFromRegistries();
        Unbind();
    }

    public void Dispose()
    {
        UnsubscribeFromRegistries();
        Unbind();
        serviceRegistry = null;
        playerScopes = null;
        stateMachine = null;
    }

    private void RefreshBinding()
    {
        IChatReadService readService = null;
        IChatCommandService commandService = null;
        ILocalPlayerInputService inputService = ResolveLocalInputService();

        if (serviceRegistry != null && !serviceRegistry.IsDisposed)
        {
            serviceRegistry.TryResolve(out readService);
            serviceRegistry.TryResolve(out commandService);
        }

        if (chatWindow != null)
            chatWindow.Construct(readService, commandService, stateMachine, inputService);

        if (notificationController != null)
            notificationController.Construct(readService, chatWindow);
    }

    private void Unbind()
    {
        ResolveReferences();

        if (chatWindow != null)
            chatWindow.Construct(null, null, stateMachine, null);

        if (notificationController != null)
            notificationController.Construct(null, chatWindow);
    }

    private void ResolveReferences()
    {
        if (chatWindow == null)
            chatWindow = GetComponentInChildren<ChatWindowUI>(true);

        if (notificationController == null)
            notificationController = GetComponentInChildren<ChatNotificationController>(true);
    }

    private void SubscribeToRegistries()
    {
        if (!registrySubscribed && serviceRegistry != null && !serviceRegistry.IsDisposed)
        {
            serviceRegistry.ServicesChanged += RefreshBinding;
            registrySubscribed = true;
        }

        if (!playerRegistrySubscribed && playerScopes != null && !playerScopes.IsDisposed)
        {
            playerScopes.PlayerScopeOpened += HandlePlayerScopeOpened;
            playerScopes.PlayerScopeClosing += HandlePlayerScopeClosing;
            playerRegistrySubscribed = true;
        }
    }

    private void UnsubscribeFromRegistries()
    {
        if (registrySubscribed && serviceRegistry != null)
            serviceRegistry.ServicesChanged -= RefreshBinding;

        if (playerRegistrySubscribed && playerScopes != null)
        {
            playerScopes.PlayerScopeOpened -= HandlePlayerScopeOpened;
            playerScopes.PlayerScopeClosing -= HandlePlayerScopeClosing;
        }

        registrySubscribed = false;
        playerRegistrySubscribed = false;
    }

    private void HandlePlayerScopeOpened(IPlayerScope playerScope)
    {
        if (playerScope != null && playerScope.IsLocalPlayer)
            RefreshBinding();
    }

    private void HandlePlayerScopeClosing(IPlayerScope playerScope)
    {
        if (playerScope == null || !playerScope.IsLocalPlayer)
            return;

        if (chatWindow != null)
            chatWindow.SetLocalInputService(null);
    }

    private ILocalPlayerInputService ResolveLocalInputService()
    {
        if (playerScopes == null ||
            playerScopes.IsDisposed ||
            !playerScopes.TryGetLocalPlayerScope(out IPlayerScope playerScope) ||
            playerScope.LocalServices == null)
        {
            return null;
        }

        playerScope.LocalServices.TryResolve(out ILocalPlayerInputService inputService);
        return inputService;
    }
}
