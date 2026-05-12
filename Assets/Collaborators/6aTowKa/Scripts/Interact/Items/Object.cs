using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public abstract class Object : InteractableObject
{
    [SerializeField] protected float mass;
   
    protected GameObject holdPoint;
    protected float maxDraggingDistance = 8f;
    protected bool isDragging;
    protected Rigidbody rb;
    private Vector3 previousPosition;

    private PlayerController playerController;
    private float lastSpeed;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = mass;

        holdPoint = new GameObject();
        holdPoint.transform.parent = this.transform;
        holdPoint.name = "HoldPoint";
    }

    public override void Interact(InteractionContext context)
    {
        if (context.HoldPoint == null) return;

        holdPoint.transform.position = context.HoldPoint.position;
        holdPoint.transform.parent = context.PlayerCameraTransform;

        playerController = context.PlayerController;

        StartDragging();
    }

    protected void Update()
    {
        //test
        if (Keyboard.current.eKey.wasReleasedThisFrame && isDragging)
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
        lastSpeed = playerController.GetSpeed();
        playerController.SetSpeed(lastSpeed / (1 + mass));

        StartCoroutine(ChangeValue(mass));
        mass = 0.1f;

        isDragging = true;
        rb.useGravity = false;
    }

    protected void Dragging()
    {
        previousPosition = rb.position;
        rb.position = Vector3.Lerp(rb.position, holdPoint.transform.position, Time.fixedDeltaTime / mass);
        if (Vector3.Distance(rb.position, holdPoint.transform.position) > maxDraggingDistance)
        {
            StopDragging();
        }
    }

    protected void StopDragging()
    {
        playerController.SetSpeed(lastSpeed);

        isDragging = false;
        rb.linearVelocity = (rb.position - previousPosition) / Time.fixedDeltaTime;
        rb.useGravity = true;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        if (holdPoint != null)
            Gizmos.DrawSphere(holdPoint.transform.position, 0.1f);
       
        if (rb != null)
            Gizmos.DrawSphere(rb.position, 0.1f);
    }

    IEnumerator ChangeValue(float targetValue)
    {
        float startValue = 0.1f;
        float elapsed = 0f;
        float duration = 0.4f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            mass = Mathf.Lerp(startValue, targetValue, elapsed / duration);
            yield return null;
        }

        mass = targetValue;
    }
}
