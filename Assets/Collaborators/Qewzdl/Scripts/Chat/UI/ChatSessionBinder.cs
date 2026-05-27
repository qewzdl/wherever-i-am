using UnityEngine;

public class ChatSessionBinder : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ChatWindowUI chatWindow;
    [SerializeField] private ChatNotificationController notificationController;
    [SerializeField] private GameStateMachine stateMachine;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        NetworkChatSession.SessionSpawned += Bind;
        NetworkChatSession.SessionDespawned += Unbind;

        if (NetworkChatSession.Instance != null)
            Bind(NetworkChatSession.Instance);
    }

    private void OnDisable()
    {
        NetworkChatSession.SessionSpawned -= Bind;
        NetworkChatSession.SessionDespawned -= Unbind;
    }

    private void Bind(NetworkChatSession session)
    {
        ResolveReferences();

        if (chatWindow != null)
            chatWindow.Construct(session, stateMachine);

        if (notificationController != null)
            notificationController.Construct(session, chatWindow);
    }

    private void Unbind()
    {
        ResolveReferences();

        if (chatWindow != null)
            chatWindow.Construct(null, stateMachine);

        if (notificationController != null)
            notificationController.Construct(null, chatWindow);
    }

    private void ResolveReferences()
    {
        if (chatWindow == null)
            chatWindow = GetComponentInChildren<ChatWindowUI>(true);

        if (notificationController == null)
            notificationController = GetComponentInChildren<ChatNotificationController>(true);

        if (stateMachine == null)
            stateMachine = GetComponent<GameStateMachine>();

        if (stateMachine == null)
            stateMachine = FindFirstObjectByType<GameStateMachine>();
    }
}
