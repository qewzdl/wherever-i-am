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

    [Header("Settings")]
    [SerializeField] private bool submitOnEnter = true;

    private IChatReadService readService;
    private IChatCommandService commandService;
    private GameStateMachine stateMachine;

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

        RefreshVisibility();
        RefreshMessages();
    }

    private void Awake()
    {
        if (messagesText != null)
            messagesText.richText = false;

        if (sendButton != null)
            sendButton.onClick.AddListener(SubmitCurrentMessage);

        if (inputField != null)
            inputField.onSubmit.AddListener(HandleInputSubmitted);

        Hide();
    }

    private void OnDestroy()
    {
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
            readService.AvailabilityChanged += RefreshVisibility;
        }

        if (stateMachine != null)
            stateMachine.StateChanged += HandleGameStateChanged;
    }

    private void UnsubscribeFromServices()
    {
        if (readService != null)
        {
            readService.MessagesChanged -= RefreshMessages;
            readService.AvailabilityChanged -= RefreshVisibility;
        }

        if (stateMachine != null)
            stateMachine.StateChanged -= HandleGameStateChanged;
    }

    private void HandleGameStateChanged(GameState previousState, GameState newState)
    {
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
        inputField.ActivateInputField();
    }

    private void RefreshVisibility()
    {
        bool shouldShow = readService != null && readService.CanSubmitMessages;

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
}