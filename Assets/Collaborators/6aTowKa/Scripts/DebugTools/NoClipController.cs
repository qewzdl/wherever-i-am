#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Временное dev-движение. Получает уже известного локального игрока, объекты не ищет.</summary>
public sealed class NoClipController
{
    private PlayerController player;
    private Rigidbody body;
    private bool originalUseGravity;
    private bool originalIsKinematic;
    private bool originalDetectCollisions;

    public bool IsEnabled { get; private set; }
    public float Speed { get; set; } = 10f;

    public bool SetEnabled(PlayerController localPlayer, bool enabled)
    {
        if (!enabled)
        {
            Restore();
            return true;
        }

        if (IsEnabled)
            return true;

        if (localPlayer == null || !localPlayer.TryGetComponent(out Rigidbody rigidbody))
            return false;

        player = localPlayer;
        body = rigidbody;
        originalUseGravity = body.useGravity;
        originalIsKinematic = body.isKinematic;
        originalDetectCollisions = body.detectCollisions;
        player.SetMovementActive(this, false);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.detectCollisions = false;
        body.isKinematic = true;
        IsEnabled = true;
        return true;
    }

    public void Update(float unscaledDeltaTime)
    {
        if (!IsEnabled)
            return;

        if (player == null || body == null || !player.isActiveAndEnabled)
        {
            Restore();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(player.transform.right, Vector3.up).normalized;
        Vector3 direction = Vector3.zero;
        if (keyboard.wKey.isPressed) direction += forward;
        if (keyboard.sKey.isPressed) direction -= forward;
        if (keyboard.dKey.isPressed) direction += right;
        if (keyboard.aKey.isPressed) direction -= right;
        if (keyboard.spaceKey.isPressed) direction += Vector3.up;
        if (keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed) direction -= Vector3.up;

        float multiplier = keyboard.leftShiftKey.isPressed ? 2.5f : 1f;
        player.transform.position += direction.normalized * Speed * multiplier * unscaledDeltaTime;
    }

    public void Restore()
    {
        if (player != null)
            player.SetMovementActive(this, true);

        if (body != null)
        {
            body.isKinematic = originalIsKinematic;
            body.detectCollisions = originalDetectCollisions;
            body.useGravity = originalUseGravity;
            body.linearVelocity = Vector3.zero;
        }

        player = null;
        body = null;
        IsEnabled = false;
    }
}
#endif
