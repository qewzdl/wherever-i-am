using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : PlayerComponent
{
    public static PlayerInputHandler Active { get; private set; }

    private readonly HashSet<object> inputBlockers = new();
    private bool inputActive = true;
    private bool isMovingInput = false;
    private Vector2 lastMoveInputDirection;

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
        Vector2 direction = context.ReadValue<Vector2>();

        if (context.started)
            isMovingInput = true;
        else if (context.canceled)
            isMovingInput = false;

        if (inputActive)
        {
            signals.MoveSignal.Trigger(direction);
        }

        if (isMovingInput)
            lastMoveInputDirection = direction;
    }

    public void OnCrouch()
    {
        if (!inputActive) return; 

        signals.CrouchInputSignal.Trigger();
    }  
    
    public void OnInteract(InputAction.CallbackContext context)
    {
        if (!inputActive) return;

        if (context.started)
            signals.Interact.Trigger();

        if (context.canceled) 
            signals.Uninteract.Trigger();
    }

    public void OnPickUp(InputAction.CallbackContext context)
    {
        if (!inputActive) return;

        if (context.started)
            signals.PickUp.Trigger();
    }

    public void OnDrop(InputAction.CallbackContext context)
    {
        if (!inputActive) return;

        if (context.started)
            signals.Drop.Trigger();
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
        else if (inputActive == true && signals != null && isMovingInput)
        {
            signals.MoveSignal.Trigger(lastMoveInputDirection);
        }
    }
} 
