using UnityEngine;

public sealed class ChatWindowLifecycleService : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private ChatWindowUI chatWindowPrefab;

    private ChatWindowUI activeWindow;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        NetworkChatSession.SessionSpawned += HandleChatSessionSpawned;
        NetworkChatSession.SessionDespawned += HandleChatSessionDespawned;

        if (stateMachine != null)
            stateMachine.StateChanged += HandleGameStateChanged;

        RefreshLifecycle();
    }

    private void OnDisable()
    {
        NetworkChatSession.SessionSpawned -= HandleChatSessionSpawned;
        NetworkChatSession.SessionDespawned -= HandleChatSessionDespawned;

        if (stateMachine != null)
            stateMachine.StateChanged -= HandleGameStateChanged;

        DestroyWindow();
    }

    private void OnDestroy()
    {
        DestroyWindow();
    }

    private void HandleChatSessionSpawned(NetworkChatSession chatSession)
    {
        RefreshLifecycle();
    }

    private void HandleChatSessionDespawned()
    {
        DestroyWindow();
    }

    private void HandleGameStateChanged(GameState previousState, GameState newState)
    {
        RefreshLifecycle();
    }

    private void RefreshLifecycle()
    {
        ResolveReferences();

        if (ShouldHaveChatWindow())
        {
            EnsureWindow();
            return;
        }

        DestroyWindow();
    }

    private bool ShouldHaveChatWindow()
    {
        if (NetworkChatSession.Instance == null)
            return false;

        if (stateMachine == null)
            return false;

        switch (stateMachine.CurrentState)
        {
            case GameState.Lobby:
            case GameState.LoadingGame:
            case GameState.InGame:
                return true;

            default:
                return false;
        }
    }

    private void EnsureWindow()
    {
        NetworkChatSession chatSession = NetworkChatSession.Instance;

        if (chatSession == null)
            return;

        if (activeWindow != null)
        {
            activeWindow.Construct(chatSession, chatSession, stateMachine);
            return;
        }

        if (chatWindowPrefab == null)
        {
            Debug.LogError("ChatWindowUI prefab is not assigned.");
            return;
        }

        activeWindow = Instantiate(chatWindowPrefab);
        activeWindow.name = "ChatWindowCanvas";

        DontDestroyOnLoad(activeWindow.gameObject);

        activeWindow.Construct(chatSession, chatSession, stateMachine);
    }

    private void DestroyWindow()
    {
        if (activeWindow == null)
            return;

        Destroy(activeWindow.gameObject);
        activeWindow = null;
    }

    private void ResolveReferences()
    {
        if (stateMachine == null)
            stateMachine = GetComponent<GameStateMachine>();

        if (stateMachine == null)
            stateMachine = FindFirstObjectByType<GameStateMachine>();
    }
}