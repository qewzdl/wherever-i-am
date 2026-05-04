using UnityEngine;
using UnityEngine.UI;

public sealed class PauseMenuUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private PlayerInputHandler playerInputHandler;

    private IPauseService pauseService;
    private INetworkSessionService sessionService;

    public void Construct(
        IPauseService pauseService,
        INetworkSessionService sessionService)
    {
        Unsubscribe();

        this.pauseService = pauseService;
        this.sessionService = sessionService;

        Subscribe();
        HandlePauseStateChanged(this.pauseService != null && this.pauseService.IsPaused);
    }

    private void OnDestroy()
    {
        if (pauseService != null && pauseService.IsPaused)
            SetPlayerInputActive(true);

        Unsubscribe();
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
