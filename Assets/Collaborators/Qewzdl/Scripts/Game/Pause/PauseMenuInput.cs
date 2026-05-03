using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PauseMenuInput : MonoBehaviour
{
    private static int suppressToggleFrame = -1;

    private IPauseService pauseService;

    public static void SuppressToggleForCurrentFrame()
    {
        suppressToggleFrame = Time.frameCount;
    }

    public void Construct(IPauseService pauseService)
    {
        this.pauseService = pauseService;
    }

    private void Update()
    {
        if (pauseService == null)
            return;

        if (Keyboard.current == null)
            return;

        if (suppressToggleFrame == Time.frameCount)
            return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            pauseService.TogglePause();
    }
}
