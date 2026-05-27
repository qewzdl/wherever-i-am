using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class ChatWindowUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Message List")]
    [SerializeField] private ChatMessageListView messageListView;

    [Header("UI")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private PlayerInputHandler playerInputHandler;

    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Visibility")]
    [SerializeField] private ChatVisibilityController visibilityController;

    [Header("Settings")]
    [SerializeField] private bool submitOnEnter = true;
    [SerializeField] private bool closeAfterSubmit = false;
    [SerializeField] private bool releaseFocusAfterSubmit = true;

    private IChatReadService readService;
    private GameStateMachine stateMachine;

    private bool isOpen;
    private bool isInputFocused;
    private bool isSubscribedToEventChannel;
    private Coroutine pendingInputRefocus;

    public event Action Opened;
    public event Action Closed;

    public bool IsOpen => isOpen;
    public bool CanOpen => readService != null && readService.CanSubmitMessages;
    public bool IsInputFocused => isInputFocused;

    public void SetInputSoundOverride(SoundEffect sound)
    {
        ResolveReferences();

        if (inputField == null)
            return;

        UiInputSound inputSound = inputField.GetComponent<UiInputSound>();

        if (inputSound == null)
        {
            if (sound == null)
                return;

            inputSound = inputField.gameObject.AddComponent<UiInputSound>();
        }

        inputSound.SetInputSoundOverride(sound);
    }

    public void SetEventChannel(ChatEventChannel chatEvents)
    {
        UnsubscribeFromEventChannel();

        this.chatEvents = chatEvents;

        ResolveReferences();
        SubscribeToEventChannel();

        if (visibilityController != null)
        {
            visibilityController.SetEventChannel(chatEvents);
        }
    }

    public void Construct(
        IChatReadService readService,
        GameStateMachine stateMachine)
    {
        UnsubscribeFromServices();

        this.readService = readService;
        this.stateMachine = stateMachine;

        ResolveReferences();
        SubscribeToEventChannel();
        SubscribeToServices();

        SetInputFocusState(false, true);
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

            FocusInput();
            return;
        }

        ApplyOpenState(true, true);
        FocusInput();
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

    public void FocusInput()
    {
        if (!isOpen)
            return;

        if (inputField == null)
            return;

        inputField.ActivateInputField();
        inputField.Select();
        SetInputFocusState(true);
    }

    public void ReleaseInputFocus()
    {
        CancelPendingInputRefocus();

        if (inputField != null)
        {
            inputField.DeactivateInputField();

            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == inputField.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        SetInputFocusState(false);
    }

    private void Awake()
    {
        ResolveReferences();
        SubscribeToEventChannel();

        if (inputField != null)
        {
            inputField.onSubmit.AddListener(HandleInputSubmitted);
            inputField.onSelect.AddListener(HandleInputSelected);
            inputField.onDeselect.AddListener(HandleInputDeselected);
        }

        isOpen = false;
        Hide();
    }

    private void Update()
    {
        HandleFocusedInputScroll();
    }

    private void OnDestroy()
    {
        if (isOpen)
            ApplyOpenState(false, true);
        else
            ReleaseInputFocus();

        UnsubscribeFromEventChannel();
        UnsubscribeFromServices();

        if (inputField != null)
        {
            inputField.onSubmit.RemoveListener(HandleInputSubmitted);
            inputField.onSelect.RemoveListener(HandleInputSelected);
            inputField.onDeselect.RemoveListener(HandleInputDeselected);
        }
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

    private void HandleInputSelected(string value)
    {
        if (!isOpen)
            return;

        SetInputFocusState(true);
    }

    private void HandleInputDeselected(string value)
    {
        SetInputFocusState(false);
    }

    private void HandleFocusedInputScroll()
    {
        if (!isOpen || !isInputFocused)
            return;

        if (messageListView == null)
            return;

        if (Mouse.current == null)
            return;

        Vector2 scrollDelta = Mouse.current.scroll.ReadValue();

        if (Mathf.Approximately(scrollDelta.y, 0f))
            return;

        if (messageListView.ContainsScreenPoint(Mouse.current.position.ReadValue()))
            return;

        messageListView.ScrollByWheelDelta(scrollDelta);
    }

    private void SubmitCurrentMessage()
    {
        if (inputField == null)
            return;

        string text = inputField.text;

        if (readService == null)
        {
            RaiseSendRejected(text, "Chat session is not ready.");
            RefocusInputAfterRejectedSubmit();
            return;
        }

        if (!readService.CanSubmitMessages)
        {
            RaiseSendRejected(text, "Chat is not available.");
            RefocusInputAfterRejectedSubmit();
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            RefocusInputAfterRejectedSubmit();
            return;
        }

        if (!SubmitSendRequest(text))
        {
            RefocusInputAfterRejectedSubmit();
            return;
        }

        CancelPendingInputRefocus();
        inputField.text = string.Empty;

        if (closeAfterSubmit)
        {
            Close();
            return;
        }

        if (releaseFocusAfterSubmit)
            ReleaseInputFocus();
        else
            FocusInput();
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

    private void RaiseSendRejected(string text, string reason)
    {
        if (chatEvents == null)
        {
            Debug.LogError($"{nameof(ChatWindowUI)} requires an assigned {nameof(ChatEventChannel)}.", this);
            return;
        }

        ChatSendRequest request = new ChatSendRequest(
            text,
            readService != null ? readService.CurrentChannel.ToString() : string.Empty
        );

        chatEvents.RaiseSendRejected(new ChatSendRejectedEvent(request, reason));
    }

    private void RefocusInputAfterRejectedSubmit()
    {
        if (!isOpen || inputField == null)
            return;

        FocusInput();

        CancelPendingInputRefocus();
        pendingInputRefocus = StartCoroutine(RefocusInputNextFrame());
    }

    private IEnumerator RefocusInputNextFrame()
    {
        yield return null;

        pendingInputRefocus = null;

        if (!isOpen || inputField == null)
            yield break;

        FocusInput();
        inputField.MoveTextEnd(false);
    }

    private void CancelPendingInputRefocus()
    {
        if (pendingInputRefocus == null)
            return;

        StopCoroutine(pendingInputRefocus);
        pendingInputRefocus = null;
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
            RefreshVisibility();
            RefreshMessages();

            FocusInput();
            Opened?.Invoke();
        }
        else
        {
            ReleaseInputFocus();
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

    private void SetInputFocusState(bool value, bool forcePlayerInputUpdate = false)
    {
        if (isInputFocused == value && !forcePlayerInputUpdate)
            return;

        isInputFocused = value;
        SetPlayerInputActive(!isInputFocused);
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
