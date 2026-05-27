using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PauseMenuInput : PauseServiceConsumer
{
    private static int suppressToggleFrame = -1;

    public static void SuppressToggleForCurrentFrame()
    {
        suppressToggleFrame = Time.frameCount;
    }

    public void Construct(IPauseService pauseService)
    {
        BindPauseService(pauseService);
    }

    private void Update()
    {
        if (PauseService == null)
            return;

        if (Keyboard.current == null)
            return;

        if (suppressToggleFrame == Time.frameCount)
            return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            PauseService.TogglePause();
    }
}