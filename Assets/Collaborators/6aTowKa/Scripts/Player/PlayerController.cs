using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : PlayerComponent, IPlayerSignalListener
{
    [SerializeField] private float speed;
    [SerializeField] private float moveInputDeadZone = 0.1f;
    [SerializeField] private Rigidbody rb;

    [Header("Gravity Settings")]
    public float gravityMultiplier = 1f;

    [Header("Ground Check")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 0.1f;

    //[SerializeField] private float speedToCrouch;

    private Vector2 direction;
    private bool isCrouching = false;

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        signals.MoveSignal.Listen(SetDirection);
        signals.CrouchInputSignal.Listen(UpdateIsCrouching);
    }

    public void Cleanup()
    {
        signals.MoveSignal.Unlisten(SetDirection);
        signals.CrouchInputSignal.Unlisten(UpdateIsCrouching);
    }

    private void FixedUpdate()
    {
        Move();
    }

    private void Move()
    {
        Vector3 localDirection = new Vector3(direction.x, 0, direction.y);

        Vector3 worldDirection = rb.rotation * localDirection;
        Vector3 horizontalVelocity = worldDirection * speed;
        rb.linearVelocity = new Vector3(horizontalVelocity.x, rb.linearVelocity.y, horizontalVelocity.z);
    }   

    public void SetDirection(Vector2 value)
    {
        direction = value.sqrMagnitude < moveInputDeadZone * moveInputDeadZone
            ? Vector2.zero
            : Vector2.ClampMagnitude(value, 1f);
    }

    public void UpdateIsCrouching()
    {
        isCrouching = !isCrouching;
        signals.CrouchUpdateSignal.Trigger(isCrouching);
    }

    public void SetSpeed(float newSpeed)
    {
        speed = newSpeed;
    }

    public float GetSpeed()
    {
        return speed;
    }
}
