using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChatWindowUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("UI")]
    [SerializeField] private TMP_Text messagesText;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button sendButton;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private PlayerInputHandler playerInputHandler;

    [Header("Settings")]
    [SerializeField] private bool submitOnEnter = true;
    [SerializeField] private bool closeAfterSubmit = false;

    private IChatReadService readService;
    private IChatCommandService commandService;
    private GameStateMachine stateMachine;

    private bool isOpen;

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

        if (isOpen)
            SetPlayerInputActive(true);

        isOpen = false;

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
    }

    public void Close()
    {
        bool wasOpen = isOpen;

        isOpen = false;

        if (inputField != null)
            inputField.DeactivateInputField();

        if (wasOpen)
            SetPlayerInputActive(true);

        RefreshVisibility();
    }

    private void Awake()
    {
        if (messagesText != null)
            messagesText.richText = false;

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
        if (messagesText == null)
            return;

        if (readService == null)
        {
            ClearMessages();
            return;
        }

        ChatChannel currentChannel = readService.CurrentChannel;
        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < readService.MessageCount; i++)
        {
            ChatMessageData message = readService.GetMessage(i);

            if (!ShouldShowMessage(message, currentChannel))
                continue;

            if (message.Channel == ChatChannel.System)
            {
                builder.Append("[System] ");
                builder.AppendLine(message.Text.ToString());
                continue;
            }

            builder.Append(message.SenderName.ToString());
            builder.Append(": ");
            builder.AppendLine(message.Text.ToString());
        }

        messagesText.text = builder.ToString();

        if (scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }

    private bool ShouldShowMessage(ChatMessageData message, ChatChannel currentChannel)
    {
        if (message.Channel == ChatChannel.System)
            return true;

        return message.Channel == currentChannel;
    }

    private void ClearMessages()
    {
        if (messagesText != null)
            messagesText.text = string.Empty;
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
