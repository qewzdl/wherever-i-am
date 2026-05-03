using UnityEngine;

public sealed class PauseCursorController : MonoBehaviour
{
    private IPauseService pauseService;

    public void Construct(IPauseService pauseService)
    {
        this.pauseService = pauseService;

        if (this.pauseService != null)
            this.pauseService.PauseStateChanged += HandlePauseStateChanged;

        HandlePauseStateChanged(this.pauseService != null && this.pauseService.IsPaused);
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