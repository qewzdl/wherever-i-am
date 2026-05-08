using UnityEngine;
using UnityEngine.UI;

public sealed class ChatWindowLifecycleService : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private Transform uiRoot;
    [SerializeField] private ChatWindowUI chatWindowPrefab;

    [Header("Canvas Fallback")]
    [SerializeField] private bool createCanvasForPanelPrefab = true;

    private ChatWindowUI activeWindow;
    private GameObject activeCanvasRoot;

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

        Transform parent = ResolveSpawnParent();

        activeWindow = parent != null
            ? Instantiate(chatWindowPrefab, parent)
            : Instantiate(chatWindowPrefab);

        activeWindow.name = "ChatWindow";

        if (activeCanvasRoot == null && activeWindow.transform.parent == null)
            DontDestroyOnLoad(activeWindow.gameObject);

        activeWindow.Construct(chatSession, chatSession, stateMachine);
    }

    private void DestroyWindow()
    {
        if (activeWindow == null && activeCanvasRoot == null)
            return;

        if (activeCanvasRoot != null)
            Destroy(activeCanvasRoot);
        else if (activeWindow != null)
            Destroy(activeWindow.gameObject);

        activeWindow = null;
        activeCanvasRoot = null;
    }

    private Transform ResolveSpawnParent()
    {
        if (uiRoot != null)
            return uiRoot;

        if (!createCanvasForPanelPrefab || PrefabProvidesCanvas())
            return null;

        activeCanvasRoot = CreateCanvasRoot();
        DontDestroyOnLoad(activeCanvasRoot);

        return activeCanvasRoot.transform;
    }

    private bool PrefabProvidesCanvas()
    {
        return chatWindowPrefab != null && chatWindowPrefab.GetComponentInParent<Canvas>() != null;
    }

    private GameObject CreateCanvasRoot()
    {
        GameObject canvasObject = new GameObject(
            "ChatWindowCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler canvasScaler = canvasObject.GetComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        canvasScaler.scaleFactor = 1f;
        canvasScaler.referencePixelsPerUnit = 100f;

        return canvasObject;
    }

    private void ResolveReferences()
    {
        if (stateMachine == null)
            stateMachine = GetComponent<GameStateMachine>();

        if (stateMachine == null)
            stateMachine = FindFirstObjectByType<GameStateMachine>();
    }
}
