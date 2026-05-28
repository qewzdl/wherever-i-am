using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : PlayerComponent, IPlayerSignalListener
{
    [SerializeField] private float speed;
    [SerializeField] private float moveInputDeadZone = 0.1f;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private float blockingContactMaxY = 0.7f;

    [Header("Gravity Settings")]
    public float gravityMultiplier = 1f;

    [Header("Ground Check")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 0.1f;

    //[SerializeField] private float speedToCrouch;

    private Vector2 direction;
    private bool isCrouching = false;
    private readonly List<Vector3> blockingContactNormals = new(8);

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
        blockingContactNormals.Clear();
    }

    private void Move()
    {
        Vector3 localDirection = new Vector3(direction.x, 0, direction.y);

        Vector3 worldDirection = rb.rotation * localDirection;
        Vector3 horizontalVelocity = worldDirection * speed;
        horizontalVelocity = ClipVelocityAgainstBlockingContacts(horizontalVelocity);

        rb.linearVelocity = new Vector3(horizontalVelocity.x, rb.linearVelocity.y, horizontalVelocity.z);
    }   

    private Vector3 ClipVelocityAgainstBlockingContacts(Vector3 velocity)
    {
        for (int i = 0; i < blockingContactNormals.Count; i++)
        {
            Vector3 normal = blockingContactNormals[i];
            float intoSurfaceSpeed = Vector3.Dot(velocity, normal);

            if (intoSurfaceSpeed < 0f)
                velocity -= normal * intoSurfaceSpeed;
        }

        velocity.y = 0f;
        return velocity;
    }

    private void OnCollisionEnter(Collision collision)
    {
        CacheBlockingContactNormals(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        CacheBlockingContactNormals(collision);
    }

    private void CacheBlockingContactNormals(Collision collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector3 normal = collision.GetContact(i).normal;

            if (normal.y > blockingContactMaxY)
                continue;

            normal.y = 0f;

            if (normal.sqrMagnitude <= Mathf.Epsilon)
                continue;

            blockingContactNormals.Add(normal.normalized);
        }
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
