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

    [Header("References")]
    [SerializeField] private GameStateMachine stateMachine;

    [Header("Settings")]
    [SerializeField] private bool submitOnEnter = true;

    private IChatReadService readService;
    private IChatCommandService commandService;

    private void Awake()
    {
        ResolveReferences();

        if (messagesText != null)
            messagesText.richText = false;

        if (sendButton != null)
            sendButton.onClick.AddListener(SubmitCurrentMessage);

        if (inputField != null)
            inputField.onSubmit.AddListener(HandleInputSubmitted);

        Hide();
    }

    private void OnEnable()
    {
        NetworkChatSession.SessionSpawned += HandleChatSessionSpawned;
        NetworkChatSession.SessionDespawned += HandleChatSessionDespawned;

        if (stateMachine != null)
            stateMachine.StateChanged += HandleGameStateChanged;

        if (NetworkChatSession.Instance != null)
            Bind(NetworkChatSession.Instance);

        RefreshVisibility();
    }

    private void OnDisable()
    {
        NetworkChatSession.SessionSpawned -= HandleChatSessionSpawned;
        NetworkChatSession.SessionDespawned -= HandleChatSessionDespawned;

        if (stateMachine != null)
            stateMachine.StateChanged -= HandleGameStateChanged;

        Unbind();
    }

    private void OnDestroy()
    {
        if (sendButton != null)
            sendButton.onClick.RemoveListener(SubmitCurrentMessage);

        if (inputField != null)
            inputField.onSubmit.RemoveListener(HandleInputSubmitted);
    }

    private void Bind(NetworkChatSession chatSession)
    {
        Unbind();

        readService = chatSession;
        commandService = chatSession;

        readService.MessagesChanged += RefreshMessages;
        readService.AvailabilityChanged += RefreshVisibility;

        RefreshVisibility();
        RefreshMessages();
    }

    private void Unbind()
    {
        if (readService != null)
        {
            readService.MessagesChanged -= RefreshMessages;
            readService.AvailabilityChanged -= RefreshVisibility;
        }

        readService = null;
        commandService = null;
    }

    private void HandleChatSessionSpawned(NetworkChatSession chatSession)
    {
        Bind(chatSession);
    }

    private void HandleChatSessionDespawned()
    {
        Unbind();
        ClearMessages();
        Hide();
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

    private void ResolveReferences()
    {
        if (stateMachine == null)
            stateMachine = FindFirstObjectByType<GameStateMachine>();
    }
}
