using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : MonoBehaviour
{
    public Action<Vector2> OnMoveUpdated;
    public Action OnCrouchUpdated;

    public void OnMove(InputAction.CallbackContext context)
    {
        OnMoveUpdated?.Invoke(context.ReadValue<Vector2>());
    }

    public void OnCrouch()
    {
        OnCrouchUpdated?.Invoke();
    }
} 
