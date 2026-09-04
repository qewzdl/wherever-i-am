using System;
using UnityEngine;
using UnityEngine.UIElements;

// The lobby's chat, in UI Toolkit.
//
// Same contract as the uGUI window it replaces - the session binder still
// constructs it, the key still opens it, the visibility controller still owns
// whether it is up - so nothing above this file changed. What changed is the
// view: a document that reads from the same theme as every other screen in the
// game, and a corner that is not the one the rail is standing in.
//
// The phone keeps the old window. It is not a screen, it is a prop, and moving
// a canvas hung on a three-dimensional phone to a world-space panel is a
// different job. See IChatWindowView.
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class LobbyChatDocument : MonoBehaviour, IChatWindowView
{
    private const string OpenClass = "chat--open";
    private const string MessageClass = "chat__message";
    private const string OwnMessageClass = "chat__message--own";
    private const string SystemMessageClass = "chat__message--system";
    private const string SenderClass = "chat__message__sender";
    private const string TextClass = "chat__message__text";

    [Header("References")]
    [SerializeField] private UIDocument document;
    [SerializeField] private ChatVisibilityController visibilityController;
    [SerializeField] private UiDocumentSounds sounds;

    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    // The roster says "Waiting for the room..." rather than showing four empty
    // rows. A box with a rule over it and a caret under it is the same silence
    // with nothing admitting to it.
    [Header("Text")]
    [SerializeField] private string emptyChatText = "Nothing said yet";

    [Header("Behaviour")]
    [SerializeField] private bool closeAfterSubmit;
    [SerializeField] private bool releaseFocusAfterSubmit = true;

    private IChatReadService readService;
    private IChatCommandService commandService;
    private IGameStateService stateMachine;
    private ILocalPlayerInputService localInputService;

    private VisualElement boundRoot;
    private VisualElement chatLayer;
    private ScrollView messages;
    private Label emptyLabel;
    private TextField input;

    private bool isOpen;
    private bool isInputFocused;
    private bool isSubscribedToEventChannel;
#if UNITY_EDITOR
    private bool warnedAboutRefusedOpen;
#endif

    public event Action Opened;
    public event Action Closed;

    public bool IsOpen => isOpen;
    public bool IsInputFocused => isInputFocused;
    public bool CanOpen => readService != null && readService.CanSubmitMessages;

    private void Awake()
    {
        if (document == null)
            document = GetComponent<UIDocument>();

        if (visibilityController == null)
            visibilityController = GetComponent<ChatVisibilityController>();

        if (sounds == null)
            sounds = GetComponent<UiDocumentSounds>();

        DetachFromParentDocument();
    }

    // A UIDocument that finds another one above it in the hierarchy stops being
    // a panel of its own: it is grafted into that document's tree, and its own
    // panel settings and sorting order are ignored outright.
    //
    // The lobby spawns its chat under the screen it is meant to float over -
    // that spawn point was chosen when the chat was a uGUI window and the
    // hierarchy meant nothing to it. Left there, this window would arrive as a
    // piece of the lobby screen and leave with it. It is neither: it is its own
    // panel, over the top, and it says so by standing on its own.
    private void DetachFromParentDocument()
    {
        Transform parent = transform.parent;

        if (parent == null || parent.GetComponentInParent<UIDocument>(true) == null)
            return;

        transform.SetParent(null, false);
    }

    // Quiet before anybody constructs it, and closed: a document builds its
    // tree in its own OnEnable and nothing guarantees a service ever arrives.
    private void OnEnable()
    {
        if (Bind())
            ApplyOpenState(false, false);

        SubscribeToEventChannel();
    }

    private void OnDisable()
    {
        UnsubscribeFromEventChannel();
    }

    private void OnDestroy()
    {
        if (isOpen)
            ApplyOpenState(false, true);
        else
            ReleaseInputFocus();

        UnsubscribeFromEventChannel();
        UnsubscribeFromServices();
    }

    public void Construct(
        IChatReadService readService,
        IChatCommandService commandService,
        IGameStateService stateMachine,
        ILocalPlayerInputService inputService)
    {
        UnsubscribeFromServices();

        this.readService = readService;
        this.commandService = commandService;
        this.stateMachine = stateMachine;
        SetLocalInputService(inputService);

        Bind();
        SubscribeToEventChannel();
        SubscribeToServices();

        SetInputFocusState(false, forcePlayerInputUpdate: true);
        ApplyOpenState(visibilityController != null && visibilityController.IsOpen, false);
        RefreshMessages();
    }

    public void SetEventChannel(ChatEventChannel chatEvents)
    {
        UnsubscribeFromEventChannel();
        this.chatEvents = chatEvents;
        SubscribeToEventChannel();

        if (visibilityController != null)
            visibilityController.SetEventChannel(chatEvents);
    }

    // Nothing to override. The old window hung a sound component off its input
    // field, because a uGUI field is a GameObject and can carry one. An element
    // here is not, so the whole document is listened to at once by
    // UiDocumentSounds - which already knows that a thing wearing .input makes
    // the typing sound when its text changes, and the click when it takes the
    // caret. A second source for that would be a second answer to a question
    // the screen has already answered for every other field in the game.
    public void SetInputSoundOverride(SoundEffect sound)
    {
    }

    public void SetLocalInputService(ILocalPlayerInputService inputService)
    {
        if (ReferenceEquals(localInputService, inputService))
            return;

        // Whatever this window was holding down for the old service, let go of
        // it: the player it belonged to is gone.
        localInputService?.SetInputActive(this, true);
        localInputService = inputService;

        if (isInputFocused)
            localInputService?.SetInputActive(this, false);
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
        {
            WarnAboutRefusedOpen();
            return;
        }

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

    // Refusing to open in silence leaves nothing to go on: the key does nothing
    // and the console stays empty. Said once, and only in the editor, because
    // the two reasons are both setup faults rather than things a player did.
    private void WarnAboutRefusedOpen()
    {
#if UNITY_EDITOR
        if (warnedAboutRefusedOpen)
            return;

        warnedAboutRefusedOpen = true;

        string reason = readService == null
            ? "no chat service was handed to this window - the session service " +
              "is missing or was never injected"
            : "the session says chat is unavailable, which it does outside the " +
              "lobby and a running game, and before the chat session has spawned";

        Debug.LogWarning($"Chat refused to open: {reason}.", this);
#endif
    }

    public void Close()
    {
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
        if (!isOpen || input == null)
            return;

        // Scheduled rather than immediate: the element has to be laid out
        // before it can take the caret, and the frame a chat is opened in is
        // the frame its layer stops being display:none.
        input.schedule.Execute(() =>
        {
            if (!isOpen || input == null)
                return;

            input.Focus();
            SetInputFocusState(true);
        });
    }

    public void ReleaseInputFocus()
    {
        input?.Blur();
        SetInputFocusState(false);
    }

    private bool Bind()
    {
        if (document == null)
            document = GetComponent<UIDocument>();

        VisualElement root = document != null ? document.rootVisualElement : null;

        if (root == null)
            return false;

        // Binding subscribes, so doing it twice would count every keystroke
        // twice; and a document that is switched off rebuilds its tree, which
        // makes the old references stale rather than merely duplicated.
        if (ReferenceEquals(root, boundRoot))
            return chatLayer != null;

        boundRoot = root;
        UiPreferences.Attach(root);

        chatLayer = root.Q<VisualElement>("Chat");
        messages = root.Q<ScrollView>("Messages");
        emptyLabel = root.Q<Label>("Empty");
        input = root.Q<TextField>("Input");

        if (emptyLabel != null)
            emptyLabel.text = emptyChatText;

        if (chatLayer == null)
        {
            Debug.LogError($"{nameof(LobbyChatDocument)} did not find 'Chat'.", this);
            return false;
        }

        if (input != null)
        {
            // Caught on the way down, before the field's own text element sees
            // it. A text field answers Return itself - it commits what has been
            // typed and hands the focus back - and by the time the key reaches
            // a listener on the field it has already been dealt with once. That
            // is what made this take two presses to send one message: the first
            // Return went into committing the edit and never reached here.
            input.RegisterCallback<KeyDownEvent>(HandleInputKeyDown, TrickleDown.TrickleDown);
            input.RegisterCallback<FocusInEvent>(_ => SetInputFocusState(true));
            input.RegisterCallback<FocusOutEvent>(_ => SetInputFocusState(false));
        }

        return true;
    }

    // Enter sends. Taken on the way down and stopped there, because a text
    // field that has already seen Return puts one in the string on a multiline
    // control and rings the system bell on a single-line one.
    private void HandleInputKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
            return;

        // Nothing else gets a look at it. Left to travel on, the same press
        // would also be a newline in the box and a submit on whatever the panel
        // thinks is the default action.
        evt.StopImmediatePropagation();
        SubmitCurrentMessage();
    }

    private void SubmitCurrentMessage()
    {
        if (input == null)
            return;

        string text = input.value;

        if (readService == null)
        {
            RaiseSendRejected(text, "Chat session is not ready.");
            return;
        }

        if (!readService.CanSubmitMessages)
        {
            RaiseSendRejected(text, "Chat is not available.");
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
            return;

        if (commandService == null)
        {
            RaiseSendRejected(text, "Chat session is not ready.");
            return;
        }

        commandService.SubmitMessage(text);
        input.SetValueWithoutNotify(string.Empty);

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

    private void RaiseSendRejected(string text, string reason)
    {
        if (chatEvents == null)
            return;

        ChatSendRequest request = new ChatSendRequest(
            text,
            readService != null ? readService.CurrentChannel.ToString() : string.Empty);

        chatEvents.RaiseSendRejected(new ChatSendRejectedEvent(request, reason));
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

    private void SyncVisibilityFromController()
    {
        if (visibilityController != null && visibilityController.IsOpen)
        {
            ApplyOpenState(true, false);
            return;
        }

        RefreshVisibility();
    }

    private void ApplyOpenState(bool shouldOpen, bool publishEvent)
    {
        if (shouldOpen && !CanOpen)
        {
            RefreshVisibility();
            return;
        }

        if (isOpen == shouldOpen)
        {
            RefreshVisibility();
            return;
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

            // The screen-level moments, said the way every other screen here
            // says them. Everything inside the window - the caret arriving, a
            // key being typed - belongs to UiDocumentSounds; opening and
            // closing belong to whoever does the opening, which is this.
            sounds?.Play(UiSoundType.Open);
            Opened?.Invoke();
        }
        else
        {
            ReleaseInputFocus();
            RefreshVisibility();
            sounds?.Play(UiSoundType.Close);
            Closed?.Invoke();
        }

        if (!publishEvent || chatEvents == null)
            return;

        ChatVisibilityState currentState = isOpen
            ? ChatVisibilityState.Open
            : ChatVisibilityState.Closed;

        chatEvents.RaiseVisibilityChanged(
            new ChatVisibilityChangedEvent(previousState, currentState));
    }

    private void RefreshVisibility()
    {
        // The same two steps every other layer in this interface is shown with:
        // display has to stay in the picture because a transparent chat still
        // takes the pointer, and a class added in the frame the element appears
        // never transitions.
        UiFade.Set(chatLayer, isOpen && CanOpen, OpenClass);
    }

    // Rebuilt rather than reconciled. A lobby chat holds tens of lines, not
    // thousands, and the old view kept a dictionary of message ids alive to
    // avoid rebuilding a list that costs nothing to rebuild.
    private void RefreshMessages()
    {
        if (messages == null)
            return;

        messages.Clear();

        int shown = 0;

        if (readService != null)
        {
            ChatChannel currentChannel = readService.CurrentChannel;

            for (int i = 0; i < readService.MessageCount; i++)
            {
                ChatMessageData message = readService.GetMessage(i);

                if (message.Channel != ChatChannel.System && message.Channel != currentChannel)
                    continue;

                messages.Add(BuildMessage(message));
                shown++;
            }

            // After the layout, not before it: scrolling to the bottom of a
            // list that has not been measured yet scrolls to the bottom of
            // nothing.
            messages.schedule.Execute(ScrollToBottom);
        }

        // Counted on the way in rather than read back off the list. A
        // ScrollView's own children are its viewport and its scrollers - what
        // was put into it lives one level down, in the content container, and
        // asking the wrong one gives an answer that is never nought.
        if (emptyLabel != null)
        {
            emptyLabel.style.display = shown == 0
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }
    }

    private VisualElement BuildMessage(ChatMessageData message)
    {
        bool isSystem = message.Channel == ChatChannel.System;
        bool isOwn = !isSystem && readService.IsLocalClient(message.SenderClientId);

        // A row of two labels, the way a roster row is built: the name sized to
        // itself, the sentence taking the rest and wrapping inside it. One
        // label with the name inside it would have to be one colour, or rich
        // text - and rich text hands every player a way to write markup into
        // somebody else's screen.
        VisualElement row = new VisualElement();
        row.AddToClassList(MessageClass);
        row.EnableInClassList(SystemMessageClass, isSystem);
        row.EnableInClassList(OwnMessageClass, isOwn);

        // The room speaking gets no name. A line with no author is a note, not
        // a message with the author left off.
        if (!isSystem)
        {
            string sender = message.SenderName.ToString();

            if (!string.IsNullOrWhiteSpace(sender))
            {
                // The colon belongs to the name and is coloured with it. It is
                // punctuation the name owns, not a separator sitting between
                // two labels - which is also why the gap after it is a space
                // rather than a margin wide enough to read as a column.
                Label name = new Label($"{sender}:") { enableRichText = false };
                name.AddToClassList(SenderClass);
                row.Add(name);
            }
        }

        Label text = new Label(message.Text.ToString()) { enableRichText = false };
        text.AddToClassList(TextClass);
        row.Add(text);

        return row;
    }

    private void ScrollToBottom()
    {
        if (messages?.contentContainer == null)
            return;

        messages.scrollOffset = new Vector2(
            messages.scrollOffset.x,
            messages.contentContainer.layout.height);
    }

    private void SetInputFocusState(bool value, bool forcePlayerInputUpdate = false)
    {
        if (isInputFocused == value && !forcePlayerInputUpdate)
            return;

        isInputFocused = value;

        // While the caret is in the chat, the player is typing rather than
        // walking. Anything else and every message moves the character.
        localInputService?.SetInputActive(this, !isInputFocused);
    }

    private void SubscribeToEventChannel()
    {
        if (isSubscribedToEventChannel || chatEvents == null)
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
}
