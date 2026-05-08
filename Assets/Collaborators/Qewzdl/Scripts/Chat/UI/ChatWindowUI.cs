using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChatWindowUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Message List")]
    [SerializeField] private ChatMessageListView messageListView;

    [Header("UI")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button sendButton;
    [SerializeField] private PlayerInputHandler playerInputHandler;

    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Visibility")]
    [SerializeField] private ChatVisibilityController visibilityController;

    [Header("Settings")]
    [SerializeField] private bool submitOnEnter = true;
    [SerializeField] private bool closeAfterSubmit = false;

    private IChatReadService readService;
    private GameStateMachine stateMachine;

    private bool isOpen;
    private bool isSubscribedToEventChannel;

    public event Action Opened;
    public event Action Closed;

    public bool IsOpen => isOpen;
    public bool CanOpen => readService != null && readService.CanSubmitMessages;
    public bool IsInputFocused => inputField != null && inputField.isFocused;

    public void Construct(
        IChatReadService readService,
        IChatCommandService commandService,
        GameStateMachine stateMachine)
    {
        UnsubscribeFromServices();

        this.readService = readService;
        this.stateMachine = stateMachine;

        ResolveReferences();
        SubscribeToEventChannel();
        SubscribeToServices();

        SetPlayerInputActive(true);
        ApplyOpenState(visibilityController != null && visibilityController.IsOpen, false);
        RefreshMessages();
    }

    public void Toggle()
    {
        if (isOpen)
        {
            Close();
            return;
        }

        Open();
    }

    public void Open()
    {
        if (!CanOpen)
            return;

        ResolveReferences();

        if (visibilityController != null)
        {
            if (!visibilityController.IsOpen)
                visibilityController.OpenChat();
            else
                ApplyOpenState(true, false);

            return;
        }

        ApplyOpenState(true, true);
    }

    public void Close()
    {
        ResolveReferences();

        if (visibilityController != null)
        {
            if (visibilityController.IsOpen)
                visibilityController.CloseChat();
            else
                ApplyOpenState(false, false);

            return;
        }

        ApplyOpenState(false, true);
    }

    private void Awake()
    {
        ResolveReferences();
        SubscribeToEventChannel();

        if (sendButton != null)
            sendButton.onClick.AddListener(SubmitCurrentMessage);

        if (inputField != null)
            inputField.onSubmit.AddListener(HandleInputSubmitted);

        isOpen = false;
        Hide();
    }

    private void OnDestroy()
    {
        if (isOpen)
            ApplyOpenState(false, true);

        UnsubscribeFromEventChannel();
        UnsubscribeFromServices();

        if (sendButton != null)
            sendButton.onClick.RemoveListener(SubmitCurrentMessage);

        if (inputField != null)
            inputField.onSubmit.RemoveListener(HandleInputSubmitted);
    }

    private void SubscribeToServices()
    {
        if (readService != null)
        {
            readService.MessagesChanged += RefreshMessages;
            readService.AvailabilityChanged += HandleAvailabilityChanged;
        }

        if (stateMachine != null)
            stateMachine.StateChanged += HandleGameStateChanged;
    }

    private void UnsubscribeFromServices()
    {
        if (readService != null)
        {
            readService.MessagesChanged -= RefreshMessages;
            readService.AvailabilityChanged -= HandleAvailabilityChanged;
        }

        if (stateMachine != null)
            stateMachine.StateChanged -= HandleGameStateChanged;
    }

    private void HandleAvailabilityChanged()
    {
        if (!CanOpen)
        {
            Close();
            return;
        }

        SyncVisibilityFromController();
    }

    private void HandleGameStateChanged(GameState previousState, GameState newState)
    {
        if (!CanOpen)
        {
            Close();
            return;
        }

        SyncVisibilityFromController();
        RefreshMessages();
    }

    private void HandleInputSubmitted(string value)
    {
        if (!submitOnEnter)
            return;

        SubmitCurrentMessage();
    }

    private void SubmitCurrentMessage()
    {
        if (readService == null)
            return;

        if (!readService.CanSubmitMessages)
            return;

        if (inputField == null)
            return;

        string text = inputField.text;

        if (!SubmitSendRequest(text))
            return;

        inputField.text = string.Empty;

        if (closeAfterSubmit)
        {
            Close();
            return;
        }

        inputField.ActivateInputField();
        inputField.Select();
    }

    private bool SubmitSendRequest(string text)
    {
        ChatSendRequest request = new ChatSendRequest(
            text,
            readService != null ? readService.CurrentChannel.ToString() : string.Empty
        );

        if (chatEvents == null)
        {
            Debug.LogError($"{nameof(ChatWindowUI)} requires an assigned {nameof(ChatEventChannel)}.", this);
            return false;
        }

        return chatEvents.RaiseSendRequested(request);
    }

    private bool ApplyOpenState(bool shouldOpen, bool publishEvent)
    {
        if (shouldOpen && !CanOpen)
        {
            RefreshVisibility();
            return false;
        }

        if (isOpen == shouldOpen)
        {
            RefreshVisibility();
            return true;
        }

        ChatVisibilityState previousState = isOpen
            ? ChatVisibilityState.Open
            : ChatVisibilityState.Closed;

        isOpen = shouldOpen;

        if (isOpen)
        {
            SetPlayerInputActive(false);

            RefreshVisibility();
            RefreshMessages();

            if (inputField != null)
            {
                inputField.ActivateInputField();
                inputField.Select();
            }

            Opened?.Invoke();
        }
        else
        {
            if (inputField != null)
                inputField.DeactivateInputField();

            SetPlayerInputActive(true);
            RefreshVisibility();
            Closed?.Invoke();
        }

        if (publishEvent)
        {
            ChatVisibilityState currentState = isOpen
                ? ChatVisibilityState.Open
                : ChatVisibilityState.Closed;

            RaiseVisibilityChanged(previousState, currentState);
        }

        return true;
    }

    private void RefreshVisibility()
    {
        bool shouldShow = isOpen && CanOpen;

        if (shouldShow)
            Show();
        else
            Hide();
    }

    private void SyncVisibilityFromController()
    {
        if (visibilityController != null && visibilityController.IsOpen)
        {
            ApplyOpenState(true, false);
            return;
        }

        RefreshVisibility();
    }

    private void RefreshMessages()
    {
        if (readService == null)
        {
            ClearMessages();
            return;
        }

        if (messageListView == null)
            return;

        messageListView.Render(readService);
    }

    private void ClearMessages()
    {
        if (messageListView != null)
            messageListView.Clear();
    }

    private void Show()
    {
        if (TrySetCanvasGroupVisibility(true))
            return;

        if (root != null)
            root.SetActive(true);
    }

    private void Hide()
    {
        if (TrySetCanvasGroupVisibility(false))
            return;

        if (root != null)
            root.SetActive(false);
    }

    private bool TrySetCanvasGroupVisibility(bool visible)
    {
        if (root == null || root != gameObject)
            return false;

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = root.AddComponent<CanvasGroup>();

        root.SetActive(true);

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;

        return true;
    }

    private void SetPlayerInputActive(bool value)
    {
        PlayerInputHandler inputHandler = ResolvePlayerInputHandler();

        if (inputHandler == null)
            return;

        inputHandler.SetInputActive(this, value);
    }

    private PlayerInputHandler ResolvePlayerInputHandler()
    {
        if (playerInputHandler != null)
            return playerInputHandler;

        playerInputHandler = PlayerInputHandler.Active;

        if (playerInputHandler != null)
            return playerInputHandler;

        playerInputHandler = FindFirstObjectByType<PlayerInputHandler>();
        return playerInputHandler;
    }

    private void SubscribeToEventChannel()
    {
        if (isSubscribedToEventChannel)
            return;

        if (chatEvents == null)
            return;

        chatEvents.VisibilityChanged += HandleVisibilityChanged;
        isSubscribedToEventChannel = true;
    }

    private void UnsubscribeFromEventChannel()
    {
        if (!isSubscribedToEventChannel || chatEvents == null)
            return;

        chatEvents.VisibilityChanged -= HandleVisibilityChanged;
        isSubscribedToEventChannel = false;
    }

    private void HandleVisibilityChanged(ChatVisibilityChangedEvent visibilityEvent)
    {
        ApplyOpenState(visibilityEvent.IsOpen, false);
    }

    private void RaiseVisibilityChanged(
        ChatVisibilityState previousState,
        ChatVisibilityState currentState)
    {
        if (chatEvents != null)
            chatEvents.RaiseVisibilityChanged(new ChatVisibilityChangedEvent(previousState, currentState));
    }

    private void ResolveReferences()
    {
        if (visibilityController == null)
            visibilityController = GetComponent<ChatVisibilityController>();
    }
}
