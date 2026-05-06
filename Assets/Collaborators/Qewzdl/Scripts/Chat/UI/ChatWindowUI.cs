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

    [Header("Settings")]
    [SerializeField] private bool submitOnEnter = true;
    [SerializeField] private bool closeAfterSubmit = false;

    private IChatReadService readService;
    private IChatCommandService commandService;
    private GameStateMachine stateMachine;

    private bool isOpen;

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
        this.commandService = commandService;
        this.stateMachine = stateMachine;

        SubscribeToServices();

        isOpen = false;

        SetPlayerInputActive(true);
        RefreshVisibility();
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

        if (isOpen)
            return;

        isOpen = true;
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

    public void Close()
    {
        bool wasOpen = isOpen;

        isOpen = false;

        if (inputField != null)
            inputField.DeactivateInputField();

        if (wasOpen)
        {
            SetPlayerInputActive(true);
            Closed?.Invoke();
        }

        RefreshVisibility();
    }

    private void Awake()
    {
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
            SetPlayerInputActive(true);

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

        RefreshVisibility();
    }

    private void HandleGameStateChanged(GameState previousState, GameState newState)
    {
        if (!CanOpen)
        {
            Close();
            return;
        }

        RefreshVisibility();
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
        if (commandService == null || readService == null)
            return;

        if (!readService.CanSubmitMessages)
            return;

        if (inputField == null)
            return;

        string text = inputField.text;

        if (string.IsNullOrWhiteSpace(text))
            return;

        commandService.SubmitMessage(text);

        inputField.text = string.Empty;

        if (closeAfterSubmit)
        {
            Close();
            return;
        }

        inputField.ActivateInputField();
        inputField.Select();
    }

    private void RefreshVisibility()
    {
        bool shouldShow = isOpen && CanOpen;

        if (shouldShow)
            Show();
        else
            Hide();
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
        if (root != null)
            root.SetActive(true);
    }

    private void Hide()
    {
        if (root != null)
            root.SetActive(false);
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
}
