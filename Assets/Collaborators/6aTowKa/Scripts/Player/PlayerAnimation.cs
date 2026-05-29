using UnityEngine;

public class PlayerAnimation : PlayerComponent, IPlayerSignalListener
{
    [Header("Physical Crouch")]
    [SerializeField] private CapsuleCollider bodyCollider;
    [SerializeField] private float crouchColliderHeight = 1f;
    [SerializeField] private float standColliderHeight = 2f;
    [SerializeField] private Vector3 crouchColliderCenter = new Vector3(0f, -0.5f, 0f);
    [SerializeField] private Vector3 standColliderCenter = Vector3.zero;

    private bool missingColliderLogged;

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        signals.CrouchUpdateSignal.Listen(SetupAnimation);

        if (isMultiplayer && !isOwner)
            signals.CrouchSyncSignal.Listen(SetupAnimation);

        ApplyColliderState(false);
    }

    public void Cleanup()
    {
        signals.CrouchUpdateSignal.Unlisten(SetupAnimation);
        signals.CrouchSyncSignal.Unlisten(SetupAnimation);
    }

    public void SetBodyCollider(CapsuleCollider collider)
    {
        bodyCollider = collider;
    }

    public void SetupAnimation(bool isCrouching)
    {
        ApplyColliderState(isCrouching);
    }

    private void ApplyColliderState(bool isCrouching)
    {
        if (bodyCollider == null)
        {
            if (!missingColliderLogged)
            {
                Debug.LogError($"{nameof(PlayerAnimation)} requires assigned {nameof(CapsuleCollider)} for physical crouch.", this);
                missingColliderLogged = true;
            }

            return;
        }

        bodyCollider.height = Mathf.Max(0.01f, isCrouching ? crouchColliderHeight : standColliderHeight);
        bodyCollider.center = isCrouching ? crouchColliderCenter : standColliderCenter;
    }
}
