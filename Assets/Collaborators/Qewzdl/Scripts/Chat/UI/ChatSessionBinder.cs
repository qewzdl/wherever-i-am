using System;
using UnityEngine;

public class ChatSessionBinder : MonoBehaviour, IDisposable
{
    [Header("References")]
    [SerializeField] private ChatWindowUI chatWindow;
    [SerializeField] private ChatNotificationController notificationController;
    private ISessionServiceRegistry serviceRegistry;
    private IGameStateService stateMachine;
    private bool registrySubscribed;

    private void Awake()
    {
        ResolveReferences();
    }

    public void Construct(
        ISessionServiceRegistry registry,
        IGameStateService gameState)
    {
        UnsubscribeFromRegistry();
        serviceRegistry = registry;
        stateMachine = gameState;

        if (isActiveAndEnabled)
            SubscribeToRegistry();

        RefreshBinding();
    }

    private void OnEnable()
    {
        SubscribeToRegistry();
        RefreshBinding();
    }

    private void OnDisable()
    {
        UnsubscribeFromRegistry();
        Unbind();
    }

    public void Dispose()
    {
        UnsubscribeFromRegistry();
        Unbind();
        serviceRegistry = null;
        stateMachine = null;
    }

    private void RefreshBinding()
    {
        IChatReadService readService = null;
        IChatCommandService commandService = null;

        if (serviceRegistry != null && !serviceRegistry.IsDisposed)
        {
            serviceRegistry.TryResolve(out readService);
            serviceRegistry.TryResolve(out commandService);
        }

        if (chatWindow != null)
            chatWindow.Construct(readService, commandService, stateMachine);

        if (notificationController != null)
            notificationController.Construct(readService, chatWindow);
    }

    private void Unbind()
    {
        ResolveReferences();

        if (chatWindow != null)
            chatWindow.Construct(null, null, stateMachine);

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

    private void SubscribeToRegistry()
    {
        if (registrySubscribed || serviceRegistry == null || serviceRegistry.IsDisposed)
            return;

        serviceRegistry.ServicesChanged += RefreshBinding;
        registrySubscribed = true;
    }

    private void UnsubscribeFromRegistry()
    {
        if (!registrySubscribed)
            return;

        if (serviceRegistry != null)
            serviceRegistry.ServicesChanged -= RefreshBinding;

        registrySubscribed = false;
    }
}
