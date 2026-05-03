using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInput : MonoBehaviour
{
    public Action<Vector2> OnMoveUpdated;
    public Action OnCrouchUpdated;

    public void OnMove(InputAction.CallbackContext context)
    {
        OnMoveUpdated?.Invoke(context.ReadValue<Vector2>());
        print("see");
    }

    public void OnCrouch()
    {
        OnCrouchUpdated?.Invoke();
    }
}
