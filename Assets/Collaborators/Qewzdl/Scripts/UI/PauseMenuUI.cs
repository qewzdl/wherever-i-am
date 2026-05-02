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

    private IPauseService pauseService;
    private INetworkSessionService sessionService;

    public void Construct(
        IPauseService pauseService,
        INetworkSessionService sessionService)
    {
        this.pauseService = pauseService;
        this.sessionService = sessionService;

        Subscribe();
        Hide();
    }

    private void OnDestroy()
    {
        Unsubscribe();

        if (pauseService != null)
            pauseService.PauseStateChanged -= HandlePauseStateChanged;
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
    }

    private void HandlePauseStateChanged(bool isPaused)
    {
        if (isPaused)
            Show();
        else
            Hide();
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
}