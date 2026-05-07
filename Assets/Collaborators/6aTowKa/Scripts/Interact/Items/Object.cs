using UnityEngine;
using UnityEngine.InputSystem;

public abstract class Object : InteractableObject
{
    [SerializeField] protected float mass;

    protected GameObject holdPoint;
    protected bool isDragging;
    protected Rigidbody rb;
    private Vector3 previousPosition;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        holdPoint = new GameObject();
        rb.mass = mass;
    }

    public override void Interact(InteractionContext context)
    {
        if (context.HoldPoint == null) return;

        holdPoint.transform.position = context.HoldPoint.position;
        holdPoint.transform.parent = context.PlayerCameraTransform;

        StartDragging();
        print("Object interact!");
    }

    protected void Update()
    {
        //test
        if (Keyboard.current.eKey.wasReleasedThisFrame)
        {
            StopDragging();
        }
    }

    protected void FixedUpdate()
    {
        if (isDragging)
            Dragging();
    }

    protected void StartDragging()
    {
        isDragging = true;
        rb.useGravity = false;
    }

    protected void Dragging()
    {
        previousPosition = rb.position;
        rb.position = Vector3.Lerp(rb.position, holdPoint.transform.position, Time.fixedDeltaTime / mass);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        if (holdPoint != null)
            Gizmos.DrawSphere(holdPoint.transform.position, 0.1f);
    }

    protected void StopDragging()
    {
        isDragging = false;
        rb.linearVelocity = (rb.position - previousPosition) / Time.fixedDeltaTime;
        rb.useGravity = true;
    }
}
