using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : PlayerComponent, ILocalPlayerInputService
{
    // The one action this class subscribes to itself rather than being called
    // about. Every other action is wired to a method on this component through
    // the input component's own event list, and crouch cannot be: it needs to
    // know the difference between the key going down and coming back up, and
    // the wiring for that lives in a serialised list nobody can read in a diff
    // - which is where the last version of this went wrong, silently, in a
    // build that compiled.
    private const string CrouchActionName = "Crouch";
    private const string RunActionName = "Run";

    [SerializeField] private CameraLook cameraLook;
    [SerializeField] private PlayerInput playerInput;

    private readonly HashSet<object> inputBlockers = new();

    private bool inputActive = true;
    private bool isMovingInput = false;
    private bool isLocalControl = false;

    private Vector2 lastMoveInputDirection;
    private InputAction crouchAction;
    private InputAction runAction;

    private void OnEnable()
    {
        SubscribeToCrouch();
        SubscribeToRun();
    }

    private void OnDisable()
    {
        UnsubscribeFromCrouch();
        UnsubscribeFromRun();

        if (cameraLook != null)
            cameraLook.SetLookActive(this, true);
    }

    private void OnDestroy()
    {
        UnsubscribeFromCrouch();
        UnsubscribeFromRun();
    }

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        isLocalControl = !isMultiplayer || isOwner;

        if (!isLocalControl)
            return;

        if (cameraLook == null)
        {
            Debug.LogError($"{nameof(PlayerInputHandler)} requires assigned {nameof(CameraLook)} for local look blocking.", this);
            return;
        }

        cameraLook.SetLookActive(this, inputActive);

        if (playerInput == null)
            playerInput = GetComponent<PlayerInput>();

        if (playerInput == null || playerInput.actions == null)
        {
            Debug.LogError(
                $"{nameof(PlayerInputHandler)} found no {nameof(PlayerInput)} to read crouch from.",
                this);
            return;
        }

        // Each one resolved and reported on its own, and none of them allowed
        // to end the method. Bailing out on the first miss is how adding a
        // second action took the first one down with it: a project without Run
        // lost crouch as well, and the log said only that Run was missing.
        crouchAction = ResolveAction(CrouchActionName);
        runAction = ResolveAction(RunActionName);

        SubscribeToCrouch();
        SubscribeToRun();
    }

    private InputAction ResolveAction(string actionName)
    {
        InputAction action = playerInput.actions.FindAction(actionName);

        if (action == null)
        {
            Debug.LogError(
                $"{nameof(PlayerInputHandler)} found no '{actionName}' action.",
                this);
        }

        return action;
    }

    // Both edges, for the reason crouch gives above: performed repeats, and a
    // run that is re-started every frame it is held would re-trigger whatever
    // eventually listens for the start of one.
    private void SubscribeToRun()
    {
        if (runAction == null)
            return;

        UnsubscribeFromRun();

        runAction.started += HandleRunInput;
        runAction.canceled += HandleRunInput;
    }

    private void UnsubscribeFromRun()
    {
        if (runAction == null)
            return;

        runAction.started -= HandleRunInput;
        runAction.canceled -= HandleRunInput;
    }

    private void HandleRunInput(InputAction.CallbackContext context)
    {
        if (!inputActive || !isLocalControl)
            return;

        if (context.started)
            signals.RunInputSignal.Trigger(true);
        else if (context.canceled)
            signals.RunInputSignal.Trigger(false);
    }

    // Both edges, and only the edges. Started is the key going down and
    // cancelled is it coming back up; performed sits between them and repeats,
    // which is what made the old wiring fire a stance change three times for
    // one press.
    private void SubscribeToCrouch()
    {
        if (crouchAction == null)
            return;

        UnsubscribeFromCrouch();

        crouchAction.started += HandleCrouchInput;
        crouchAction.canceled += HandleCrouchInput;
    }

    private void UnsubscribeFromCrouch()
    {
        if (crouchAction == null)
            return;

        crouchAction.started -= HandleCrouchInput;
        crouchAction.canceled -= HandleCrouchInput;
    }

    private void HandleCrouchInput(InputAction.CallbackContext context)
    {
        if (!inputActive || !isLocalControl)
            return;

        if (context.started)
            signals.CrouchInputSignal.Trigger(true);
        else if (context.canceled)
            signals.CrouchInputSignal.Trigger(false);
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
