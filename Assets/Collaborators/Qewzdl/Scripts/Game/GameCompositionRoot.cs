using UnityEngine;

public sealed class GameCompositionRoot : CompositionRoot
{
    [Header("Core")]
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private NetworkSessionOrchestrator sessionService;

    [Header("Pause")]
    [SerializeField] private GamePauseService pauseService;
    [SerializeField] private PauseMenuInput pauseMenuInput;
    [SerializeField] private PauseMenuUI pauseMenuUI;
    [SerializeField] private PauseCursorController pauseCursorController;

    [Header("Player")]
    [SerializeField] private MouseLook mouseLook;

    protected override void ResolveReferences()
    {
        if (stateMachine == null)
            stateMachine = FindFirstObjectByType<GameStateMachine>();

        if (sessionService == null)
            sessionService = NetworkSessionOrchestrator.Instance != null
                ? NetworkSessionOrchestrator.Instance
                : FindFirstObjectByType<NetworkSessionOrchestrator>();

        if (pauseService == null)
            pauseService = FindFirstObjectByType<GamePauseService>();

        if (pauseMenuInput == null)
            pauseMenuInput = FindFirstObjectByType<PauseMenuInput>();

        if (pauseMenuUI == null)
            pauseMenuUI = FindFirstObjectByType<PauseMenuUI>();

        if (pauseCursorController == null)
            pauseCursorController = FindFirstObjectByType<PauseCursorController>();

        if (mouseLook == null)
            mouseLook = FindFirstObjectByType<MouseLook>();
    }

    protected override void Compose()
    {
        if (pauseService == null)
        {
            Debug.LogError("GamePauseService was not found.");
            return;
        }

        pauseService.Construct(stateMachine);

        if (pauseMenuInput != null)
            pauseMenuInput.Construct(pauseService);

        if (pauseMenuUI != null)
            pauseMenuUI.Construct(pauseService, sessionService);

        if (pauseCursorController != null)
            pauseCursorController.Construct(pauseService);

        if (mouseLook != null)
            mouseLook.Construct(pauseService);
    }
}