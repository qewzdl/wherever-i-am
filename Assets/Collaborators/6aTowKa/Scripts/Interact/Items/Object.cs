using System.Collections;
using Unity.Netcode;
using UnityEngine;

public abstract class DraggingObject : InteractableObject
{
    [Header("Physics")]
    [SerializeField] protected float mass = 1f;
    [SerializeField] private float followSpeed = 15f;       
    [SerializeField] private float maxDragDistance = 6f;    
    [SerializeField] private float throwVelocitySamples = 5;
    [SerializeField] private float minDistance = 1f;

    private NetworkVariable<bool> netIsDragging = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    protected Rigidbody rb { get; private set; }

    private Transform holdPointTransform; 
    private PlayerController playerController;
    private float originalPlayerSpeed;
    private float originalMass;

    private Vector3[] velocityBuffer;
    private int velocityBufferIndex;
    private Vector3 previousPosition;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = mass;
        originalMass = mass;

        int samples = Mathf.Max(1, (int)throwVelocitySamples);
        velocityBuffer = new Vector3[samples]; //create buffer
    }

    protected virtual void FixedUpdate()
    {
        if (!netIsDragging.Value) return;

        if (IsClient && IsOwner)
        {
            TickDragging();
        }
    }

    public override void Interact(InteractionContext context)
    {
        if (netIsDragging.Value) return;
        if (NetworkManager.Singleton == null) return;

        if (context.HoldPoint == null) return;

        if (holdPointTransform == null)
        {
            var go = new GameObject("HoldPoint_Local");
            holdPointTransform = go.transform;
        }

        holdPointTransform.position = context.HoldPoint.position;
        CheckStartDistance(context.RayOriginPosition);

        holdPointTransform.SetParent(context.PlayerCameraTransform, worldPositionStays: true);

        playerController = context.PlayerController;
        SetupPlayerBeforeDragging();

        if (!IsOwner) // change ownership
        {
            RequestOwnershipServerRpc(NetworkManager.Singleton.LocalClientId);
        }
        else
        {
            StartDragging();
        }
    }

    public void Uninteract()
    {
        if (netIsDragging.Value)
            StopDragging(applyThrow: true);
    }

    protected virtual void SetupPlayerBeforeDragging()
    {
        originalPlayerSpeed = playerController.GetSpeed();
        float speedMultiplier = 1f / (1f + mass * 0.3f);
        playerController.SetSpeed(originalPlayerSpeed * speedMultiplier);
    }

    protected virtual void RestorePlayerAfterDragging()
    {
        if (playerController != null)
            playerController.SetSpeed(originalPlayerSpeed);
    }

    private void CheckStartDistance(Vector3 rayOrigin)
    {
        if (holdPointTransform == null) return;

        float distance = Vector3.Distance(rayOrigin, holdPointTransform.transform.position);
        if (distance < minDistance)
        {
            holdPointTransform.position = rayOrigin + (holdPointTransform.position - rayOrigin).normalized * minDistance;
        }
    }

    // ServerRpc 

    [Rpc(SendTo.Server)]
    private void RequestOwnershipServerRpc(ulong requestingClientId)
    {
        if (netIsDragging.Value) return;

        GetComponent<NetworkObject>().ChangeOwnership(requestingClientId);
        StartDraggingOwnerRpc();
    }

    [Rpc(SendTo.Owner)]
    private void StartDraggingOwnerRpc()
    {
        StartDragging();
    }


    // Client Logic 

    private void StartDragging()
    {
        if (netIsDragging.Value) return;

        netIsDragging.Value = true;
        rb.useGravity = false;
        rb.linearDamping = 5f;
        rb.angularDamping = 10f;

        originalMass = rb.mass;
        rb.mass = 0.1f;

        previousPosition = rb.position;
        ResetVelocityBuffer();

        StartCoroutine(LerpMassCoroutine(from: rb.mass, to: originalMass, duration: 0.4f));
    }

    private void TickDragging()
    {
        Vector3 targetPos = holdPointTransform.position;
        float distanceToTarget = Vector3.Distance(rb.position, targetPos);

        if (distanceToTarget > maxDragDistance)
        {
            StopDragging(applyThrow: false);
            return;
        }

        Vector3 frameVelocity = (rb.position - previousPosition) / Time.fixedDeltaTime;
        velocityBuffer[velocityBufferIndex % velocityBuffer.Length] = frameVelocity;
        velocityBufferIndex++;
        previousPosition = rb.position;

        Vector3 newPos = Vector3.Lerp(rb.position, targetPos, Time.fixedDeltaTime * ( followSpeed/(1 + mass) ) );
        rb.MovePosition(newPos);
    }

    private void StopDragging(bool applyThrow)
    {
        StopAllCoroutines();

        netIsDragging.Value = false;
        rb.useGravity = true;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.05f;
        rb.mass = originalMass;

        if (applyThrow)
        {
            rb.linearVelocity = GetAveragedVelocity();
        }
        else
        {
            rb.linearVelocity = Vector3.zero;
        }

        RestorePlayerAfterDragging();
        CleanupHoldPoint();
    }

    // Coroutines 

    private IEnumerator LerpMassCoroutine(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.fixedDeltaTime;
            rb.mass = Mathf.Lerp(from, to, elapsed / duration);
            yield return new WaitForFixedUpdate();
        }
        rb.mass = to;
    }

    // Utilities 

    private void CleanupHoldPoint()
    {
        if (holdPointTransform != null)
        {
            holdPointTransform.SetParent(null);
            Destroy(holdPointTransform.gameObject);
            holdPointTransform = null;
        }
    }

    private void ResetVelocityBuffer()
    {
        velocityBufferIndex = 0;
        for (int i = 0; i < velocityBuffer.Length; i++)
            velocityBuffer[i] = Vector3.zero;
    }

    private Vector3 GetAveragedVelocity()
    {
        Vector3 sum = Vector3.zero;
        foreach (var v in velocityBuffer)
            sum += v;
        return sum / velocityBuffer.Length;
    }


    // Gizmos 

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        if (holdPointTransform != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(holdPointTransform.position, 0.12f);
            Gizmos.DrawLine(transform.position, holdPointTransform.position);
        }

        if (rb != null)
        {
            Gizmos.color = netIsDragging.Value ? Color.yellow : Color.red;
            Gizmos.DrawWireSphere(rb.position, 0.12f);
        }

        // Радиус автосброса
        Gizmos.color = new Color(1, 0, 0, 0.1f);
        if (holdPointTransform != null)
            Gizmos.DrawWireSphere(holdPointTransform.position, maxDragDistance);
    }

}