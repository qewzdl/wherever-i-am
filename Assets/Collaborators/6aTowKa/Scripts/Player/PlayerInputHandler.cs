using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : PlayerComponent
{
    public static PlayerInputHandler Active { get; private set; }

    [SerializeField] private CameraLook cameraLook;

    private readonly HashSet<object> inputBlockers = new();

    private bool inputActive = true;
    private bool isMovingInput = false;
    private bool isLocalControl = false;

    private Vector2 lastMoveInputDirection;

    private void OnEnable()
    {
        if (isLocalControl)
            Active = this;
    }

    private void OnDisable()
    {
        if (Active == this)
            Active = null;

        if (cameraLook != null)
            cameraLook.SetLookActive(this, true);
    }

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        isLocalControl = !isMultiplayer || isOwner;

        if (!isLocalControl)
        {
            if (Active == this)
                Active = null;

            return;
        }

        Active = this;

        if (cameraLook == null)
        {
            Debug.LogError($"{nameof(PlayerInputHandler)} requires assigned {nameof(CameraLook)} for local look blocking.", this);
            return;
        }

        cameraLook.SetLookActive(this, inputActive);
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        Vector2 direction = context.ReadValue<Vector2>();

        if (context.started)
            isMovingInput = true;
        else if (context.canceled)
            isMovingInput = false;

        if (inputActive)
            signals.MoveSignal.Trigger(direction);

        if (isMovingInput)
            lastMoveInputDirection = direction;
    }

    public void OnCrouch()
    {
        if (!inputActive)
            return;

        signals.CrouchInputSignal.Trigger();
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (!inputActive)
            return;

        if (context.started)
            signals.Interact.Trigger();

        if (context.canceled)
            signals.Uninteract.Trigger();
    }

    public void OnPickUp(InputAction.CallbackContext context)
    {
        if (!inputActive)
            return;

        if (context.started)
            signals.PickUp.Trigger();
    }

    public void OnDrop(InputAction.CallbackContext context)
    {
        if (!inputActive)
            return;

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

        if (cameraLook != null)
            cameraLook.SetLookActive(this, inputActive);

        if (inputActive == false && signals != null)
        {
            signals.MoveSignal.Trigger(Vector2.zero);
            return;
        }

        if (inputActive == true && signals != null && isMovingInput)
            signals.MoveSignal.Trigger(lastMoveInputDirection);
    }
}