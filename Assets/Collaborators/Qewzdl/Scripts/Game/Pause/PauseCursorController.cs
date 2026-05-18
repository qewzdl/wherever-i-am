using UnityEngine;

public sealed class PauseCursorController : MonoBehaviour, IPauseServiceConsumer
{
    private IPauseService pauseService;

    public void Construct(IPauseService pauseService)
    {
        if (this.pauseService != null)
            this.pauseService.PauseStateChanged -= HandlePauseStateChanged;

        this.pauseService = pauseService;

        if (this.pauseService != null)
            this.pauseService.PauseStateChanged += HandlePauseStateChanged;

        HandlePauseStateChanged(this.pauseService != null && this.pauseService.IsPaused);
    }

    public void BindPauseService(IPauseService pauseService)
    {
        Construct(pauseService);
    }

    private void OnDestroy()
    {
        if (pauseService != null)
            pauseService.PauseStateChanged -= HandlePauseStateChanged;

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
