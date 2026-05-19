using UnityEngine;

public sealed class PauseCursorController : PauseServiceConsumer
{
    public void Construct(IPauseService pauseService)
    {
        BindPauseService(pauseService);
    }

    private void Start()
    {
        if (PauseService == null)
            LockCursor();
    }

    protected override void OnAfterPauseServiceBound(IPauseService pauseService)
    {
        pauseService.PauseStateChanged += HandlePauseStateChanged;
        HandlePauseStateChanged(pauseService.IsPaused);
    }

    protected override void OnBeforePauseServiceUnbound(IPauseService pauseService)
    {
        pauseService.PauseStateChanged -= HandlePauseStateChanged;
    }

    protected override void OnPauseServiceRebound(IPauseService pauseService)
    {
        if (pauseService != null)
            HandlePauseStateChanged(pauseService.IsPaused);
    }

    private void OnDestroy()
    {
        ClearPauseService();
        UnlockCursor();
    }

    private void HandlePauseStateChanged(bool isPaused)
    {
        if (isPaused)
        {
            UnlockCursor();
            return;
        }

        LockCursor();
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
