using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public sealed class ChatWindowInput : MonoBehaviour
{
    [Header("References")]
    [FormerlySerializedAs("chatWindowUI")]
    [SerializeField] private MonoBehaviour chatWindowBehaviour;

    private IChatWindowView chatWindowUI;

    [Header("Input")]
    [FormerlySerializedAs("toggleChatAction")]
    [SerializeField] private InputActionReference openChatAction;
    [SerializeField] private InputActionReference closeChatAction;
    [SerializeField] private bool enableActionOnEnable = true;
    [FormerlySerializedAs("ignoreToggleWhileInputFocused")]
    [SerializeField] private bool ignoreOpenWhileInputFocused = true;
    [SerializeField] private bool closeOnlyWhenInputFocused = true;

    // What to press, read off the binding rather than written into a label: a
    // screen that says T while the asset says something else is a screen
    // nobody can correct.
    //
    // Taken from the path and not from the resolved control on purpose. A
    // control reports the character its key produces under the layout the
    // player is currently typing in, so <Keyboard>/t comes back as E on a
    // Cyrillic layout - true, and useless. The binding names a position on the
    // keyboard, and the letter printed on that key is what a prompt is for.
    public string OpenChatDisplayName => KeyboardKeyName(
        openChatAction != null ? openChatAction.action : null);

    private static string KeyboardKeyName(InputAction action)
    {
        if (action == null)
            return string.Empty;

        foreach (InputBinding binding in action.bindings)
        {
            if (binding.isComposite || binding.isPartOfComposite)
                continue;

            string path = binding.effectivePath;

            if (string.IsNullOrEmpty(path) || !path.StartsWith("<Keyboard>/"))
                continue;

            // The last segment of the path, which for a key is the key.
            string key = path.Substring(path.LastIndexOf('/') + 1);

            return key.Length == 0
                ? string.Empty
                : char.ToUpperInvariant(key[0]) + key.Substring(1);
        }

        return string.Empty;
    }

    private InputAction subscribedOpenAction;
    private InputAction subscribedCloseAction;
    private bool openActionEnabledByThisComponent;
    private bool closeActionEnabledByThisComponent;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        InputAction openAction = openChatAction != null ? openChatAction.action : null;
        InputAction closeAction = closeChatAction != null ? closeChatAction.action : null;

        if (openAction == null)
        {
            Debug.LogError("Open chat input action is not assigned.");
        }

        if (closeAction == null)
        {
            Debug.LogError("Close chat input action is not assigned.");
        }

        if (openAction != null && closeAction != null && openAction == closeAction)
        {
            Debug.LogError("Open and close chat input actions must be different.");
            return;
        }

        SubscribeAction(
            openAction,
            ref subscribedOpenAction,
            ref openActionEnabledByThisComponent,
            HandleOpenChatPerformed);

        SubscribeAction(
            closeAction,
            ref subscribedCloseAction,
            ref closeActionEnabledByThisComponent,
            HandleCloseChatPerformed);
    }

    private void Unsubscribe()
    {
        UnsubscribeAction(
            ref subscribedOpenAction,
            ref openActionEnabledByThisComponent,
            HandleOpenChatPerformed);

        UnsubscribeAction(
            ref subscribedCloseAction,
            ref closeActionEnabledByThisComponent,
            HandleCloseChatPerformed);
    }

    private void SubscribeAction(
        InputAction action,
        ref InputAction subscribedAction,
        ref bool actionEnabledByThisComponent,
        System.Action<InputAction.CallbackContext> callback)
    {
        if (action == null || subscribedAction == action)
            return;

        UnsubscribeAction(ref subscribedAction, ref actionEnabledByThisComponent, callback);

        subscribedAction = action;
        subscribedAction.performed += callback;

        if (enableActionOnEnable && !subscribedAction.enabled)
        {
            subscribedAction.Enable();
            actionEnabledByThisComponent = true;
        }
    }

    private void UnsubscribeAction(
        ref InputAction subscribedAction,
        ref bool actionEnabledByThisComponent,
        System.Action<InputAction.CallbackContext> callback)
    {
        if (subscribedAction == null)
            return;

        subscribedAction.performed -= callback;

        if (actionEnabledByThisComponent && subscribedAction.enabled)
            subscribedAction.Disable();

        subscribedAction = null;
        actionEnabledByThisComponent = false;
    }

    private void HandleOpenChatPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        if (!TryResolveChatWindowUI())
            return;

        if (ignoreOpenWhileInputFocused && chatWindowUI.IsInputFocused)
            return;

        chatWindowUI.Open();
    }

    private void HandleCloseChatPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        if (!TryResolveChatWindowUI())
            return;

        if (!chatWindowUI.IsOpen)
            return;

        if (closeOnlyWhenInputFocused && !chatWindowUI.IsInputFocused)
            return;

        PauseMenuInput.SuppressToggleForCurrentFrame();
        chatWindowUI.Close();
    }

    private bool TryResolveChatWindowUI()
    {
        ResolveReferences();

        if (chatWindowUI != null)
            return true;

        Debug.LogError("ChatWindowUI is missing.");
        return false;
    }

    private void ResolveReferences()
    {
        if (chatWindowUI == null)
            chatWindowUI = chatWindowBehaviour as IChatWindowView;

        if (chatWindowUI == null)
            chatWindowUI = GetComponent<IChatWindowView>();

        if (chatWindowUI == null)
            chatWindowUI = GetComponentInChildren<IChatWindowView>(true);

        if (chatWindowUI != null && chatWindowBehaviour == null)
            chatWindowBehaviour = chatWindowUI as MonoBehaviour;
    }
}
