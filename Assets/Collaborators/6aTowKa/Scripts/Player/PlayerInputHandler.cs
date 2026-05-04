using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : PlayerComponent
{
    private bool inputActive = true;

    public void OnMove(InputAction.CallbackContext context)
    {
        if (inputActive)
            signals.MoveSignal.Trigger(context.ReadValue<Vector2>());
    }

    public void OnCrouch()
    {
        if (inputActive)
            signals.CrouchInputSignal.Trigger();
    }    
    
    public void SetInputActive(bool value)
    {
        inputActive = value;

        if (inputActive == false)
            signals.MoveSignal.Trigger(Vector2.zero);
    }
} 
