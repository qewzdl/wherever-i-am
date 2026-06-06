using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class ServerObservedMovementNoiseValidator :
    NetworkBehaviour,
    IGameplayNoiseRequestValidator
{
    private const float MinimumAllowedSpeed = 0.01f;
    private const float MinimumAllowedDistance = 0.01f;
    private const float MinimumAllowedGraceDuration = 0.05f;

    [Header("Observation")]
    [SerializeField] private Transform observedTransform;
    [SerializeField, Min(MinimumAllowedSpeed)] private float minimumHorizontalSpeed = 0.1f;
    [SerializeField, Min(MinimumAllowedDistance)] private float minimumDistancePerNoise = 0.5f;
    [SerializeField, Min(MinimumAllowedDistance)] private float maximumBufferedDistance = 1f;
    [SerializeField, Min(MinimumAllowedGraceDuration)] private float movementGraceDuration = 0.25f;

    private Vector3 previousPosition;
    private float previousSampleTime;
    private float lastObservedMovementTime = float.NegativeInfinity;
    private float bufferedHorizontalDistance;
    private bool hasPreviousSample;

    public override void OnNetworkSpawn()
    {
        ResetObservation();
    }

    public override void OnNetworkDespawn()
    {
        ResetObservation();
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        ObserveMovement();
    }

    public bool CanEmitNoiseServer(
        GameplayNoiseEmitter emitter,
        ulong senderClientId
    )
    {
        if (!IsServer ||
            !IsSpawned ||
            !isActiveAndEnabled ||
            emitter == null ||
            emitter.NetworkObject != NetworkObject)
        {
            return false;
        }

        if (senderClientId != OwnerClientId)
        {
            return false;
        }

        if (Time.time - lastObservedMovementTime > movementGraceDuration ||
            bufferedHorizontalDistance < minimumDistancePerNoise)
        {
            return false;
        }

        bufferedHorizontalDistance = Mathf.Max(
            0f,
            bufferedHorizontalDistance - minimumDistancePerNoise
        );

        return true;
    }

    private void ObserveMovement()
    {
        Transform target = observedTransform != null
            ? observedTransform
            : transform;

        Vector3 currentPosition = target.position;
        float currentTime = Time.time;

        if (!hasPreviousSample)
        {
            previousPosition = currentPosition;
            previousSampleTime = currentTime;
            hasPreviousSample = true;
            return;
        }

        float deltaTime = currentTime - previousSampleTime;

        if (deltaTime > Mathf.Epsilon)
        {
            Vector3 displacement = currentPosition - previousPosition;
            displacement.y = 0f;

            float horizontalSpeed = displacement.magnitude / deltaTime;

            if (horizontalSpeed >= minimumHorizontalSpeed)
            {
                lastObservedMovementTime = currentTime;
                bufferedHorizontalDistance = Mathf.Min(
                    maximumBufferedDistance,
                    bufferedHorizontalDistance + displacement.magnitude
                );
            }
        }

        previousPosition = currentPosition;
        previousSampleTime = currentTime;
    }

    private void ResetObservation()
    {
        previousPosition = default;
        previousSampleTime = 0f;
        lastObservedMovementTime = float.NegativeInfinity;
        bufferedHorizontalDistance = 0f;
        hasPreviousSample = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        minimumHorizontalSpeed = Mathf.Max(
            MinimumAllowedSpeed,
            minimumHorizontalSpeed
        );

        minimumDistancePerNoise = Mathf.Max(
            MinimumAllowedDistance,
            minimumDistancePerNoise
        );

        maximumBufferedDistance = Mathf.Max(
            minimumDistancePerNoise,
            maximumBufferedDistance
        );

        movementGraceDuration = Mathf.Max(
            MinimumAllowedGraceDuration,
            movementGraceDuration
        );
    }
#endif
}
