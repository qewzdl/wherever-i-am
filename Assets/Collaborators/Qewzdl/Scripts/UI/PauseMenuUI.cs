using UnityEngine;
using UnityEngine.UI;

public sealed class PauseMenuUI : MonoBehaviour, IPauseServiceConsumer
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private HUDUI hudUI;

    private IPauseService pauseService;
    private INetworkSessionService sessionService;
    private IPlayerScopeRegistry playerScopes;
    private ILocalPlayerInputService localInputService;

    public void Construct(
        IPauseService pauseService,
        INetworkSessionService sessionService,
        IPlayerScopeRegistry playerScopeRegistry)
    {
        Unsubscribe();

        this.pauseService = pauseService;
        this.sessionService = sessionService;
        playerScopes = playerScopeRegistry;

        Subscribe();
        RefreshLocalInputService();
        HandlePauseStateChanged(this.pauseService != null && this.pauseService.IsPaused);
    }

    public void BindPauseService(IPauseService pauseService)
    {
        Construct(pauseService, sessionService, playerScopes);
    }

    public void Dispose()
    {
        if (pauseService != null && pauseService.IsPaused)
            SetPlayerInputActive(true);

        Unsubscribe();
        Hide();
        pauseService = null;
        sessionService = null;
        playerScopes = null;
        localInputService = null;
    }

    private void OnDestroy()
    {
        Dispose();
    }

    private void Subscribe()
    {
        if (resumeButton != null)
            resumeButton.onClick.AddListener(Resume);

        if (mainMenuButton != null)
            mainMenuButton.onClick.AddListener(ReturnToMainMenu);

        if (quitButton != null)
            quitButton.onClick.AddListener(QuitGame);

        if (pauseService != null)
            pauseService.PauseStateChanged += HandlePauseStateChanged;

        if (playerScopes != null && !playerScopes.IsDisposed)
        {
            playerScopes.PlayerScopeOpened += HandlePlayerScopeOpened;
            playerScopes.PlayerScopeClosing += HandlePlayerScopeClosing;
        }
    }

    private void Unsubscribe()
    {
        if (resumeButton != null)
            resumeButton.onClick.RemoveListener(Resume);

        if (mainMenuButton != null)
            mainMenuButton.onClick.RemoveListener(ReturnToMainMenu);

        if (quitButton != null)
            quitButton.onClick.RemoveListener(QuitGame);

        if (pauseService != null)
            pauseService.PauseStateChanged -= HandlePauseStateChanged;

        if (playerScopes != null)
        {
            playerScopes.PlayerScopeOpened -= HandlePlayerScopeOpened;
            playerScopes.PlayerScopeClosing -= HandlePlayerScopeClosing;
        }
    }

    private void HandlePauseStateChanged(bool isPaused)
    {
        if (isPaused)
            Show();
        else
            Hide();

        SetPlayerInputActive(!isPaused);
    }

    private void Resume()
    {
        pauseService?.Resume();
    }

    private void ReturnToMainMenu()
    {
        sessionService?.ShutdownToMainMenu();
    }

    private void QuitGame()
    {
        Application.Quit();
    }

    private void Show()
    {
        if (root != null)
            root.SetActive(true);

        if (hudUI != null)
            hudUI.HideHUD();
    }

    private void Hide()
    {
        if (root != null)
            root.SetActive(false);

        if (hudUI != null)
            hudUI.ShowHUD();    
    }

    private void SetPlayerInputActive(bool value)
    {
        localInputService?.SetInputActive(this, value);
    }

    private void HandlePlayerScopeOpened(IPlayerScope playerScope)
    {
        if (playerScope == null || !playerScope.IsLocalPlayer)
            return;

        RefreshLocalInputService();
        SetPlayerInputActive(pauseService == null || !pauseService.IsPaused);
    }

    private void HandlePlayerScopeClosing(IPlayerScope playerScope)
    {
        if (playerScope == null || !playerScope.IsLocalPlayer)
            return;

        localInputService?.SetInputActive(this, true);
        localInputService = null;
    }

    private void RefreshLocalInputService()
    {
        localInputService = null;

        if (playerScopes == null ||
            playerScopes.IsDisposed ||
            !playerScopes.TryGetLocalPlayerScope(out IPlayerScope playerScope) ||
            playerScope.LocalServices == null)
        {
            return;
        }

        playerScope.LocalServices.TryResolve(out localInputService);
    }
}
