using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PauseMenuInput : MonoBehaviour
{
    private IPauseService pauseService;

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

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            pauseService.TogglePause();
    }
}