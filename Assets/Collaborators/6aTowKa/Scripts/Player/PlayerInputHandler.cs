using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : PlayerComponent
{
    public static PlayerInputHandler Active { get; private set; }

    private readonly HashSet<object> inputBlockers = new();
    private bool inputActive = true;

    private void OnEnable()
    {
        Active = this;
    }

    private void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

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
        inputBlockers.Clear();
        ApplyInputActive(value);
    }

    public void SetInputActive(object source, bool value)
    {
        if (source == null)
        {
            SetInputActive(value);
            return;
        }

        if (value)
            inputBlockers.Remove(source);
        else
            inputBlockers.Add(source);

        ApplyInputActive(inputBlockers.Count == 0);
    }

    private void ApplyInputActive(bool value)
    {
        if (inputActive == value)
            return;

        inputActive = value;

        if (inputActive == false && signals != null)
            signals.MoveSignal.Trigger(Vector2.zero);
    }
} 
