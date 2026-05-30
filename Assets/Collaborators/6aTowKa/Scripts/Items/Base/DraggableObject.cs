using System.Collections;
using Unity.Netcode;
using UnityEngine;

public abstract class DraggableObject : InteractableObject
{
    private NetworkVariable<bool> netIsDragging = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    protected Rigidbody rb { get; private set; }
    protected PlayerInteraction playerInteraction;

    private Transform holdPointTransform;
    private PlayerController playerController;
    private float originalPlayerSpeed;
    private float originalMass;

    private Vector3[] velocityBuffer;
    private int velocityBufferIndex;
    private Vector3 previousPosition;

    private void OnValidate()
    {
        if (data != null)
        {
            if (data is not DraggableObjectData)
                Debug.LogError($"Data for {name} must be of type DraggableObjectData!", this);
        }
    }

    private void Awake()
    {        
        rb = GetComponent<Rigidbody>();
        rb.mass = ((DraggableObjectData)data).Mass;
        originalMass = ((DraggableObjectData)data).Mass;

        int samples = Mathf.Max(1, (int)((DraggableObjectData)data).ThrowVelocitySamples);
        velocityBuffer = new Vector3[samples]; //create buffer
    }

    private void FixedUpdate()
    {
        if (!netIsDragging.Value) return;

        if (IsClient && IsOwner)
        {
            TickDragging();
        }
    }

    public override void OnInteract(InteractionContext context)
    {
        Interact(context);
    }

    public void OnUninteract()
    {
        Uninteract();
    }

    private void Interact(InteractionContext context)
    {
        if (netIsDragging.Value) return;
        if (NetworkManager.Singleton == null) return;

        if (context.HitPoint == null) return;

        if (holdPointTransform == null)
        {
            var go = new GameObject("HoldPoint_Local");
            holdPointTransform = go.transform;
        }
        playerInteraction = context.PlayerInteraction;

        holdPointTransform.position = context.HitPoint.position;
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

    private void Uninteract()
    {
        if (netIsDragging.Value)
            StopDragging(applyThrow: true);
    }

    private void SetupPlayerBeforeDragging()
    {
        originalPlayerSpeed = playerController.GetSpeed();
        float speedMultiplier = 1f / (1f + ((DraggableObjectData)data).Mass * 0.3f);
        playerController.SetSpeed(originalPlayerSpeed * speedMultiplier);
    }

    private void RestorePlayerAfterDragging()
    {
        if (playerController != null)
            playerController.SetSpeed(originalPlayerSpeed);
    }

    private void CheckStartDistance(Vector3 rayOrigin)
    {
        if (holdPointTransform == null) return;

        float distance = Vector3.Distance(rayOrigin, holdPointTransform.transform.position);
        if (distance < ((DraggableObjectData)data).MinDistance)
        {
            holdPointTransform.position = rayOrigin + (holdPointTransform.position - rayOrigin).normalized * ((DraggableObjectData)data).MinDistance;
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

        if (distanceToTarget > ((DraggableObjectData)data).MaxDragDistance)
        {
            StopDragging(applyThrow: false);
            return;
        }

        Vector3 frameVelocity = (rb.position - previousPosition) / Time.fixedDeltaTime;
        velocityBuffer[velocityBufferIndex % velocityBuffer.Length] = frameVelocity;
        velocityBufferIndex++;
        previousPosition = rb.position;

        Vector3 delta = targetPos - rb.position;
        Vector3 desiredSpeed = delta * ((DraggableObjectData)data).FollowSpeedMultiplier;
        rb.linearVelocity = Vector3.ClampMagnitude(desiredSpeed, ((DraggableObjectData)data).MaxFollowSpeed);
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
        playerInteraction.Undrag();
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
            Gizmos.DrawWireSphere(holdPointTransform.position, ((DraggableObjectData)data).MaxDragDistance);
    }
}
